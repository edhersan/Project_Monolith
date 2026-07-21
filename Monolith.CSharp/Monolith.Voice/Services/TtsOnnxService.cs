using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;
using NAudio.Wave;

namespace Monolith.Voice.Services;

public partial class TtsOnnxService : ITTSService, IDisposable
{
    private readonly string _modelRoot;
    private readonly string? _selectedVoice;
    private readonly int _speakerId;
    private float _noiseScale;
    private float _lengthScale;
    private float _noiseW;
    private readonly PipelineMetrics? _metrics;

    private string? _resolvedVoicePath;
    private int _sampleRate = 22050;
    private InferenceSession? _session;
    private int _numSpeakers = 1;
    private bool _disposed;

    private Dictionary<char, int> _charToId = new();
    private int _blankId;
    private bool _addBlank;

    private Dictionary<string, long[]>? _phonemeIdMap;
    private TtsPhonemizerService? _phonemizer;
    private string _espeakVoice = "es";

    [GeneratedRegex(@"[\<\>\(\)\[\]\""]+")]
    private static partial Regex StripBrackets();

    public TtsOnnxService(
        string modelRoot,
        string? selectedVoice = null,
        int speakerId = 0,
        float noiseScale = 0.667f,
        float lengthScale = 1.0f,
        float noiseW = 0.8f,
        PipelineMetrics? metrics = null)
    {
        _modelRoot = modelRoot;
        _selectedVoice = selectedVoice;
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
        ResolveVoice();
        LoadModel();
    }

    private void ResolveVoice()
    {
        if (File.Exists(Path.Combine(_modelRoot, "model.onnx")))
        {
            _resolvedVoicePath = _modelRoot;
            Console.WriteLine($"[TTS ONNX] Voz: {Path.GetFileName(_modelRoot)}");
            return;
        }

        var voiceDirs = Directory.GetDirectories(_modelRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "model.onnx")))
            .OrderBy(Path.GetFileName)
            .ToList();

        if (voiceDirs.Count == 0)
        {
            Console.WriteLine("[TTS ONNX] No se encontraron voces en: " + _modelRoot);
            Console.WriteLine("[TTS ONNX] Coloca cada voz en su propia carpeta:");
            Console.WriteLine("[TTS ONNX]   " + Path.Combine(_modelRoot, "es_MX-mi-voz", "model.onnx"));
            Console.WriteLine("[TTS ONNX]   " + Path.Combine(_modelRoot, "es_MX-mi-voz", "config.json"));
            throw new FileNotFoundException("No se encontraron modelos ONNX", _modelRoot);
        }

        string? selectedDir = null;

        if (!string.IsNullOrEmpty(_selectedVoice))
        {
            selectedDir = voiceDirs.FirstOrDefault(d =>
                Path.GetFileName(d).Equals(_selectedVoice, StringComparison.OrdinalIgnoreCase));

            if (selectedDir == null)
            {
                Console.WriteLine($"[TTS ONNX] Voz '{_selectedVoice}' no encontrada.");
                ListVoices(voiceDirs);
                throw new FileNotFoundException(
                    $"Voz '{_selectedVoice}' no encontrada. Revisa TtsOnnxSelectedVoice en appsettings.json");
            }

            Console.WriteLine($"[TTS ONNX] Voz: {_selectedVoice}");
        }
        else if (voiceDirs.Count == 1)
        {
            selectedDir = voiceDirs[0];
            Console.WriteLine($"[TTS ONNX] Voz auto-detectada: {Path.GetFileName(selectedDir)}");
        }
        else
        {
            Console.WriteLine("[TTS ONNX] Varias voces. Escribe el numero y presiona Enter:");
            for (int i = 0; i < voiceDirs.Count; i++)
                Console.WriteLine($"  [{i + 1}] {Path.GetFileName(voiceDirs[i])}");

            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();

            if (int.TryParse(input, out var idx) && idx >= 1 && idx <= voiceDirs.Count)
                selectedDir = voiceDirs[idx - 1];
            else
                selectedDir = voiceDirs[0];

            Console.WriteLine($"[TTS ONNX] Voz: {Path.GetFileName(selectedDir)}");
        }

