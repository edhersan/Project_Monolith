using System.Threading.Channels;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;

namespace Monolith.Core.Services;

public class LLMQueueService : IDisposable
{
    private readonly Channel<string> _channel;
    private readonly ILLMProvider _llm;
    private readonly IWebSocketServer _ws;
    private readonly ITTSService _tts;
    private readonly PipelineMetrics _metrics;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _workers;

    public LLMQueueService(
        ILLMProvider llm,
        IWebSocketServer ws,
        ITTSService tts,
        PipelineMetrics metrics,
        int workerCount = 1)
    {
        _llm = llm;
        _ws = ws;
        _tts = tts;
        _metrics = metrics;
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _cts = new CancellationTokenSource();
        _workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() => WorkerLoop(_cts.Token)))
            .ToArray();
    }

    public async Task EnqueueAsync(string text, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(text, ct);
        _metrics.QueueDepth = _channel.Reader.Count;
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        await foreach (var prompt in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                _metrics.QueueDepth = _channel.Reader.Count;

                var response = await _llm.GenerateAsync(prompt, ct);
                if (string.IsNullOrWhiteSpace(response))
                    continue;

                Console.WriteLine($"Monolith (Texto): {response}");

                await _ws.BroadcastAsync(new { speaker = "zael", text = response });

                await _tts.SpeakAsync(response, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[LLM QUEUE ERROR] {ex.Message}");
            }
            finally
            {
                _metrics.QueueDepth = _channel.Reader.Count;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        Task.WaitAll(_workers, 5000);
        _cts.Dispose();
    }
}
