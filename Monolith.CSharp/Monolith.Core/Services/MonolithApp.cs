using Monolith.Core.Interfaces;
using Monolith.Core.Models;

namespace Monolith.Core.Services;

public class MonolithApp
{
    private readonly ILLMProvider _llmProvider;
    private Action<string>? _responseCallback;
    private IWebSocketServer? _wsServer;

    public MonolithApp(ILLMProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public void SetResponseCallback(Action<string> callback)
    {
        _responseCallback = callback;
    }

    public void SetWsServer(IWebSocketServer wsServer)
    {
        _wsServer = wsServer;
    }

    public async Task<string?> HandleTranscriptAsync(TranscriptEvent evt)
    {
        if (!evt.IsFinal)
            return null;

        var prompt = (evt.Text ?? "").Trim();
        if (string.IsNullOrEmpty(prompt))
            return null;

        Console.WriteLine($"[APP] Procesando input: {prompt}");

        var response = await _llmProvider.GenerateAsync(prompt);

        _responseCallback?.Invoke(response);

        if (_wsServer != null && !string.IsNullOrEmpty(response))
        {
            Console.WriteLine($"[APP] Enviando WebSocket: {response}");
            await _wsServer.BroadcastAsync(new { speaker = "zael", text = response });
        }

        return response;
    }
}
