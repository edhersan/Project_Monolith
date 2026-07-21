namespace Monolith.Core.Interfaces;

public interface ILLMProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
}
