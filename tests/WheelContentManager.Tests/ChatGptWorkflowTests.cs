using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
using Xunit;
namespace WheelContentManager.Tests;
public class ChatGptWorkflowTests
{
    private static async Task ManualAsync(TestEnvironment env)
    {
        var service = env.Services.GetRequiredService<SettingsService>(); var s = await service.LoadAsync();
        s.UseChatGpt = true; s.AiModel = ""; s.InputPricePerMillion = s.OutputPricePerMillion = 0;
        await service.SaveAsync(s); env.Services.GetRequiredService<ISecretStore>().AsMemory().Missing = true;
    }
    [Fact] public async Task StockCreatesThreeVerifiedPromptsWithNoApiCredentialsOrAi()
    {
        await using var env = await TestEnvironment.CreateAsync(); await ManualAsync(env);
        await env.Content.PrepareReadySetAsync(null, default);
        var topics = await env.Content.ChatGptTopicsAsync(); Assert.Equal(3, topics.Count);
        Assert.Equal(3, topics.Select(t => t.Brand).Distinct().Count()); Assert.Empty(env.Ai.Calls);
        Assert.All(topics, t => { Assert.Contains("2200–2600", t.Prompt); Assert.Contains("POTWIERDZONE", t.Prompt); Assert.Contains("\"articles\"", t.Prompt); Assert.Contains("fixture.example/product", t.Prompt); });
        Assert.True(await env.Content.PreparationCompleteAsync()); Assert.Empty(await env.Content.ArticlesAsync());
        var counts = env.Providers.Select(p => p.Discoveries).ToArray(); await env.Content.PrepareReadySetAsync(null, default);
        Assert.Equal(counts, env.Providers.Select(p => p.Discoveries).ToArray()); Assert.Empty(env.Ai.Calls);
    }
    [Theory][InlineData(true, false)][InlineData(false, true)] public async Task PublishedOrPossibleDuplicatesAreNeverOffered(bool published, bool possible)
    {
        await using var env = await TestEnvironment.CreateAsync(); await ManualAsync(env);
        env.Blog.Result = new(published, possible, "https://fixture.example/blog/existing", "Duplikat");
        await env.Content.PrepareReadySetAsync(null, default); Assert.Empty(await env.Content.ChatGptTopicsAsync()); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task FailedArchiveCheckAndMissingProductBlockPrompt()
    {
        await using var env = await TestEnvironment.CreateAsync(); await ManualAsync(env); await env.Content.SyncAsync(null, default);
        var gallery = (await env.Content.GalleriesAsync())[0]; env.Blog.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.PrepareChatGptTopicAsync(gallery.Id, default));
        env.Blog.Fail = false; env.Providers.Single(p => p.Brand == gallery.Brand).MissingProduct = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.PrepareChatGptTopicAsync(gallery.Id, default)); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task RecheckRemovesTopicWhenItWasPublishedAfterPreparation()
    {
        await using var env = await TestEnvironment.CreateAsync(); await ManualAsync(env); await env.Content.PrepareReadySetAsync(null, default);
        var topic = (await env.Content.ChatGptTopicsAsync())[0]; env.Blog.Result = new(true, false, "https://fixture.example/blog/new", "Nowy wpis");
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.PrepareChatGptTopicAsync(topic.GalleryId, default));
        Assert.DoesNotContain(await env.Content.ChatGptTopicsAsync(), t => t.GalleryId == topic.GalleryId); Assert.Empty(env.Ai.Calls);
    }
    private static string Response(Gallery gallery) => JsonSerializer.Serialize(new { articles = new[] { Fixtures.ArticleData(gallery), Fixtures.ArticleData(gallery, "EN") } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    [Fact] public async Task ImportSavesBothLanguagesExportsAndKeepsBrandReservedForReviewWithoutAi()
    {
        await using var env = await TestEnvironment.CreateAsync(); await ManualAsync(env); await env.Content.PrepareReadySetAsync(null, default);
        var topic = (await env.Content.ChatGptTopicsAsync())[0]; var gallery = (await env.Content.GalleriesAsync()).Single(g => g.Id == topic.GalleryId);
        var article = await env.Content.ImportChatGptResponseAsync(gallery.Id, "```json\n" + Response(gallery) + "\n```", default);
        Assert.Equal(ArticleStatus.NeedsCorrection, article.Status); Assert.Equal(2, article.Versions.Count);
        Assert.All(article.Versions, v => { Assert.Equal(0, v.InputTokens); Assert.Equal(0, v.OutputTokens); });
        Assert.NotEmpty(Directory.GetFiles(article.ExportFolder!)); Assert.Empty(env.Ai.Calls);
        await env.Content.PrepareReadySetAsync(null, default); Assert.Equal(2, (await env.Content.ChatGptTopicsAsync()).Count);
        Assert.True(await env.Content.PreparationCompleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.ImportChatGptResponseAsync(gallery.Id, Response(gallery), default));
        Assert.Single(await env.Content.ArticlesAsync());
    }
    [Fact] public async Task ImportBlocksNewPublicationAndApiEntryPointIsDisabled()
    {
        await using var env = await TestEnvironment.CreateAsync(); await ManualAsync(env); await env.Content.PrepareReadySetAsync(null, default);
        var topic = (await env.Content.ChatGptTopicsAsync())[0]; var gallery = (await env.Content.GalleriesAsync()).Single(g => g.Id == topic.GalleryId);
        env.Blog.Result = new(false, true, "https://fixture.example/blog/new", "Duplikat");
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.ImportChatGptResponseAsync(gallery.Id, Response(gallery), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(gallery.Id, false, false, null, null, default));
        Assert.Empty(await env.Content.ArticlesAsync()); Assert.Empty(env.Ai.Calls);
    }
    [Theory][InlineData("{}")] [InlineData("[]")] [InlineData("{\"articles\":[]}")] [InlineData("not json")]
    public void InvalidOrIncompleteResponseIsRejected(string response) => Assert.ThrowsAny<Exception>(() => ContentService.ParseChatGptResponse(response));
    [Fact] public void ExistingSettingsWithoutModeDefaultToChatGpt() => Assert.True(JsonSerializer.Deserialize<AppSettings>("{\"AiModel\":\"old-model\"}")!.UseChatGpt);
}
internal static class SecretTestExtensions { public static MemorySecrets AsMemory(this ISecretStore store) => (MemorySecrets)store; }
