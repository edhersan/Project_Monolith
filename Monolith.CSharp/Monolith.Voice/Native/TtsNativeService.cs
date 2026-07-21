using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;
using NAudio.Wave;

namespace Monolith.Voice.Native;

public class TtsNativeService : ITTSService, IDisposable
{
    private readonly BufferedWaveProvider _buffer;
    private readonly WaveOutEvent _output;
    private readonly Channel<byte[]> _packetChannel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _consumerTask;
    private readonly PipelineMetrics? _metrics;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _opusBitrate;
    private readonly int _maxConcurrency;
    private readonly string _modelPath;

    private IntPtr _handle;
    private bool _nativeAvailable;
    private TaskCompletionSource? _pendingEos;

    // Keep delegates alive (prevent GC)
    private static readonly OnOpusPacketDelegate PacketCb = OnPacket;
    private static readonly OnLogDelegate LogCb = OnLog;
    private GCHandle _selfHandle;

    private bool _disposed;

    public TtsNativeService(
        string modelPath = "",
        int sampleRate = 48000,
        int channels = 1,
        int opusBitrate = 48000,
        int maxConcurrency = 1,
        PipelineMetrics? metrics = null)
    {
        _metrics = metrics;
        _modelPath = modelPath;
        _sampleRate = sampleRate;
        _channels = channels;
        _opusBitrate = opusBitrate;
        _maxConcurrency = maxConcurrency;

        _selfHandle = GCHandle.Alloc(this);

        _packetChannel = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

        var format = new WaveFormat(sampleRate, 16, channels);
        _buffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true
        };

        _output = new WaveOutEvent();
        _output.Init(_buffer);
        _output.Play();

        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumerLoop(_cts.Token));
    }

    private bool EnsureNativeLoaded()
    {
        if (_nativeAvailable) return true;
        if (_disposed) return false;

        try
        {
            var modelPtr = Marshal.StringToCoTaskMemAnsi(_modelPath);
            var cfg = new TtsConfig
            {
                ModelPath = modelPtr,
                SampleRate = _sampleRate,
                Channels = _channels,
                OpusBitrate = _opusBitrate,
                MaxConcurrency = _maxConcurrency
            };

            _handle = NativeMethods.tts_create(ref cfg, PacketCb, LogCb, GCHandle.ToIntPtr(_selfHandle));
            Marshal.FreeCoTaskMem(modelPtr);

            _nativeAvailable = _handle != IntPtr.Zero;

            if (!_nativeAvailable)
                Console.WriteLine("[NativeTTS] tts_create returned NULL");
            else
                Console.WriteLine("[NativeTTS] DLL cargada correctamente");

            return _nativeAvailable;
        }
        catch (DllNotFoundException ex)
        {
            Console.WriteLine($"[NativeTTS] DLL no encontrada: {ex.Message}");
            return false;
        }
        catch (BadImageFormatException ex)
        {
            Console.WriteLine($"[NativeTTS] DLL invalida: {ex.Message}");
            return false;
        }
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!EnsureNativeLoaded())
        {
            Console.WriteLine("[NativeTTS] No disponible, omitiendo síntesis");
            return;
        }

        var sw = Stopwatch.StartNew();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Interlocked.Exchange(ref _pendingEos, tcs);

        var result = NativeMethods.tts_speak_async(_handle, text, null, 0);
        if (result != 0)
        {
            Console.WriteLine($"[NativeTTS] tts_speak_async returned {result}");
            tcs.TrySetResult();
        }

        try
        {
            await tcs.Task.WaitAsync(ct);

            sw.Stop();
            var latencyMs = sw.ElapsedMilliseconds;
            Console.WriteLine($"[NativeTTS] OK ({latencyMs}ms)");
            _metrics?.RecordTtsLatency(latencyMs);

            // Wait for buffer to drain
            while (_buffer.BufferedDuration.TotalMilliseconds > 20 && !ct.IsCancellationRequested)
                await Task.Delay(20, ct);
        }
        catch (OperationCanceledException)
        {
            if (_handle != IntPtr.Zero)
                NativeMethods.tts_stop(_handle, 0);
            Console.WriteLine("[NativeTTS] Cancelado");
        }
    }

    private async Task ConsumerLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var packet in _packetChannel.Reader.ReadAllAsync(ct))
            {
                _buffer.AddSamples(packet, 0, packet.Length);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnPacketReceived(IntPtr data, int len)
    {
        if (len <= 0 || data == IntPtr.Zero)
        {
            Interlocked.Exchange(ref _pendingEos, null)?.TrySetResult();
            return;
        }

        var buf = new byte[len];
        Marshal.Copy(data, buf, 0, len);
        _packetChannel.Writer.TryWrite(buf);
    }

    private static void OnLogReceived(string msg)
    {
        Console.WriteLine($"[NativeTTS] {msg}");
    }

    private static void OnPacket(IntPtr data, int len, IntPtr user)
    {
        var target = GCHandle.FromIntPtr(user).Target as TtsNativeService;
        target?.OnPacketReceived(data, len);
    }

    private static void OnLog(IntPtr msg, IntPtr user)
    {
        var str = Marshal.PtrToStringAnsi(msg);
        if (str == null) return;
        OnLogReceived(str);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _packetChannel.Writer.TryComplete();

        try { _consumerTask.Wait(2000); } catch { }

        if (_handle != IntPtr.Zero)
            NativeMethods.tts_destroy(_handle);

        _output?.Stop();
        _output?.Dispose();

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();

        _cts.Dispose();
    }
}
