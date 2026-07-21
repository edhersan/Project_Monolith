using System.Diagnostics;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;
using NAudio.Wave;

namespace Monolith.Voice.Services;

public class TtsOnnxService : ITTSService, IDisposable
{
    private readonly string _modelPath;
    private int _sampleRate;
    private readonly int _speakerId;
    private readonly float _noiseScale;
    private readonly float _lengthScale;
    private readonly float _noiseW;
    private readonly PipelineMetrics? _metrics;

    private InferenceSession? _session;
    private int _numSpeakers;
    private bool _disposed;

    public TtsOnnxService(
        string modelPath,
        int sampleRate = 22050,
        int speakerId = 0,
        float noiseScale = 0.667f,
        float lengthScale = 1.0f,
        float noiseW = 0.8f,
        PipelineMetrics? metrics = null)
    {
        _modelPath = modelPath;
        _sampleRate = sampleRate;
        _speakerId = speakerId;
        _noiseScale = noiseScale;
        _lengthScale = lengthScale;
        _noiseW = noiseW;
        _metrics = metrics;
    }

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();

            try
            {
                ct.ThrowIfCancellationRequested();
                EnsureModelLoaded();

                var audio = GenerateAudio(text, ct);
                if (audio == null || audio.Length == 0) return;

                Console.WriteLine("[TTS ONNX] Reproduciendo...");
                PlayPcm(audio);

                sw.Stop();
                Console.WriteLine($"[TTS ONNX] OK ({sw.ElapsedMilliseconds}ms, {audio.Length / (_sampleRate * 2)}s)");
                _metrics?.RecordTtsLatency(sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[TTS ONNX] Cancelado");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS ONNX ERROR] {ex.Message}");
            }
        }, ct);
    }

    private void EnsureModelLoaded()
    {
        if (_session != null) return;

        var modelFile = Path.Combine(_modelPath, "model.onnx");
        var configFile = Path.Combine(_modelPath, "config.json");

        if (!File.Exists(modelFile))
        {
            Console.WriteLine($"[TTS ONNX] Modelo no encontrado: {modelFile}");
            Console.WriteLine("[TTS ONNX] Descarga un modelo Piper TTS desde https://huggingface.co/rhasspy/piper-voices");
            Console.WriteLine("[TTS ONNX] Coloca model.onnx y config.json en: " + _modelPath);
            throw new FileNotFoundException("Modelo ONNX no encontrado", modelFile);
        }

        if (File.Exists(configFile))
        {
            var configJson = File.ReadAllText(configFile);
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("audio", out var audio))
            {
                if (audio.TryGetProperty("sample_rate", out var sr))
                    _sampleRate = sr.GetInt32();
            }

            if (root.TryGetProperty("num_speakers", out var ns))
                _numSpeakers = ns.GetInt32();
            else
                _numSpeakers = 1;

            Console.WriteLine($"[TTS ONNX] Modelo: sr={_sampleRate} speakers={_numSpeakers}");
        }

        var opts = new SessionOptions();
        _session = new InferenceSession(modelFile, opts);
        Console.WriteLine($"[TTS ONNX] Modelo cargado: {Path.GetFileName(modelFile)}");
    }

    private short[]? GenerateAudio(string text, CancellationToken ct)
    {
        if (_session == null) return null;

        var phones = TextToPhonemes(text);
        if (phones.Length == 0) return null;

        var inputIds = new long[phones.Length];
        for (int i = 0; i < phones.Length; i++)
            inputIds[i] = phones[i];

        var inputLengths = new long[] { inputIds.Length };
        var scales = new float[] { _noiseScale, _lengthScale, _noiseW };

        var inputTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
        var lengthTensor = new DenseTensor<long>(inputLengths, new[] { 1 });
        var scalesTensor = new DenseTensor<float>(scales, new[] { 3 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("input_lengths", lengthTensor),
            NamedOnnxValue.CreateFromTensor("scales", scalesTensor),
        };

        if (_numSpeakers > 1)
        {
            var sidTensor = new DenseTensor<long>(new[] { (long)_speakerId }, new[] { 1 });
            inputs.Add(NamedOnnxValue.CreateFromTensor("sid", sidTensor));
        }

        using var results = _session.Run(inputs);
        var audioTensor = results.FirstOrDefault()?.AsTensor<float>();
        if (audioTensor == null) return null;

        var audio = audioTensor.ToArray();
        var pcm = new short[audio.Length];
        for (int i = 0; i < audio.Length; i++)
        {
            var sample = audio[i] * 32767f;
            if (sample > 32767f) sample = 32767f;
            if (sample < -32768f) sample = -32768f;
            pcm[i] = (short)sample;
        }

        return pcm;
    }

    private static long[] TextToPhonemes(string text)
    {
        // Approximate grapheme-to-phoneme for Spanish
        // This is a simplified mapping for Piper/VITS models
        var result = new List<long>();

        foreach (var ch in text.Normalize().ToLowerInvariant())
        {
            if (ch == ' ') { result.Add(0); continue; }

            var code = SimplePhonemeMap(ch);
            if (code >= 0) result.Add(code);
        }

        return result.Count > 0 ? result.ToArray() : new long[] { 0 };
    }

    private static int SimplePhonemeMap(char ch)
    {
        // Simple character-to-ID mapping for Spanish
        return ch switch
        {
            'a' => 1, 'b' => 2, 'c' => 3, 'd' => 4, 'e' => 5,
            'f' => 6, 'g' => 7, 'h' => 8, 'i' => 9, 'j' => 10,
            'k' => 11, 'l' => 12, 'm' => 13, 'n' => 14, 'o' => 15,
            'p' => 16, 'q' => 17, 'r' => 18, 's' => 19, 't' => 20,
            'u' => 21, 'v' => 22, 'w' => 23, 'x' => 24, 'y' => 25,
            'z' => 26,
            'á' => 27, 'é' => 28, 'í' => 29, 'ó' => 30, 'ú' => 31,
            'ü' => 32, 'ñ' => 33,
            _ => -1
        };
    }

    private static void PlayPcm(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);

        var format = new WaveFormat(22050, 16, 1);
        using var provider = new RawSourceWaveStream(new MemoryStream(bytes), format);
        using var output = new WaveOutEvent();
        output.Init(provider);
        output.Play();

        while (output.PlaybackState == PlaybackState.Playing)
            Thread.Sleep(50);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
    }
}
