namespace Monolith.Voice.Models;

public class VadMetrics
{
    private readonly object _lock = new();
    private readonly List<double> _utteranceLengthsMs = new();
    private readonly List<double> _sttLatenciesMs = new();

    public int TotalUtterances { get; private set; }
    public int FramesProcessed { get; set; }
    public int SpeechFrames { get; set; }
    public int SilenceFrames { get; set; }
    public int FalseTriggers { get; set; }

    public void RecordUtterance(double lengthMs, double sttLatencyMs)
    {
        lock (_lock)
        {
            TotalUtterances++;
            _utteranceLengthsMs.Add(lengthMs);
            _sttLatenciesMs.Add(sttLatencyMs);
        }
    }

    public void RecordFalseTrigger()
    {
        Interlocked.Increment(ref _falseTriggerCount);
    }

    private int _falseTriggerCount;
    public int FalseTriggerCount => _falseTriggerCount;

    public double AvgUtteranceLengthMs
    {
        get
        {
            lock (_lock)
                return _utteranceLengthsMs.Count > 0 ? _utteranceLengthsMs.Average() : 0;
        }
    }

    public double AvgSttLatencyMs
    {
        get
        {
            lock (_lock)
                return _sttLatenciesMs.Count > 0 ? _sttLatenciesMs.Average() : 0;
        }
    }

    public double SpeechRatio =>
        FramesProcessed > 0 ? (double)SpeechFrames / FramesProcessed : 0;

    public override string ToString()
    {
        return
            $"VAD Metrics:\n" +
            $"  Utterances: {TotalUtterances} (false: {FalseTriggerCount})\n" +
            $"  Avg length: {AvgUtteranceLengthMs:F0}ms\n" +
            $"  Avg STT latency: {AvgSttLatencyMs:F0}ms\n" +
            $"  Speech ratio: {SpeechRatio:P1}\n" +
            $"  Frames: {FramesProcessed} (speech={SpeechFrames}, silence={SilenceFrames})";
    }
}
