using System.Text.Json;
using Fleck;
using Monolith.Core.Interfaces;

namespace Monolith.WebSocket.Services;

public class FooterWSServer : Monolith.Core.Interfaces.IWebSocketServer
{
    private readonly string _host;
    private readonly int _port;
    private WebSocketServer? _server;
    private readonly List<IWebSocketConnection> _clients = new();
    private readonly object _lock = new();

    public FooterWSServer(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public int ConnectedClients
    {
        get { lock (_lock) { return _clients.Count; } }
    }

    public Task StartAsync()
    {
        if (_server != null)
            return Task.CompletedTask;

        _server = new WebSocketServer($"ws://{_host}:{_port}");
        _server.RestartAfterListenError = true;

        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                lock (_lock) _clients.Add(socket);
                Console.WriteLine($"[WS] Cliente conectado. Total: {ConnectedClients}");
            };

            socket.OnClose = () =>
            {
                lock (_lock) _clients.Remove(socket);
                Console.WriteLine($"[WS] Cliente desconectado. Total: {ConnectedClients}");
            };

            socket.OnError = ex =>
            {
                Console.WriteLine($"[WS] Error: {ex.Message}");
            };
        });

        Console.WriteLine($"[WS] Servidor WebSocket activo en ws://{_host}:{_port}");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        foreach (var client in _clients.ToList())
        {
            client.Close();
        }

        _server?.Dispose();
        _server = null;
        return Task.CompletedTask;
    }

    public async Task BroadcastAsync(object payload)
    {
        List<IWebSocketConnection> clients;
        lock (_lock) clients = _clients.ToList();

        if (clients.Count == 0)
        {
            Console.WriteLine("[WS] No hay clientes conectados para enviar mensaje");
            return;
        }

        var message = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        Console.WriteLine($"[WS] Enviando mensaje a {clients.Count} clientes: {message[..Math.Min(message.Length, 100)]}...");

        var tasks = clients.Select(c =>
        {
            try { return c.Send(message); }
            catch { return Task.CompletedTask; }
        });

        await Task.WhenAll(tasks);
    }
}
