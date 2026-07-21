using Xunit;
using Monolith.Voice.Models;
using Monolith.Voice.Services;

namespace Monolith.Core.Tests.Services;

public class VadDetectorTests
{
    [Fact]
    public void WebRtcVadDetector_SilenceFrame_ReturnsFalse()
    {
        using var vad = new WebRtcVadDetector(20, 3);
        var silence = new short[vad.FrameSizeSamples];
        var result = vad.IsSpeech(silence, 16000);
        Assert.False(result);
    }

    [Fact]
    public void WebRtcVadDetector_NoiseFrame_DetectsSpeech()
    {
        using var vad = new WebRtcVadDetector(20, 2);
        var rng = new Random(42);
        var noise = new short[vad.FrameSizeSamples];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = (short)(rng.NextSingle() * 32000 - 16000);
        var result = vad.IsSpeech(noise, 16000);
        Assert.True(result);
    }

    [Fact]
    public void WebRtcVadDetector_SpeechLikeSignal_DetectsSpeech()
    {
        using var vad = new WebRtcVadDetector(20, 1);
        var samples = new short[vad.FrameSizeSamples];
        var rng = new Random(99);
        for (int i = 0; i < samples.Length; i++)
        {
            var modFreq = 200 + rng.NextSingle() * 2000;
            samples[i] = (short)(Math.Sin(2 * Math.PI * modFreq * i / 16000) * 24000);
        }
        var result = vad.IsSpeech(samples, 16000);
        Assert.True(result);
    }

    [Fact]
    public void WebRtcVadDetector_InvalidFrameSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => new WebRtcVadDetector(15, 0));
        Assert.Throws<ArgumentException>(() => new WebRtcVadDetector(5, 0));
        Assert.Throws<ArgumentException>(() => new WebRtcVadDetector(40, 0));
    }

    [Fact]
    public void WebRtcVadDetector_FrameSizeSamples_CorrectFor20ms()
    {
        using var vad = new WebRtcVadDetector(20, 0);
        Assert.Equal(320, vad.FrameSizeSamples);
    }

    [Fact]
    public void WebRtcVadDetector_FrameSizeSamples_CorrectFor10ms()
    {
        using var vad = new WebRtcVadDetector(10, 0);
        Assert.Equal(160, vad.FrameSizeSamples);
    }

    [Fact]
    public void WebRtcVadDetector_VeryAggressiveMode_RejectsLowAmplitude()
    {
        using var vad = new WebRtcVadDetector(20, 3);
        var lowEnergy = new short[vad.FrameSizeSamples];
        var rng = new Random(123);
        for (int i = 0; i < lowEnergy.Length; i++)
            lowEnergy[i] = (short)(rng.NextSingle() * 200 - 100);
        var result = vad.IsSpeech(lowEnergy, 16000);
        Assert.False(result);
    }

    [Fact]
    public void WebRtcVadDetector_RmsFallback_WorksOnException()
    {
        using var vad = new WebRtcVadDetector(20, 0, rmsFallbackThreshold: 0.5);
        var highEnergy = new short[vad.FrameSizeSamples];
        Array.Fill(highEnergy, (short)30000);
        var result = vad.IsSpeech(highEnergy, 16000);
        Assert.True(result);
    }

    [Fact]
    public void VadConfigValidator_ValidConfig_DoesNotThrow()
    {
        var config = new VadConfig { FrameMs = 20, PreRollMs = 500, PostRollMs = 400, VADMode = 1 };
        var exception = Record.Exception(() => config.Validate());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(40)]
    [InlineData(50)]
    public void VadConfigValidator_InvalidFrameMs_Throws(int frameMs)
    {
        var config = new VadConfig { FrameMs = frameMs };
        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Fact]
    public void VadConfigValidator_InvalidPostRoll_Throws()
    {
        var config = new VadConfig { PostRollMs = 50 };
        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(5)]
    public void VadConfigValidator_InvalidVADMode_Throws(int mode)
    {
        var config = new VadConfig { VADMode = mode };
        Assert.Throws<ArgumentException>(() => config.Validate());
    }
}
