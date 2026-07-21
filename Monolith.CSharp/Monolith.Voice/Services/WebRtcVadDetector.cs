using WebRtcVadSharp;

namespace Monolith.Voice.Services;

public sealed class WebRtcVadDetector : IVadDetector
{
    private WebRtcVad? _native;
    private readonly int _frameSizeSamples;
    private readonly int _frameMs;
    private readonly int _aggressiveness;

    public WebRtcVadDetector(int frameMs = 20, int aggressiveness = 0, double rmsFallbackThreshold = 0.0008)
    {
        if (frameMs != 10 && frameMs != 20 && frameMs != 30)
            throw new ArgumentException("Frame size must be 10, 20, or 30 ms");

        _frameMs = frameMs;
        _frameSizeSamples = 16 * frameMs;
        _aggressiveness = aggressiveness;
        RmsFallbackThreshold = rmsFallbackThreshold;

        try
        {
            _native = CreateNative(frameMs, aggressiveness);
        }
        catch
        {
            _native = null;
        }
    }

    private static WebRtcVad CreateNative(int frameMs, int aggressiveness)
    {
        var vad = new WebRtcVad();
        vad.FrameLength = frameMs switch
        {
            10 => FrameLength.Is10ms,
            20 => FrameLength.Is20ms,
            30 => FrameLength.Is30ms,
            _ => FrameLength.Is20ms
        };
        vad.SampleRate = SampleRate.Is16kHz;
        vad.OperatingMode = aggressiveness switch
        {
            0 => OperatingMode.HighQuality,
            1 => OperatingMode.LowBitrate,
            2 => OperatingMode.Aggressive,
            3 => OperatingMode.VeryAggressive,
            _ => OperatingMode.HighQuality
        };
        return vad;
    }

    public int FrameSizeSamples => _frameSizeSamples;
    public double RmsFallbackThreshold { get; set; }

    public bool IsSpeech(ReadOnlySpan<short> pcmSamples, int sampleRate)
    {
        if (_native != null)
        {
            try
            {
                var frame = pcmSamples.ToArray();
                return _native.HasSpeech(frame);
            }
            catch
            {
                _native.Dispose();
                _native = null;
            }
        }

        return RmsFallback(pcmSamples);
    }

    private bool RmsFallback(ReadOnlySpan<short> samples)
    {
        double sumSq = 0;
        foreach (var s in samples)
            sumSq += (double)s * s;

        var normalized = sumSq / (samples.Length * 32768.0 * 32768.0);
        return normalized > RmsFallbackThreshold;
    }

    public void Dispose()
    {
        _native?.Dispose();
    }
}
