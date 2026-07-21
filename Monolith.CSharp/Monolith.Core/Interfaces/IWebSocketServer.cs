namespace Monolith.Core.Interfaces;

public interface IWebSocketServer
{
    int ConnectedClients { get; }
    Task StartAsync();
    Task StopAsync();
    Task BroadcastAsync(object payload);
}
