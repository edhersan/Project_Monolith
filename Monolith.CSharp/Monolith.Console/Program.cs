using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;
using Monolith.Core.Services;
using Monolith.Voice.Models;
using Monolith.Voice.Services;
using Monolith.WebSocket.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

LoadEnvFile();

if (args.Any(arg => arg.Equals("--demo", StringComparison.OrdinalIgnoreCase) || arg.Equals("demo", StringComparison.OrdinalIgnoreCase)))
{
    await RunDemoAsync();
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<MonolithConfig>(builder.Configuration.GetSection("Monolith"));

var metrics = new PipelineMetrics();
builder.Services.AddSingleton(metrics);

builder.Services.AddSingleton<ILLMProvider>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MonolithConfig>>().Value;

    if (!config.HasGeminiKey)
        throw new InvalidOperationException("GEMINI_API_KEY no esta configurada en appsettings.json o en el entorno.");

    return new GeminiLLMProvider(
        config.GeminiApiKey,
        config.GeminiModelName,
        Prompts.SystemPrompt,
        metrics,
        config.LlmMaxConcurrentCalls,
        config.LlmRetryCount,
        config.LlmBaseDelayMs);
});

builder.Services.AddSingleton<GoogleSpeechSTT>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MonolithConfig>>().Value;
    return new GoogleSpeechSTT(
        config.GoogleSpeechKey,
        config.RecordingDurationSeconds,
        config.SampleRate,
        config.SttRmsFallbackThreshold,
        config.SttMaxRetries,
        config.SttRetryDelayMs);
});

builder.Services.AddSingleton<ISTTService>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MonolithConfig>>().Value;
    var stt = sp.GetRequiredService<GoogleSpeechSTT>();

    if (config.UseVad)
    {
        var vadConfig = new VadConfig
        {
            FrameMs = config.VadFrameMs,
            PreRollMs = config.VadPreRollMs,
            PostRollMs = config.VadPostRollMs,
            VADMode = config.VadMode,
            RmsFallbackThreshold = config.VadRmsFallbackThreshold
        };
        var vad = new WebRtcVadDetector(vadConfig.FrameMs, vadConfig.VADMode, vadConfig.RmsFallbackThreshold);
        Console.WriteLine($"[VAD] Activo (frame={vadConfig.FrameMs}ms, pre={vadConfig.PreRollMs}ms, post={vadConfig.PostRollMs}ms, mode={vadConfig.VADMode})");
        return new VadCaptureService(stt, vad, vadConfig);
    }

    return stt;
});

builder.Services.AddSingleton<ITTSService>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MonolithConfig>>().Value;

    if (!string.IsNullOrEmpty(config.TtsOnnxModelPath) && Directory.Exists(config.TtsOnnxModelPath))
    {
        var voiceName = !string.IsNullOrEmpty(config.TtsOnnxSelectedVoice)
            ? config.TtsOnnxSelectedVoice
            : null;
        Console.WriteLine($"[TTS] Usando ONNX: {config.TtsOnnxModelPath}" +
            (voiceName != null ? $" [{voiceName}]" : ""));
        return new TtsOnnxService(
            config.TtsOnnxModelPath,
            voiceName,
            config.TtsOnnxSpeakerId,
            metrics: sp.GetRequiredService<PipelineMetrics>());
    }

    if (!string.IsNullOrEmpty(config.TtsNativeModelPath))
    {
        Console.WriteLine("[TTS] Usando modulo nativo P/Invoke");
        return new Monolith.Voice.Native.TtsNativeService(
            config.TtsNativeModelPath,
            config.TtsNativeSampleRate,
            config.TtsNativeChannels,
            config.TtsNativeOpusBitrate,
            config.TtsNativeMaxConcurrency,
            sp.GetRequiredService<PipelineMetrics>());
    }

    Console.WriteLine("[TTS] Usando System.Speech (Windows)");
    return new TtsSystemSpeechService(sp.GetRequiredService<PipelineMetrics>());
});

builder.Services.AddSingleton<IWebSocketServer>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MonolithConfig>>().Value;
    return new FooterWSServer(config.WsHost, config.WsPort);
});

builder.Services.AddSingleton<MonolithApp>();

var host = builder.Build();

var app = host.Services.GetRequiredService<MonolithApp>();
var wsServer = host.Services.GetRequiredService<IWebSocketServer>();
var stt = host.Services.GetRequiredService<ISTTService>();
var tts = host.Services.GetRequiredService<ITTSService>();
var llm = host.Services.GetRequiredService<ILLMProvider>();

var llmQueue = new LLMQueueService(llm, wsServer, tts, metrics, 1);

Console.WriteLine("Iniciando Monolith con voz (Google STT + TTS nativo)...");

try
{
    await wsServer.StartAsync();
    Console.WriteLine($"[WS] Clientes conectados: {wsServer.ConnectedClients}");
}
catch (Exception ex)
{
    Console.WriteLine($"[WS] No se pudo iniciar el servidor WebSocket: {ex.Message}");
}

try
{
    while (true)
    {
        try
        {
            var userInput = await stt.ListenAsync();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            await wsServer.BroadcastAsync(new { speaker = "edhersan", text = userInput });

            if (userInput.Contains("salir", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Monolith fuera.");
                break;
            }

            await llmQueue.EnqueueAsync(userInput);

            if (metrics.TotalUtterances > 0 && metrics.TotalUtterances % 5 == 0)
                Console.WriteLine(metrics);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR INESPERADO]: {ex.Message}");
        }
    }
}
finally
{
    llmQueue.Dispose();
    await wsServer.StopAsync();
}

static void LoadEnvFile()
{
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
    {
        var envFile = Path.Combine(d.FullName, ".env");
        if (!File.Exists(envFile)) continue;

        foreach (var line in File.ReadAllLines(envFile))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;

            var key = trimmed[..eq].Trim();
            var val = trimmed[(eq + 1)..].Trim().Trim('"', '\'');
            if (!string.IsNullOrEmpty(key) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, val);
            }

            if (key == "GEMINI_API_KEY")
                Environment.SetEnvironmentVariable("Monolith__GeminiApiKey", val);
        }
        break;
    }
}

static async Task RunDemoAsync()
{
    var app = new MonolithApp(new DemoLLMProvider());
    app.SetResponseCallback(response => Console.WriteLine($"RESPUESTA: {response}"));

    Console.WriteLine("Iniciando demo local de Monolith...");
    await app.HandleTranscriptAsync(new TranscriptEvent("hola monolith presentate", IsFinal: true));
}

sealed class DemoLLMProvider : ILLMProvider
{
    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var lastLine = prompt.Split('\n').LastOrDefault() ?? prompt;
        var preview = lastLine.Length > 120 ? lastLine[..120] : lastLine;
        return Task.FromResult("Respuesta de prueba: " + preview);
    }
}
