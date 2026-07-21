using Xunit;
using Monolith.Voice.Services;

namespace Monolith.Core.Tests.Services;

public class SttRetryTests
{
    [Fact]
    public async Task RecognizeAsync_ReturnsNull_WhenRmsBelowThreshold()
    {
        using var stt = new GoogleSpeechSTT("test-key", 5, 16000, rmsThreshold: 0.5);
        var silence = new byte[16000 * 2];
        var result = await stt.RecognizeAsync(silence);
        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_ReturnsText_OnFirstAttempt()
    {
        using var stt = new StubStt("hola mundo", 0.001, 0, 0);
        var audio = MakeNoise();
        var result = await stt.RecognizeAsync(audio);
        Assert.Equal("hola mundo", result);
    }

    [Fact]
    public async Task RecognizeAsync_Retries_OnEmptyResult_WithHighRms()
    {
        using var stt = new StubSttWithRetries(null, "texto en retry");
        var audio = MakeLoudAudio();
        var result = await stt.RecognizeAsync(audio);
        Assert.Equal("texto en retry", result);
        Assert.True(stt.AttemptCount >= 2);
    }

    [Fact]
    public async Task RecognizeAsync_NoRetry_WhenRmsBelowThreshold()
    {
        using var stt = new StubSttWithRetries(null, "fallback", rmsThreshold: 0.5);
        var audio = new byte[16000 * 2];
        var result = await stt.RecognizeAsync(audio);
        Assert.Null(result);
        Assert.Equal(0, stt.AttemptCount);
    }

    [Fact]
    public void ComputeRms_Zero_ReturnsZero()
    {
        var pcm = new byte[320];
        var rms = GoogleSpeechSTT.ComputeRms(pcm);
        Assert.Equal(0, rms, 4);
    }

    [Fact]
    public void ComputeRms_MaxAmplitude_ReturnsOne()
    {
        var pcm = new byte[320];
        for (int i = 0; i < pcm.Length; i += 2)
        {
            pcm[i] = 0xFF;
            pcm[i + 1] = 0x7F;
        }
        var rms = GoogleSpeechSTT.ComputeRms(pcm);
        Assert.Equal(1.0, rms, 1);
    }

    private static byte[] MakeNoise()
    {
        var buf = new byte[16000 * 2];
        new Random(42).NextBytes(buf);
        return buf;
    }

    private static byte[] MakeLoudAudio()
    {
        var buf = new byte[16000 * 2];
        for (int i = 0; i < buf.Length; i += 2)
        {
            var s = (short)20000;
            buf[i] = (byte)s;
            buf[i + 1] = (byte)(s >> 8);
        }
        return buf;
    }

    private sealed class StubStt : GoogleSpeechSTT
    {
        private readonly string _response;
        public StubStt(string response, double rmsThreshold, int maxRetries, int retryDelayMs)
            : base("test-key", 5, 16000, rmsThreshold, maxRetries, retryDelayMs)
        {
            _response = response;
        }

        protected override Task<string?> SendToGoogleSpeechAsync(byte[] audioData, CancellationToken ct)
            => Task.FromResult<string?>(audioData.Length > 0 ? _response : null);
    }

    private sealed class StubSttWithRetries : GoogleSpeechSTT
    {
        private readonly string? _firstResult;
        private readonly string? _secondResult;
        public int AttemptCount { get; private set; }

        public StubSttWithRetries(string? first, string? second, double rmsThreshold = 0.0001)
            : base("test-key", 5, 16000, rmsThreshold, 2, 1)
        {
            _firstResult = first;
            _secondResult = second;
        }

        protected override Task<string?> SendToGoogleSpeechAsync(byte[] audioData, CancellationToken ct)
        {
            AttemptCount++;
            return Task.FromResult(AttemptCount == 1 ? _firstResult : _secondResult);
        }
    }
}
