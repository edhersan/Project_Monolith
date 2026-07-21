using Xunit;
using Monolith.Core.Models;

namespace Monolith.Core.Tests.Services;

public class PipelineMetricsTests
{
    [Fact]
    public void InitialState_AllZero()
    {
        var m = new PipelineMetrics();
        Assert.Equal(0, m.TotalUtterances);
        Assert.Equal(0, m.AvgSttLatencyMs);
        Assert.Equal(0, m.AvgLlmLatencyMs);
        Assert.Equal(0, m.AvgTtsLatencyMs);
        Assert.Equal(0, m.Llm429Count);
    }

    [Fact]
    public void RecordSttLatency_UpdatesAverage()
    {
        var m = new PipelineMetrics();
        m.RecordSttLatency(100);
        m.RecordSttLatency(300);
        Assert.Equal(2, m.TotalUtterances);
        Assert.Equal(200, m.AvgSttLatencyMs, 1);
    }

    [Fact]
    public void RecordLlmLatency_UpdatesAverage()
    {
        var m = new PipelineMetrics();
        m.RecordLlmLatency(500);
        m.RecordLlmLatency(1500);
        Assert.Equal(1000, m.AvgLlmLatencyMs, 1);
    }

    [Fact]
    public void RecordTtsLatency_UpdatesAverage()
    {
        var m = new PipelineMetrics();
        m.RecordTtsLatency(2000);
        m.RecordTtsLatency(4000);
        Assert.Equal(3000, m.AvgTtsLatencyMs, 1);
    }

    [Fact]
    public void Llm429Count_Increments()
    {
        var m = new PipelineMetrics();
        m.Llm429Count++;
        m.Llm429Count++;
        Assert.Equal(2, m.Llm429Count);
    }

    [Fact]
    public void QueueDepth_TracksCorrectly()
    {
        var m = new PipelineMetrics();
        m.QueueDepth = 5;
        Assert.Equal(5, m.QueueDepth);
    }

    [Fact]
    public void ToString_ContainsExpectedLabels()
    {
        var m = new PipelineMetrics();
        var s = m.ToString();
        Assert.Contains("Pipeline Metrics", s);
        Assert.Contains("Utterances", s);
        Assert.Contains("Avg STT", s);
        Assert.Contains("Avg LLM", s);
        Assert.Contains("Avg TTS", s);
        Assert.Contains("LLM 429", s);
        Assert.Contains("Queue depth", s);
    }
}
