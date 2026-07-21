namespace Monolith.Core.Models;

public record MonolithConfig
{
    public string GeminiApiKey { get; init; } = string.Empty;
    public string GeminiModelName { get; init; } = "gemini-2.5-flash";
    public string GoogleSpeechKey { get; init; } = "AIzaSyBOti4mM-6x9WDnZIjIeyEU21OpBXqWBgw";
    public bool UseVad { get; init; } = true;
    public int VadFrameMs { get; init; } = 20;
    public int VadPreRollMs { get; init; } = 500;
    public int VadPostRollMs { get; init; } = 400;
    public int VadMode { get; init; } = 0;
    public double VadRmsFallbackThreshold { get; init; } = 0.0008;
    public int WsPort { get; init; } = 8765;
    public string WsHost { get; init; } = "127.0.0.1";
    public int RecordingDurationSeconds { get; init; } = 5;
    public int SampleRate { get; init; } = 16000;

    public int LlmMaxConcurrentCalls { get; init; } = 1;
    public int LlmRetryCount { get; init; } = 5;
    public int LlmBaseDelayMs { get; init; } = 500;

    public double SttRmsFallbackThreshold { get; init; } = 0.01;
    public int SttMaxRetries { get; init; } = 2;
    public int SttRetryDelayMs { get; init; } = 200;

    public int LlmQueueWorkerCount { get; init; } = 1;

    public string TtsNativeModelPath { get; init; } = "";
    public int TtsNativeSampleRate { get; init; } = 48000;
    public int TtsNativeChannels { get; init; } = 1;
    public int TtsNativeOpusBitrate { get; init; } = 48000;
    public int TtsNativeMaxConcurrency { get; init; } = 1;

    public string TtsOnnxModelPath { get; init; } = "";
    public string TtsOnnxSelectedVoice { get; init; } = "";
    public int TtsOnnxSampleRate { get; init; } = 22050;
    public int TtsOnnxSpeakerId { get; init; } = 0;

    public bool HasGeminiKey => !string.IsNullOrWhiteSpace(GeminiApiKey);
}
