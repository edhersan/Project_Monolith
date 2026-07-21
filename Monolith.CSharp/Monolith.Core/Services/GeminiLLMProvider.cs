using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monolith.Core.Interfaces;
using Monolith.Core.Models;
using Polly;
using Polly.Retry;

namespace Monolith.Core.Services;

public class GeminiLLMProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly string _systemPrompt;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _semaphore;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly PipelineMetrics _metrics;

    public GeminiLLMProvider(
        string apiKey,
        string modelName,
        string systemPrompt,
        PipelineMetrics metrics,
        int maxConcurrentCalls = 1,
        int retryCount = 5,
        int baseDelayMs = 500)
    {
        _apiKey = apiKey;
        _modelName = modelName;
        _systemPrompt = systemPrompt;
        _metrics = metrics;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _semaphore = new SemaphoreSlim(maxConcurrentCalls);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r =>
                (int)r.StatusCode == 429 || (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(retryCount,
                attempt => TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt))
                    + TimeSpan.FromMilliseconds(new Random().Next(0, 300)));
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var request = new GeminiRequest
        {
            SystemInstruction = new Content { Parts = [new Part { Text = _systemPrompt }] },
            Contents = [new Content { Role = "user", Parts = [new Part { Text = prompt }] }],
            GenerationConfig = new GenerationConfig { Temperature = 0.7f }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";

        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            await _semaphore.WaitAsync(ct);
            try
            {
                var sw = Stopwatch.StartNew();

                var response = await _retryPolicy.ExecuteAsync(async () =>
                {
                    var resp = await _http.PostAsync(url, httpContent, ct).ConfigureAwait(false);
                    if ((int)resp.StatusCode == 429)
                        _metrics.Llm429Count++;
                    return resp;
                });

                response.EnsureSuccessStatusCode();
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                sw.Stop();

                _metrics.RecordLlmLatency(sw.Elapsed.TotalMilliseconds);

                var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, _jsonOptions);
                return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM ERROR] {ex.Message}");
            return string.Empty;
        }
    }

    public void Dispose() => _http.Dispose();
}

internal class GeminiRequest
{
    [JsonPropertyName("system_instruction")]
    public Content? SystemInstruction { get; set; }
    public List<Content>? Contents { get; set; }
    public GenerationConfig? GenerationConfig { get; set; }
}

internal class Content
{
    public string? Role { get; set; }
    public List<Part>? Parts { get; set; }
}

internal class Part
{
    public string? Text { get; set; }
}

internal class GenerationConfig
{
    public float Temperature { get; set; }
}

internal class GeminiResponse
{
    public List<Candidate>? Candidates { get; set; }
}

internal class Candidate
{
    public Content? Content { get; set; }
}
