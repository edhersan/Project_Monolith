namespace Monolith.Core.Interfaces;

public interface ITTSService
{
    Task SpeakAsync(string text, CancellationToken ct = default);
}
