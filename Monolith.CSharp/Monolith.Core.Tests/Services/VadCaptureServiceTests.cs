using Xunit;
using Monolith.Core.Interfaces;
using Monolith.Voice.Models;
using Monolith.Voice.Services;

namespace Monolith.Core.Tests.Services;

public class VadCaptureServiceTests
{
    [Fact]
    public void VadMetrics_InitialState()
    {
        var m = new VadMetrics();
        Assert.Equal(0, m.TotalUtterances);
        Assert.Equal(0, m.FalseTriggerCount);
        Assert.Equal(0, m.AvgUtteranceLengthMs);
        Assert.Equal(0, m.AvgSttLatencyMs);
    }

    [Fact]
    public void VadMetrics_RecordUtterance_UpdatesStats()
    {
        var m = new VadMetrics();
        m.RecordUtterance(1500, 200);
        m.RecordUtterance(2500, 300);
        Assert.Equal(2, m.TotalUtterances);
        Assert.Equal(2000, m.AvgUtteranceLengthMs, 0.01);
        Assert.Equal(250, m.AvgSttLatencyMs, 0.01);
    }

    [Fact]
    public void VadMetrics_FalseTrigger_Increments()
    {
        var m = new VadMetrics();
        m.RecordFalseTrigger();
        m.RecordFalseTrigger();
        Assert.Equal(2, m.FalseTriggerCount);
    }

    [Fact]
    public void VadMetrics_SpeechRatio_Calculated()
    {
        var m = new VadMetrics();
        m.FramesProcessed = 100;
        m.SpeechFrames = 40;
        m.SilenceFrames = 60;
        Assert.Equal(0.4, m.SpeechRatio, 0.01);
    }

    [Fact]
    public async Task VadCaptureService_WithNullAudio_ReturnsNull()
    {
        var stt = new StubGoogleSpeech("test response");
        var vad = new WebRtcVadDetector(20, 3);
        var config = new VadConfig { FrameMs = 20, PreRollMs = 100, PostRollMs = 200, VADMode = 3 };

        using var service = new VadCaptureService(stt, vad, config);

        Assert.NotNull(service);
        Assert.Equal(0, service.Metrics.TotalUtterances);
    }

    private sealed class StubGoogleSpeech : GoogleSpeechSTT
    {
        private readonly string _response;
        public StubGoogleSpeech(string response) : base("test-key", 5, 16000)
        {
            _response = response;
        }

        public override Task<string?> RecognizeAsync(byte[] audioData, CancellationToken ct)
            => Task.FromResult<string?>(audioData.Length > 0 ? _response : null);
    }
}
