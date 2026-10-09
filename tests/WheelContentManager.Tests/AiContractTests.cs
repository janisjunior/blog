using System.Net;
using System.Text;
using System.Text.Json;
using WheelContentManager.AI;
using WheelContentManager.Core;
using Xunit;
namespace WheelContentManager.Tests;
public class AiContractTests
{
    [Theory][InlineData("{}")][InlineData("{\"title\":null}")][InlineData("{\"title\":\"t\",\"intro\":\"i\",\"body\":\"b\",\"language\":\"PL\",\"sources\":null,\"warnings\":[]}")]
    public void JsonEnvelopeRejectsMissingFieldsAndWrongTypes(string json) => Assert.Throws<JsonException>(() => AiResponseParser.Parse(json));
    [Fact] public async Task OpenAiUsesJsonOutputAndReportsTokenUsage()
    {
        var handler = new FakeHandler("""{"choices":[{"finish_reason":"stop","message":{"content":"{}"}}],"usage":{"prompt_tokens":123,"completion_tokens":45}}""");
        var provider = new OpenAiProvider(new HttpClient(handler)); var result = await provider.CompleteAsync("test-key", "vision-model", "instruction", "data", [], 1000, default);
        Assert.Equal(123, result.InputTokens); Assert.Equal(45, result.OutputTokens); using var body = JsonDocument.Parse(handler.Body!); Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString()); Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString()); Assert.Equal("api.openai.com", handler.Host);
    }
    [Fact] public async Task TruncatedOpenAiResponseIsNotAccepted()
    {
        var provider = new OpenAiProvider(new HttpClient(new FakeHandler("""{"choices":[{"finish_reason":"length","message":{"content":"{}"}}]}""")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync("test-key", "vision-model", "i", "d", [], 1000, default));
    }
    [Fact] public async Task AnthropicSendsSeparateSystemAndUserContent()
    {
        var handler = new FakeHandler("""{"stop_reason":"end_turn","content":[{"type":"text","text":"{}"}],"usage":{"input_tokens":20,"output_tokens":30}}""");
        var provider = new AnthropicProvider(new HttpClient(handler)); var result = await provider.CompleteAsync("test-key", "vision-model", "trusted instruction", "untrusted data", [], 1000, default);
        Assert.Equal(20, result.InputTokens); using var body = JsonDocument.Parse(handler.Body!); Assert.Contains("trusted instruction", body.RootElement.GetProperty("system").GetString()); Assert.Equal("untrusted data", body.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString()); Assert.Equal("2023-06-01", handler.AnthropicVersion);
    }
    [Fact] public async Task AuthenticationDenialDoesNotExposeKeyOrResponseBody()
    {
        var provider = new OpenAiProvider(new HttpClient(new FakeHandler("sensitive server response", HttpStatusCode.Unauthorized)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync("secret-test-value", "model", "i", "d", [], 1000, default)); Assert.DoesNotContain("secret-test-value", error.Message); Assert.DoesNotContain("sensitive server response", error.Message); Assert.Contains("401", error.Message);
    }
    private sealed class FakeHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body; public string? Host; public string? AnthropicVersion;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct); Host = request.RequestUri!.Host; AnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var v) ? v.Single() : null; return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") }; }
    }
}
