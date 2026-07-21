namespace Monolith.Voice.Models;

public record VadConfig
{
    public int FrameMs { get; init; } = 20;
    public int PreRollMs { get; init; } = 500;
    public int PostRollMs { get; init; } = 400;
    public int VADMode { get; init; } = 0;
    public double RmsFallbackThreshold { get; init; } = 0.0008;

    public void Validate()
    {
        if (FrameMs is not (10 or 20 or 30))
            throw new ArgumentException("FrameMs must be 10, 20, or 30");
        if (PreRollMs < 0)
            throw new ArgumentException("PreRollMs must be >= 0");
        if (PostRollMs < 100)
            throw new ArgumentException("PostRollMs must be >= 100");
        if (VADMode is < 0 or > 3)
            throw new ArgumentException("VADMode must be 0-3");
        if (RmsFallbackThreshold is < 0 or > 1)
            throw new ArgumentException("RmsFallbackThreshold must be 0.0-1.0");
    }
}
