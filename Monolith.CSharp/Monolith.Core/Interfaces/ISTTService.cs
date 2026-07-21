namespace Monolith.Core.Interfaces;

public interface ISTTService
{
    Task<string?> ListenAsync(CancellationToken ct = default);
}
