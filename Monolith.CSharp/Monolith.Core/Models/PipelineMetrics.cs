namespace Monolith.Core.Models;

public class PipelineMetrics
{
    private readonly object _lock = new();
    private readonly List<double> _sttLatencies = new();
    private readonly List<double> _llmLatencies = new();
    private readonly List<double> _ttsLatencies = new();

    public int Llm429Count { get; set; }
    public int TotalUtterances { get; private set; }
    public int QueueDepth { get; set; }

    public void RecordSttLatency(double ms)
    {
        lock (_lock)
        {
            TotalUtterances++;
            _sttLatencies.Add(ms);
        }
    }

    public void RecordLlmLatency(double ms)
    {
        lock (_lock) _llmLatencies.Add(ms);
    }

    public void RecordTtsLatency(double ms)
    {
        lock (_lock) _ttsLatencies.Add(ms);
    }

    public double AvgSttLatencyMs
    {
        get { lock (_lock) return _sttLatencies.Count > 0 ? _sttLatencies.Average() : 0; }
    }

    public double AvgLlmLatencyMs
    {
        get { lock (_lock) return _llmLatencies.Count > 0 ? _llmLatencies.Average() : 0; }
    }

    public double AvgTtsLatencyMs
    {
        get { lock (_lock) return _ttsLatencies.Count > 0 ? _ttsLatencies.Average() : 0; }
    }

    public override string ToString()
    {
        return
            $"Pipeline Metrics:\n" +
            $"  Utterances: {TotalUtterances}\n" +
            $"  Avg STT: {AvgSttLatencyMs:F0}ms\n" +
            $"  Avg LLM: {AvgLlmLatencyMs:F0}ms\n" +
            $"  Avg TTS: {AvgTtsLatencyMs:F0}ms\n" +
            $"  LLM 429s: {Llm429Count}\n" +
            $"  Queue depth: {QueueDepth}";
    }
}
