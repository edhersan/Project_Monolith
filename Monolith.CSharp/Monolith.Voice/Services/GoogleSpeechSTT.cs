using System.Diagnostics;
using System.Text.Json;
using Monolith.Core.Interfaces;

namespace Monolith.Voice.Services;

public class GoogleSpeechSTT : ISTTService, IDisposable
{
    private readonly string _apiKey;
    private readonly int _durationSeconds;
    private readonly int _sampleRate;
    private readonly HttpClient _http;
    private readonly double _rmsThreshold;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;

    public GoogleSpeechSTT(
        string apiKey,
        int durationSeconds = 5,
        int sampleRate = 16000,
        double rmsThreshold = 0.01,
        int maxRetries = 2,
        int retryDelayMs = 200)
    {
        _apiKey = apiKey;
        _durationSeconds = durationSeconds;
        _sampleRate = sampleRate;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _rmsThreshold = rmsThreshold;
        _maxRetries = maxRetries;
        _retryDelayMs = retryDelayMs;
    }

    public async Task<string?> ListenAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"\n[Escuchando] Habla ahora (Grabando {_durationSeconds} segundos)...");

        try
        {
            var audioData = RecordAudio();
            if (audioData.Length == 0)
            {
                Console.WriteLine("[?] No se capturo audio.");
                return null;
            }

            Console.WriteLine("[Procesando voz...]");
            return await RecognizeAsync(audioData, ct);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Error STT]: No se pudo conectar con Google Speech API: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("[Error STT]: Tiempo de espera agotado.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error STT]: {ex.Message}");
            return null;
        }
    }

    public virtual async Task<string?> RecognizeAsync(byte[] audioData, CancellationToken ct = default)
    {
        var rms = ComputeRms(audioData);
        var durationMs = audioData.Length * 1000.0 / (_sampleRate * 2);

        Console.WriteLine($"[STT] audioDurationMs={durationMs:F0} payloadSize={audioData.Length} rms={rms:F6}");

        if (rms <= _rmsThreshold)
        {
            Console.WriteLine($"[STT] RMS bajo ({rms:F6} <= {_rmsThreshold}), probable silencio.");
            return null;
        }

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                Console.WriteLine($"[STT] Reintento {attempt}/{_maxRetries}...");
                await Task.Delay(_retryDelayMs, ct);
            }

            var text = await SendToGoogleSpeechAsync(audioData, ct);

            if (!string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine($"Tu dijiste: {text}");
                return text;
            }

            Console.WriteLine($"[STT] Resultado vacio pero RMS={rms:F6} > umbral, reintentando...");
        }

        Console.WriteLine("[?] No entendi bien lo que dijiste, intenta de nuevo.");
        return null;
    }

    public static double ComputeRms(byte[] pcm16)
    {
        if (pcm16.Length < 2) return 0;
        var sampleCount = pcm16.Length / 2;
        double sumSq = 0;
        for (int i = 0; i < pcm16.Length - 1; i += 2)
        {
            var sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            sumSq += (double)sample * sample;
        }
        return Math.Sqrt(sumSq / sampleCount) / 32768.0;
    }

    private byte[] RecordAudio()
    {
        using var waveIn = new NAudio.Wave.WaveInEvent
        {
            WaveFormat = new NAudio.Wave.WaveFormat(_sampleRate, 16, 1),
            BufferMilliseconds = 100
        };

        var audioBuffer = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        var recordingDone = new ManualResetEvent(false);

        waveIn.DataAvailable += (s, e) =>
        {
            var chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            audioBuffer.Enqueue(chunk);
        };

        waveIn.RecordingStopped += (s, e) =>
        {
            recordingDone.Set();
        };

        waveIn.StartRecording();
        Thread.Sleep(_durationSeconds * 1000);
        waveIn.StopRecording();
        recordingDone.WaitOne(2000);

        var allChunks = audioBuffer.ToArray();
        var totalLength = allChunks.Sum(c => c.Length);
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in allChunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    protected virtual async Task<string?> SendToGoogleSpeechAsync(byte[] audioData, CancellationToken ct)
    {
        var url = $"https://www.google.com/speech-api/v2/recognize?output=json&lang=es-CO&key={_apiKey}";

        var content = new ByteArrayContent(audioData);
        content.Headers.TryAddWithoutValidation("Content-Type", $"audio/l16; rate={_sampleRate}; channels=1");

        var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        Console.WriteLine($"[STT DEBUG] Respuesta Google API: {json.Truncate(200)}");

        foreach (var line in json.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) continue;
            if (!root.TryGetProperty("result", out var resultArray) || resultArray.ValueKind != JsonValueKind.Array) continue;

            foreach (var result in resultArray.EnumerateArray())
            {
                if (!result.TryGetProperty("alternative", out var altArray) || altArray.ValueKind != JsonValueKind.Array) continue;

                foreach (var alt in altArray.EnumerateArray())
                {
                    if (alt.TryGetProperty("transcript", out var transcript))
                    {
                        return transcript.GetString();
                    }
                }
            }
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
