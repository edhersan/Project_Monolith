using System.Diagnostics;
using Monolith.Core.Interfaces;
using Monolith.Voice.Models;
using NAudio.Wave;

namespace Monolith.Voice.Services;

public class VadCaptureService : ISTTService, IDisposable
{
    private readonly GoogleSpeechSTT _stt;
    private readonly IVadDetector _vad;
    private readonly VadConfig _config;
    private readonly VadMetrics _metrics;
    private readonly CircularAudioBuffer _circularBuffer;
    private readonly int _frameSizeBytes;
    private readonly int _preRollBytes;
    private readonly int _postRollFrames;
    private readonly int _sampleRate;

    private enum VadState { Idle, Speech, Flushing }
    private VadState _state = VadState.Idle;
    private int _speechStartBufferPos;
    private int _flushFramesRemaining;
    private long _utteranceStartTimestamp;
    private long _lastSpeechTimestamp;

    public event Action<byte[]>? OnUtteranceReady;

    public VadCaptureService(
        GoogleSpeechSTT stt,
        IVadDetector vad,
        VadConfig config)
    {
        _stt = stt;
        _vad = vad;
        _config = config;
        _metrics = new VadMetrics();
        _sampleRate = 16000;
        _frameSizeBytes = vad.FrameSizeSamples * 2;
        _preRollBytes = (config.PreRollMs * _sampleRate / 1000) * 2;
        _postRollFrames = config.PostRollMs / config.FrameMs;
        var bufferCapacity = (_preRollBytes + (_sampleRate * 30 * 2));
        _circularBuffer = new CircularAudioBuffer(bufferCapacity);
    }

    public VadMetrics Metrics => _metrics;

    public async Task<string?> ListenAsync(CancellationToken ct = default)
    {
        ResetState();

        var tcs = new TaskCompletionSource<string?>();
        var tcsLock = new object();

        using var capture = new NaudioVadCapture(_sampleRate, _config.FrameMs);

        void OnData(byte[] buffer, int bytesRecorded)
        {
            if (ct.IsCancellationRequested)
            {
                TrySetResult(null);
                return;
            }
            ProcessAudioChunk(buffer, bytesRecorded, tcs, tcsLock);
        }

        void OnStopped() => TrySetResult(null);

        void TrySetResult(string? result)
        {
            lock (tcsLock)
            {
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(result);
            }
        }

        capture.OnAudioData += OnData;
        capture.OnRecordingStopped += OnStopped;

        Console.WriteLine("[VAD] Escuchando (deteccion de voz activa)...");
        capture.Start();

        try
        {
            using var reg = ct.Register(() => capture.Stop());
            return await tcs.Task;
        }
        finally
        {
            capture.OnAudioData -= OnData;
            capture.OnRecordingStopped -= OnStopped;
            capture.Stop();
        }
    }

    private void ResetState()
    {
        _state = VadState.Idle;
        _flushFramesRemaining = 0;
        _circularBuffer.Clear();
    }

    private void ProcessAudioChunk(
        byte[] buffer, int bytesRecorded,
        TaskCompletionSource<string?> tcs, object tcsLock)
    {
        _circularBuffer.Write(buffer.AsSpan(0, bytesRecorded));

        for (int offset = 0; offset + _frameSizeBytes <= bytesRecorded; offset += _frameSizeBytes)
        {
            var frameSamples = new short[_vad.FrameSizeSamples];
            Buffer.BlockCopy(buffer, offset, frameSamples, 0, _frameSizeBytes);

            var isSpeech = _vad.IsSpeech(frameSamples, _sampleRate);

            _metrics.FramesProcessed++;
            if (isSpeech)
                _metrics.SpeechFrames++;
            else
                _metrics.SilenceFrames++;

            ProcessVadFrame(isSpeech, tcs, tcsLock);
        }
    }

    private void ProcessVadFrame(
        bool isSpeech,
        TaskCompletionSource<string?> tcs, object tcsLock)
    {
        switch (_state)
        {
            case VadState.Idle:
                if (isSpeech)
                {
                    _state = VadState.Speech;
                    _speechStartBufferPos = Math.Max(0, _circularBuffer.Count - _preRollBytes);
                    _utteranceStartTimestamp = Stopwatch.GetTimestamp();
                    _lastSpeechTimestamp = _utteranceStartTimestamp;
                }
                break;

            case VadState.Speech:
                if (isSpeech)
                {
                    _lastSpeechTimestamp = Stopwatch.GetTimestamp();
                    _flushFramesRemaining = 0;
                }
                else
                {
                    if (_flushFramesRemaining == 0)
                        _flushFramesRemaining = _postRollFrames;

                    _flushFramesRemaining--;
                    if (_flushFramesRemaining <= 0)
                        FinalizeUtterance(tcs, tcsLock);
                }
                break;

            case VadState.Flushing:
                break;
        }
    }

    private void FinalizeUtterance(
        TaskCompletionSource<string?> tcs, object tcsLock)
    {
        _state = VadState.Flushing;
        var utteranceLengthMs = (Stopwatch.GetTimestamp() - _utteranceStartTimestamp)
            * 1000.0 / Stopwatch.Frequency;

        var bufferCopy = _circularBuffer.ReadAll();
        var utteranceStart = Math.Max(0, _speechStartBufferPos);
        var utteranceBytes = bufferCopy.Length - utteranceStart;

        if (utteranceBytes < _frameSizeBytes * 2)
        {
            _state = VadState.Idle;
            _metrics.RecordFalseTrigger();
            return;
        }

        var utterance = new byte[utteranceBytes];
        Buffer.BlockCopy(bufferCopy, utteranceStart, utterance, 0, utteranceBytes);

        OnUtteranceReady?.Invoke(utterance);

        var sttStart = Stopwatch.GetTimestamp();
        _ = TranscribeAndCompleteAsync(utterance, utteranceLengthMs, sttStart, tcs, tcsLock);
    }

    private async Task TranscribeAndCompleteAsync(
        byte[] utterance, double utteranceLengthMs, long sttStart,
        TaskCompletionSource<string?> tcs, object tcsLock)
    {
        try
        {
            var sttLatencyMs = (Stopwatch.GetTimestamp() - sttStart) * 1000.0 / Stopwatch.Frequency;
            var text = await _stt.RecognizeAsync(utterance, CancellationToken.None);
            sttLatencyMs = (Stopwatch.GetTimestamp() - sttStart) * 1000.0 / Stopwatch.Frequency;

            _metrics.RecordUtterance(utteranceLengthMs, sttLatencyMs);

            lock (tcsLock)
            {
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(text);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VAD STT ERROR] {ex.Message}");
            _metrics.RecordFalseTrigger();
            lock (tcsLock)
            {
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(null);
            }
        }
    }

    public void Dispose()
    {
        _stt.Dispose();
        _vad.Dispose();
    }
}
