using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WheelContentManager.Core;

namespace WheelContentManager.AI;

public abstract class AiProvider(HttpClient http) : IAiProvider
{
    protected HttpClient Http { get; } = http;
    public abstract string Name { get; }
    public abstract Task<IReadOnlyList<string>> ModelsAsync(string key, CancellationToken ct);
    public abstract Task<AiResult> CompleteAsync(string key, string model, string instruction, string data, IReadOnlyList<string> imagePaths, int maxTokens, CancellationToken ct);
    protected async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = request(); using var response = await Http.SendAsync(req, ct);
            if (response.IsSuccessStatusCode) return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (attempt < 2 && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
            {
                var seconds = Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? Math.Pow(2, attempt + 1), 1, 30);
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct); continue;
            }
            throw new InvalidOperationException($"{Name}: HTTP {(int)response.StatusCode}. Sprawdź klucz API, dostęp do modelu, limit i rozliczenia API.");
        }
    }
    protected static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
    protected static HttpRequestMessage Request(HttpMethod method, string url, string key, object? body = null)
    {
        var req = new HttpRequestMessage(method, url); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (body != null) req.Content = JsonContent.Create(body); return req;
    }
}
public sealed class OpenAiProvider(HttpClient http) : AiProvider(http)
{
    public override string Name => "OpenAI";
    public override async Task<IReadOnlyList<string>> ModelsAsync(string key, CancellationToken ct)
    {
        using var json = await SendAsync(() => Request(HttpMethod.Get, "https://api.openai.com/v1/models", key), ct);
        return json.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).Order().ToArray();
    }
    public override async Task<AiResult> CompleteAsync(string key, string model, string instruction, string data, IReadOnlyList<string> imagePaths, int maxTokens, CancellationToken ct)
    {
        var content = new List<object> { new { type = "text", text = data } };
        foreach (var path in imagePaths) content.Add(new { type = "image_url", image_url = new { url = $"data:{MediaType(path)};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct))}" } });
        using var json = await SendAsync(() => Request(HttpMethod.Post, "https://api.openai.com/v1/chat/completions", key, new { model, max_completion_tokens = maxTokens, response_format = new { type = "json_object" }, messages = new object[] { new { role = "system", content = instruction }, new { role = "user", content } } }), ct);
        var root = json.RootElement; var choice = root.GetProperty("choices")[0];
        if (choice.GetProperty("finish_reason").GetString() != "stop") throw new InvalidOperationException("OpenAI: odpowiedź nie została ukończona. Zwiększ limit tokenów lub wybierz model obsługujący obrazy i JSON.");
        var usage = root.GetProperty("usage");
        return new(choice.GetProperty("message").GetProperty("content").GetString()!, usage.GetProperty("prompt_tokens").GetInt32(), usage.GetProperty("completion_tokens").GetInt32());
    }
}
public sealed class AnthropicProvider(HttpClient http) : AiProvider(http)
{
    public override string Name => "Anthropic";
    private static HttpRequestMessage ClaudeRequest(HttpMethod method, string url, string key, object? body = null)
    {
        var req = new HttpRequestMessage(method, url); req.Headers.Add("x-api-key", key); req.Headers.Add("anthropic-version", "2023-06-01"); if (body != null) req.Content = JsonContent.Create(body); return req;
    }
    public override async Task<IReadOnlyList<string>> ModelsAsync(string key, CancellationToken ct)
    {
        using var json = await SendAsync(() => ClaudeRequest(HttpMethod.Get, "https://api.anthropic.com/v1/models", key), ct);
        return json.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).Order().ToArray();
    }
    public override async Task<AiResult> CompleteAsync(string key, string model, string instruction, string data, IReadOnlyList<string> imagePaths, int maxTokens, CancellationToken ct)
    {
        var content = new List<object> { new { type = "text", text = data } };
        foreach (var path in imagePaths) content.Add(new { type = "image", source = new { type = "base64", media_type = MediaType(path), data = Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)) } });
        using var json = await SendAsync(() => ClaudeRequest(HttpMethod.Post, "https://api.anthropic.com/v1/messages", key, new { model, max_tokens = maxTokens, system = instruction + " Zwróć wyłącznie JSON, bez bloków Markdown.", messages = new[] { new { role = "user", content } } }), ct);
        var root = json.RootElement;
        if (root.GetProperty("stop_reason").GetString() != "end_turn") throw new InvalidOperationException("Anthropic: nieukończona odpowiedź. Sprawdź limit tokenów.");
        var text = string.Concat(root.GetProperty("content").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "text").Select(x => x.GetProperty("text").GetString()));
        var u = root.GetProperty("usage"); return new(text, u.GetProperty("input_tokens").GetInt32(), u.GetProperty("output_tokens").GetInt32());
    }
}
