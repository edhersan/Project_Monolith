using System.Diagnostics;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;

namespace Monolith.Voice.Services;

[SupportedOSPlatform("windows")]
public class TtsSystemSpeechService : ITTSService, IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private readonly PipelineMetrics? _metrics;
    private bool _disposed;

    public TtsSystemSpeechService(PipelineMetrics? metrics = null)
    {
        _metrics = metrics;
        _synth = new SpeechSynthesizer();

        try
        {
            // Try to select Spanish voice
            var spanishVoice = _synth.GetInstalledVoices()
                .FirstOrDefault(v => v.VoiceInfo?.Culture?.Name?.StartsWith("es") == true);

            if (spanishVoice != null)
            {
                _synth.SelectVoice(spanishVoice.VoiceInfo.Name);
                Console.WriteLine($"[TTS] Voz seleccionada: {spanishVoice.VoiceInfo.Name}");
            }
            else
            {
                Console.WriteLine("[TTS] No se encontró voz en español, usando voz por defecto");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Error seleccionando voz: {ex.Message}");
        }
    }

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            _synth.Speak(text);
            sw.Stop();

            Console.WriteLine($"[TTS] OK ({sw.ElapsedMilliseconds}ms)");
            _metrics?.RecordTtsLatency(sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _synth.SpeakAsyncCancelAll();
            Console.WriteLine("[TTS] Cancelado");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS ERROR] {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synth?.Dispose();
    }
}