        _resolvedVoicePath = selectedDir;
    }

    private static void ListVoices(List<string> voiceDirs)
    {
        Console.WriteLine("[TTS ONNX] Voces disponibles:");
        for (int i = 0; i < voiceDirs.Count; i++)
            Console.WriteLine($"  {i + 1}. {Path.GetFileName(voiceDirs[i])}");
    }

    private void LoadModel()
    {
        var modelFile = Path.Combine(_resolvedVoicePath!, "model.onnx");

        if (!File.Exists(modelFile))
            throw new FileNotFoundException("model.onnx no encontrado", modelFile);

        var configFile = FindConfigFile(_resolvedVoicePath!);

        if (configFile != null)
        {
            var configJson = File.ReadAllText(configFile);
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var sr))
            {
                _sampleRate = sr.GetInt32();
            }

            if (root.TryGetProperty("num_speakers", out var ns))
                _numSpeakers = ns.GetInt32();

            // Leer parametros de inferencia del config (top-level o nested "inference")
            JsonElement inference = default;
            var hasInference = root.TryGetProperty("inference", out inference);

            if (root.TryGetProperty("inference_noise_scale", out var ins))
                _noiseScale = ins.GetSingle();
            else if (hasInference && inference.TryGetProperty("noise_scale", out var ins2))
                _noiseScale = ins2.GetSingle();

            if (root.TryGetProperty("length_scale", out var ls))
                _lengthScale = ls.GetSingle();
            else if (hasInference && inference.TryGetProperty("length_scale", out var ls2))
                _lengthScale = ls2.GetSingle();

            if (root.TryGetProperty("inference_noise_scale_dp", out var nsdp))
                _noiseW = nsdp.GetSingle();
            else if (hasInference && inference.TryGetProperty("noise_w", out var nw))
                _noiseW = nw.GetSingle();

            // Detectar tipo de modelo
            if (root.TryGetProperty("phoneme_id_map", out var phonemeMap))
            {
                Console.WriteLine("[TTS ONNX] Modelo fonetico (Piper)");
                _phonemeIdMap = ParsePhonemeIdMap(phonemeMap);

                if (root.TryGetProperty("espeak", out var espeak) &&
                    espeak.TryGetProperty("voice", out var ev))
                {
                    _espeakVoice = ev.GetString() ?? "es";
                }

                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var exePath = Path.Combine(exeDir, "espeak-ng.exe");
                var dataPath = Path.Combine(exeDir, "espeak-ng-data");

                if (File.Exists(exePath) && Directory.Exists(dataPath))
                {
                    _phonemizer = new TtsPhonemizerService(exePath, dataPath);
                    Console.WriteLine($"[TTS ONNX] espeak-ng listo: voz={_espeakVoice} phonemes={_phonemeIdMap.Count}");
                }
                else
                {
                    Console.WriteLine("[TTS ONNX] espeak-ng no encontrado en output.");
                    Console.WriteLine("[TTS ONNX] Buscando: " + exePath);
                    throw new FileNotFoundException(
                        "espeak-ng.exe no encontrado. Copialo al directorio de salida.");
                }
            }
            else if (root.TryGetProperty("characters", out var characters))
            {
                BuildVocabFromConfig(characters);
            }
            else
            {
                Console.WriteLine("[TTS ONNX] Sin 'characters' en config. Usando vocabulario simple.");
                BuildSimpleVocab();
            }

            Console.WriteLine($"[TTS ONNX] sr={_sampleRate}Hz speakers={_numSpeakers} " +
                $"vocab={_charToId.Count} add_blank={_addBlank}");
        }
        else
        {
            Console.WriteLine("[TTS ONNX] Sin config.json. Usando mapa simple.");
            BuildSimpleVocab();
        }

        var opts = new SessionOptions();
        _session = new InferenceSession(modelFile, opts);
        Console.WriteLine($"[TTS ONNX] Cargado: {Path.GetFileName(modelFile)}");
    }

    private void BuildVocabFromConfig(JsonElement characters)
    {
        var pad = characters.TryGetProperty("pad", out var p) ? p.GetString() ?? "_" : "_";
        var punct = characters.TryGetProperty("punctuations", out var pu) ? pu.GetString() ?? "" : "";
        var chars = characters.TryGetProperty("characters", out var c) ? c.GetString() ?? "" : "";
        var blank = characters.TryGetProperty("blank", out var b) ? b.GetString() : null;
        if (string.IsNullOrEmpty(blank)) blank = "<BLNK>";

        var vocab = new List<string> { pad };
        vocab.AddRange(punct.Select(ch => ch.ToString()));
        vocab.AddRange(chars.Select(ch => ch.ToString()));
        vocab.Add(blank);

        _blankId = vocab.Count - 1;
        _addBlank = true;

        _charToId = new Dictionary<char, int>();
        for (int i = 0; i < vocab.Count; i++)
        {
            if (vocab[i].Length == 1)
                _charToId.TryAdd(vocab[i][0], i);
        }
    }

    private void BuildSimpleVocab()
    {
        _charToId = new Dictionary<char, int>
        {
            [' '] = 0,
            ['a'] = 1, ['b'] = 2, ['c'] = 3, ['d'] = 4, ['e'] = 5,
            ['f'] = 6, ['g'] = 7, ['h'] = 8, ['i'] = 9, ['j'] = 10,
            ['k'] = 11, ['l'] = 12, ['m'] = 13, ['n'] = 14, ['o'] = 15,
            ['p'] = 16, ['q'] = 17, ['r'] = 18, ['s'] = 19, ['t'] = 20,
            ['u'] = 21, ['v'] = 22, ['w'] = 23, ['x'] = 24, ['y'] = 25,
            ['z'] = 26,
            ['á'] = 27, ['é'] = 28, ['í'] = 29, ['ó'] = 30, ['ú'] = 31,
            ['ü'] = 32, ['ñ'] = 33,
        };
        _blankId = 0;
        _addBlank = false;
    }

    private short[]? GenerateAudio(string text, CancellationToken ct)
    {
        if (_session == null) return null;

        var ids = TextToIds(text);
        if (ids.Length == 0) return null;

        var inputLengths = new long[] { ids.Length };
        var scales = new float[] { _noiseScale, _lengthScale, _noiseW };

        var inputTensor = new DenseTensor<long>(ids, new[] { 1, ids.Length });
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

    private long[] TextToIds(string text)
    {
        // Phoneme model: usar espeak-ng
        if (_phonemeIdMap != null && _phonemizer != null)
        {
            var ids = _phonemizer.ToPhonemeIds(text, _espeakVoice, _phonemeIdMap);
            return ids ?? [0];
        }

        // Char-level model: usar vocabulario directo
        text = NormalizeText(text);

        var rawIds = new List<long>();
        foreach (var ch in text)
        {
            if (_charToId.TryGetValue(ch, out var id))
                rawIds.Add(id);
        }

        if (rawIds.Count == 0) return [0];

        if (!_addBlank) return rawIds.ToArray();

        var result = new List<long>(rawIds.Count * 2 + 1) { _blankId };
        foreach (var id in rawIds)
        {
            result.Add(id);
            result.Add(_blankId);
        }

        return result.ToArray();
    }

    private static string? FindConfigFile(string voicePath)
    {
        var configJson = Path.Combine(voicePath, "config.json");
        if (File.Exists(configJson)) return configJson;

        var onnxConfigs = Directory.GetFiles(voicePath, "*.onnx.json");
        if (onnxConfigs.Length > 0) return onnxConfigs[0];

        return null;
    }

    private static Dictionary<string, long[]> ParsePhonemeIdMap(JsonElement map)
    {
        var result = new Dictionary<string, long[]>();
        foreach (var entry in map.EnumerateObject())
        {
            var ids = new List<long>();
            foreach (var id in entry.Value.EnumerateArray())
                ids.Add(id.GetInt64());
            result[entry.Name] = ids.ToArray();
        }
        return result;
    }

    private static string NormalizeText(string text)
    {
        text = text.ToLowerInvariant();
        text = text.Replace(";", ",");
        text = text.Replace(":", ",");
        text = text.Replace("-", " ");
        text = StripBrackets().Replace(text, "");
        text = Regex.Replace(text, @"[\*\#\_\~\`\[\]\(\)\>\<]", "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private void PlayPcm(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);

        var format = new WaveFormat(_sampleRate, 16, 1);
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
        _phonemizer?.Dispose();
    }
}
