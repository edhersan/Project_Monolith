using System.Net.Http.Json;
using System.Text.Json;
using Concentus;
using Concentus.Structs;
using NAudio.Wave;

namespace Monolith.Voice.Services;

public class OpusStreamPlayer : IDisposable
{
    private readonly HttpClient _http;
    private readonly BufferedWaveProvider _buffer;
    private readonly WaveOutEvent _output;
    private readonly IOpusDecoder _decoder;
    private bool _disposed;

    public OpusStreamPlayer(int sampleRate = 48000, int channels = 1, int bufferMs = 300)
    {
        SampleRate = sampleRate;
        Channels = channels;

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);
        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels))
        {
            BufferDuration = TimeSpan.FromMilliseconds(bufferMs * 3),
            DiscardOnBufferOverflow = true
        };
        _output = new WaveOutEvent();
        _output.Init(_buffer);
        _output.Play();
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int BufferedMilliseconds => (int)(_buffer.BufferedDuration.TotalMilliseconds);
    public PlaybackState PlaybackState => _output.PlaybackState;

    public async Task PlayFromDaemonAsync(
        string daemonUrl,
        string text,
        string voice = "es-CO-GonzaloNeural",
        string? apiKey = null,
        CancellationToken ct = default)
    {
        var payload = new { text, voice };
        var content = JsonContent.Create(payload, options: new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{daemonUrl}/stream")
        {
            Content = content
        };

        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);

        using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await ReadAndDecodeStreamAsync(stream, ct);
    }

    public async Task ReadAndDecodeStreamAsync(Stream stream, CancellationToken ct = default)
    {
        var lenBuf = new byte[4];
        const int frameSamples = 48000 * 20 / 1000; // 20ms at 48kHz

        while (!ct.IsCancellationRequested)
        {
            var r = await ReadExactAsync(stream, lenBuf, 0, 4, ct);
            if (r == 0) break;

            var packetLen = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (packetLen <= 0 || packetLen > 4096 * 100)
                break;

            var packet = new byte[packetLen];
            await ReadExactAsync(stream, packet, 0, packetLen, ct);

            var pcm = new short[frameSamples];
            var decoded = _decoder.Decode(packet.AsSpan(0, packetLen), pcm.AsSpan(0, frameSamples), frameSamples);

            if (decoded > 0)
            {
                var pcmBytes = new byte[decoded * 2];
                Buffer.BlockCopy(pcm, 0, pcmBytes, 0, pcmBytes.Length);
                _buffer.AddSamples(pcmBytes, 0, pcmBytes.Length);
            }
        }
    }

    private static async Task<int> ReadExactAsync(Stream s, byte[] buf, int off, int cnt, CancellationToken ct)
    {
        var total = 0;
        while (total < cnt)
        {
            var n = await s.ReadAsync(buf, off + total, cnt - total, ct);
            if (n == 0) return total;
            total += n;
        }
        return total;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _output?.Stop();
        _output?.Dispose();
        _decoder?.Dispose();
        _http?.Dispose();
    }
}
