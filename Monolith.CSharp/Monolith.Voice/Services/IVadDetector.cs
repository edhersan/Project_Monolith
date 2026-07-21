namespace Monolith.Voice.Services;

public interface IVadDetector : IDisposable
{
    bool IsSpeech(ReadOnlySpan<short> pcmSamples, int sampleRate);
    int FrameSizeSamples { get; }
    double RmsFallbackThreshold { get; set; }
}
