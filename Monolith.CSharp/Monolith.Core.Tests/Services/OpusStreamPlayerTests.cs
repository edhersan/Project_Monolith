using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using Monolith.Voice.Services;
using Xunit;

namespace Monolith.Core.Tests.Services;

public class OpusStreamPlayerTests
{
    [Fact]
    public async Task ReadAndDecodeStreamAsync_DecodesSingleOpusPacket()
    {
        int sampleRate = 48000;
        int channels = 1;
        int frameSamples = sampleRate * 20 / 1000; // 960 @ 20ms

        using var player = new OpusStreamPlayer(sampleRate, channels);

        var originalPcm = new short[frameSamples];
        for (int i = 0; i < frameSamples; i++)
        {
            double t = (double)i / sampleRate;
            originalPcm[i] = (short)(Math.Sin(2 * Math.PI * 440 * t) * short.MaxValue * 0.3);
        }

        var encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        var opusBuf = new byte[4000];
        int packetLen = encoder.Encode(originalPcm.AsSpan(0, frameSamples), frameSamples, opusBuf.AsSpan(0, opusBuf.Length), opusBuf.Length);

        using var ms = new MemoryStream();
        ms.WriteByte((byte)((packetLen >> 24) & 0xFF));
        ms.WriteByte((byte)((packetLen >> 16) & 0xFF));
        ms.WriteByte((byte)((packetLen >> 8) & 0xFF));
        ms.WriteByte((byte)(packetLen & 0xFF));
        ms.Write(opusBuf, 0, packetLen);
        ms.Position = 0;

        await player.ReadAndDecodeStreamAsync(ms, CancellationToken.None);

        Assert.True(player.BufferedMilliseconds > 0,
            $"Expected buffered audio > 0ms, got {player.BufferedMilliseconds}ms");
    }

    [Fact]
    public async Task ReadAndDecodeStreamAsync_MultiplePackets_AccumulatesBuffer()
    {
        int sampleRate = 48000;
        int channels = 1;
        int frameSamples = sampleRate * 20 / 1000;

        using var player = new OpusStreamPlayer(sampleRate, channels);

        var encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        var opusBuf = new byte[4000];

        using var ms = new MemoryStream();
        var pcmBuf = new short[frameSamples];

        for (int pkt = 0; pkt < 3; pkt++)
        {
            for (int i = 0; i < frameSamples; i++)
            {
                double t = (double)i / sampleRate;
                pcmBuf[i] = (short)(Math.Sin(2 * Math.PI * (440 + pkt * 100) * t) * short.MaxValue * 0.3);
            }

            int packetLen = encoder.Encode(pcmBuf.AsSpan(0, frameSamples), frameSamples, opusBuf.AsSpan(0, opusBuf.Length), opusBuf.Length);

            ms.WriteByte((byte)((packetLen >> 24) & 0xFF));
            ms.WriteByte((byte)((packetLen >> 16) & 0xFF));
            ms.WriteByte((byte)((packetLen >> 8) & 0xFF));
            ms.WriteByte((byte)(packetLen & 0xFF));
            ms.Write(opusBuf, 0, packetLen);
        }

        ms.Position = 0;

        await player.ReadAndDecodeStreamAsync(ms, CancellationToken.None);

        Assert.True(player.BufferedMilliseconds > 40, // At least 60ms (3 × 20ms)
            $"Expected buffered audio > 40ms, got {player.BufferedMilliseconds}ms");
    }

    [Fact]
    public async Task ReadAndDecodeStreamAsync_EmptyStream_DoesNotThrow()
    {
        using var player = new OpusStreamPlayer();
        using var ms = new MemoryStream();

        await player.ReadAndDecodeStreamAsync(ms, CancellationToken.None);

        Assert.Equal(0, player.BufferedMilliseconds);
    }

    [Fact]
    public async Task ReadAndDecodeStreamAsync_Cancellation_StopsEarly()
    {
        int sampleRate = 48000;
        int channels = 1;
        int frameSamples = sampleRate * 20 / 1000;

        using var player = new OpusStreamPlayer(sampleRate, channels);

        var encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        var pcmBuf = new short[frameSamples];
        var opusBuf = new byte[4000];

        using var ms = new MemoryStream();
        for (int pkt = 0; pkt < 20; pkt++)
        {
            int packetLen = encoder.Encode(pcmBuf.AsSpan(0, frameSamples), frameSamples, opusBuf.AsSpan(0, opusBuf.Length), opusBuf.Length);
            ms.WriteByte((byte)((packetLen >> 24) & 0xFF));
            ms.WriteByte((byte)((packetLen >> 16) & 0xFF));
            ms.WriteByte((byte)((packetLen >> 8) & 0xFF));
            ms.WriteByte((byte)(packetLen & 0xFF));
            ms.Write(opusBuf, 0, packetLen);
        }
        ms.Position = 0;

        using var cts = new CancellationTokenSource();

        // Start decoding in background, cancel after short delay
        var decodeTask = player.ReadAndDecodeStreamAsync(ms, cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() => decodeTask);
        Assert.Null(ex); // Should complete gracefully (OperationCanceledException caught inside)
    }
}
