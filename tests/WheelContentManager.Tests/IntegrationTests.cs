using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
using Xunit;

namespace WheelContentManager.Tests;
public sealed class IntegrationTests
{
    [Fact] public async Task MigrationsCreateAllTablesAndPersistSettingsAndPromptHistory()
    {
        await using var fixture = await TestEnvironment.CreateAsync(); await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.Equal(3, await db.Brands.CountAsync()); Assert.Equal(3, await db.PromptTemplates.CountAsync());
        var settings = fixture.Services.GetRequiredService<SettingsService>(); var value = await settings.LoadAsync(); value.MinWords = 150; await settings.SaveAsync(value); Assert.Equal(150, (await settings.LoadAsync()).MinWords);
        var prompts = fixture.Services.GetRequiredService<PromptService>(); await prompts.SaveAsync(WheelBrand.JR, "Nowy {CAR_MODEL}", "test", true); var history = await prompts.HistoryAsync(WheelBrand.JR); Assert.Equal(2, history.Count); Assert.Equal(2, (await prompts.CurrentAsync(WheelBrand.JR)).Revision);
    }
    [Fact] public async Task RepeatedSynchronizationDoesNotDuplicateGalleriesAndKeepsCorrections()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.Content.SyncAsync(null, default); await env.Content.SyncAsync(null, default); Assert.Equal(3, (await env.Content.GalleriesAsync()).Count);
        var gallery = (await env.Content.GalleriesAsync())[0]; gallery.Specification.Finish = "Ręczna korekta"; await env.Content.SaveGalleryAsync(gallery, true); await env.Content.SyncAsync(null, default);
        Assert.Equal("Ręczna korekta", (await env.Content.GalleriesAsync()).Single(x => x.Id == gallery.Id).Specification.Finish);
    }
    [Fact] public async Task PhotoPermissionSaveKeepsUnchangedSourceFactsConfirmed()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); var gallery = (await env.Content.GalleriesAsync())[0]; gallery.Images[0].UsageAllowed = true;
        await env.Content.SaveGalleryAsync(gallery, false); var saved = (await env.Content.GalleriesAsync()).Single(x => x.Id == gallery.Id);
        Assert.True(saved.Images[0].UsageAllowed); Assert.True(saved.Sources.Single(x => x.Field == "WheelModel").Confirmed); Assert.True(saved.Sources.Single(x => x.Field == "CarModel").Confirmed);
    }
    [Fact] public async Task FullGenerationUsesImagesIndependentLanguageCallsAndSavesBothVersions()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); var g = (await env.Content.GalleriesAsync()).First(x => x.Brand == WheelBrand.JR);
        var result = await env.Content.GenerateAsync(g.Id, false, false, null, null, default);
        Assert.Equal(ArticleStatus.Ready, result.Status); Assert.Equal(2, result.Versions.Count); Assert.Contains(env.Ai.Calls, x => x.Images == 1); Assert.Contains(env.Ai.Calls, x => x.Language == "PL"); Assert.Contains(env.Ai.Calls, x => x.Language == "EN");
        Assert.True((await env.Content.GalleriesAsync()).Single(x => x.Id == g.Id).Used); Assert.Single(await env.Content.ArticlesAsync()); Assert.True(File.Exists(Path.Combine(result.ExportFolder!, "article_EN.docx")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(g.Id, false, false, null, null, default));
    }
    [Fact] public async Task ExplicitRegenerationKeepsOneArticleAndHistory()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); var g = (await env.Content.GalleriesAsync())[0];
        await env.Content.GenerateAsync(g.Id, false, false, null, null, default); var changed = await env.Content.GenerateAsync(g.Id, true, false, null, null, default);
        Assert.Single(await env.Content.ArticlesAsync()); Assert.Equal(4, changed.Versions.Count); Assert.Equal(2, changed.Versions.Max(x => x.Revision));
    }
    [Fact] public async Task DryRunDoesNotUseGallerySaveArticleOrSendNotification()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); var g = (await env.Content.GalleriesAsync())[0];
        var a = await env.Content.GenerateAsync(g.Id, false, true, null, null, default); Assert.Equal(2, a.Versions.Count); Assert.False((await env.Content.GalleriesAsync()).Single(x => x.Id == g.Id).Used); Assert.Empty(await env.Content.ArticlesAsync());
        await using var db = await env.Factory.CreateDbContextAsync(); Assert.Empty(await db.NotificationHistory.ToListAsync());
    }
    [Fact] public async Task UnverifiedPromptBlocksAiAndDoesNotMarkGalleryUsed()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.Content.SyncAsync(null, default); var g = (await env.Content.GalleriesAsync())[0];
        await env.Services.GetRequiredService<PromptService>().SaveAsync(g.Brand, "Nowy dokument wymagający sprawdzenia {CAR_MODEL}", "niezweryfikowany import", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(g.Id, false, false, null, null, default)); Assert.Empty(env.Ai.Calls); Assert.False((await env.Content.GalleriesAsync()).Single(x => x.Id == g.Id).Used);
    }
    [Fact] public async Task InvalidAiResponseKeepsFailureInsteadOfReadyAndRetriesAreBounded()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); env.Ai.BrokenJson = true; var g = (await env.Content.GalleriesAsync())[0];
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(g.Id, false, false, null, null, default)); Assert.Equal(ArticleStatus.NeedsCorrection, (await env.Content.ArticlesAsync()).Single().Status); Assert.Equal(4, env.Ai.Calls.Count); // 1 analiza + 3 próby artykułu
    }
    [Fact] public async Task TokenAndCostLimitPreventsApiCall()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); var s = await env.Services.GetRequiredService<SettingsService>().LoadAsync(); s.MaxCycleCost = .000001m; await env.Services.GetRequiredService<SettingsService>().SaveAsync(s);
        var gallery = (await env.Content.GalleriesAsync())[0]; await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(gallery.Id, false, true, null, null, default)); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task IncompleteArticleCannotBeApproved()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); await using var db = await env.Factory.CreateDbContextAsync(); var g = await db.Galleries.FirstAsync();
        var a = new Article { GalleryId = g.Id, Versions = [new() { Language = "PL", Body = "krótki" }] }; db.Articles.Add(a); await db.SaveChangesAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.SetStatusAsync(a.Id, false));
    }
    [Fact] public async Task UnknownNotificationIsNotResent()
    {
        await using var env = await TestEnvironment.CreateAsync(); await using var db = await env.Factory.CreateDbContextAsync(); var run = new AutomationRun(); db.NotificationHistory.Add(new() { RunId = run.Id + ":partial", State = "Unknown", MessageId = "test@example.invalid" }); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Services.GetRequiredService<NotificationService>().NotifyAsync(run, [], "test", default)); Assert.Contains("niepewny wynik", error.Message);
    }
    [Fact] public async Task ExplicitReceiptConfirmationClosesUnknownDelivery()
    {
        await using var env = await TestEnvironment.CreateAsync(); await using var db = await env.Factory.CreateDbContextAsync(); var n = new NotificationHistory { RunId = "test:partial", State = "Unknown", MessageId = "test@example.invalid" }; db.NotificationHistory.Add(n); await db.SaveChangesAsync();
        await env.Services.GetRequiredService<NotificationService>().MarkReceivedAsync(n.Id, default); await db.Entry(n).ReloadAsync(); Assert.Equal("Sent", n.State); Assert.NotNull(n.SentAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Services.GetRequiredService<NotificationService>().RetryAsync(n.Id, default));
    }
    [Fact] public async Task WeeklyCycleUsesOneGalleryPerBrandAndDoesNotRegenerateCompletedWeek()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); var s = await env.Services.GetRequiredService<SettingsService>().LoadAsync(); s.ScheduleEnabled = true; await env.Services.GetRequiredService<SettingsService>().SaveAsync(s);
        await env.Content.RunCycleAsync(true, null, default); var articles = await env.Content.ArticlesAsync(); Assert.Equal(3, articles.Count); Assert.All(articles, x => Assert.Equal(ArticleStatus.Ready, x.Status)); var calls = env.Ai.Calls.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.RunCycleAsync(true, null, default)); Assert.Equal(calls, env.Ai.Calls.Count); Assert.Equal(3, (await env.Content.ArticlesAsync()).Count);
    }
}
internal sealed class MemorySecrets : ISecretStore { public bool Missing { get; set; } public string? Read(string name) => Missing ? null : "test-only-key"; public void Save(string name, string value) { } }
internal sealed class MockAi : IAiProvider
{
    public string Name => "OpenAI"; public string NarrativeVariant { get; set; } = ""; public bool BrokenJson { get; set; } public List<(int Images, string Language)> Calls { get; } = [];
    public Task<IReadOnlyList<string>> ModelsAsync(string key, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["mock-vision"]);
    public Task<AiResult> CompleteAsync(string key, string model, string instruction, string data, IReadOnlyList<string> imagePaths, int maxTokens, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var language = instruction.Contains("Pole language = EN") ? "EN" : "PL"; Calls.Add((imagePaths.Count, language));
        if (instruction.StartsWith("Analizuj")) return Task.FromResult(new AiResult("{\"observations\":[\"syntetyczne zdjęcie do testu\"],\"warnings\":[]}", 50, 50));
        if (instruction.StartsWith("Sprawdź")) return Task.FromResult(new AiResult("{\"errors\":[]}", 50, 50));
        if (BrokenJson) return Task.FromResult(new AiResult("not JSON", 50, 50));
        using var json = JsonDocument.Parse(data.Split("\nPoprzednia próba")[0]); var brand = json.RootElement.GetProperty("wheelBrand").GetString();
        var g = Fixtures.Gallery(brand == "JR Wheels" ? WheelBrand.JR : brand == "Concaver Wheels" ? WheelBrand.Concaver : WheelBrand.Vesser);
        g.Url = json.RootElement.GetProperty("galleryUrl").GetString()!;
        g.Vehicle.Model = json.RootElement.GetProperty("bindings").GetProperty("CAR_MODEL").GetString();
        var article = Fixtures.ArticleData(g, language);
        if (NarrativeVariant.Length > 0) article = article with { Body = article.Body.Replace("jasny", "jasny" + NarrativeVariant).Replace("ciemny", "ciemny" + NarrativeVariant).Replace("srebrny", "srebrny" + NarrativeVariant) };
        return Task.FromResult(new AiResult(JsonSerializer.Serialize(article, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), 50, 50));
    }
}
internal sealed class FixtureProvider(WheelBrand brand, string imagePath) : IGalleryProvider
{
    public int Discoveries { get; private set; }
    public bool MissingProduct { get; set; } public bool WrongProduct { get; set; } public int Loads { get; private set; }
    public WheelBrand Brand => brand; public Uri IndexUrl => new("https://fixture.example/");
    public Task<Gallery> LoadAsync(string url, CancellationToken ct)
    {
        Loads++; var g = Fixtures.Gallery(brand); g.Images[0].LocalPath = imagePath;
        if (MissingProduct) return Task.FromResult(g);
        g.Specification.ProductUrl = "https://fixture.example/product/" + brand;
        g.Specification.ProductDetails = "Potwierdzona karta modelu do testu.";
        g.Sources.AddRange([new() { Field = "ProductModel", Value = WrongProduct ? "JR99" : g.Specification.Model, Url = g.Specification.ProductUrl, Confirmed = true }, new() { Field = "ProductDetails", Value = g.Specification.ProductDetails, Url = g.Specification.ProductUrl, Confirmed = true }]);
        return Task.FromResult(g);
    }
    public Task<IReadOnlyList<Gallery>> DiscoverAsync(CancellationToken ct) { Discoveries++; var g = Fixtures.Gallery(brand); g.Images[0].LocalPath = imagePath; return Task.FromResult<IReadOnlyList<Gallery>>([g]); }
}
internal sealed class TestEnvironment : IAsyncDisposable
{
    public List<FixtureProvider> Providers { get; } = [];
    public ServiceProvider Services { get; private set; } = null!; public string Root { get; private set; } = ""; public MockAi Ai { get; } = new(); public MockBlog Blog { get; } = new();
    public IDbContextFactory<ContentDb> Factory => Services.GetRequiredService<IDbContextFactory<ContentDb>>(); public ContentService Content => Services.GetRequiredService<ContentService>();
    public static async Task<TestEnvironment> CreateAsync()
    {
        var env = new TestEnvironment { Root = Path.Combine(Path.GetTempPath(), "wcm-tests-" + Guid.NewGuid()) }; var paths = new AppPaths(env.Root); var image = Path.Combine(env.Root, "fixture.png");
        File.WriteAllBytes(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/u94AAAAASUVORK5CYII="));
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(paths); services.AddDbContextFactory<ContentDb>(o => o.UseSqlite("Data Source=" + paths.Database)); services.AddSingleton<ISecretStore, MemorySecrets>(); services.AddSingleton<IAiProvider>(env.Ai);
        foreach (var b in Enum.GetValues<WheelBrand>()) { var provider = new FixtureProvider(b, image); env.Providers.Add(provider); services.AddSingleton<IGalleryProvider>(provider); }
        services.AddSingleton<SettingsService>(); services.AddSingleton<PromptService>(); services.AddSingleton<ExportService>(); services.AddSingleton<NotificationService>(); services.AddSingleton<ContentService>(); services.AddSingleton<IBlogPublicationChecker>(env.Blog); services.AddHttpClient();
        env.Services = services.BuildServiceProvider(); await Bootstrap.InitializeAsync(env.Services); var settings = Fixtures.Settings(); settings.ExportFolder = paths.Exports; await env.Services.GetRequiredService<SettingsService>().SaveAsync(settings); return env;
    }
    public async Task PrepareAsync() { await Content.SyncAsync(null, default); foreach (var b in Enum.GetValues<WheelBrand>()) await Services.GetRequiredService<PromptService>().SaveAsync(b, PromptService.Baseline, "test fixture — nie dokument produkcyjny", true); }
    public async ValueTask DisposeAsync() { await Services.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(Root, true); }
}

internal sealed class MockBlog : IBlogPublicationChecker { public IReadOnlyList<RecentBlogPost> Posts { get; set; } = []; public WheelBrand? RecentFailure { get; set; } public Task<IReadOnlyList<RecentBlogPost>> RecentAsync(WheelBrand brand, CancellationToken ct) => brand == RecentFailure ? throw new HttpRequestException("recent unavailable") : Task.FromResult(Posts); public BlogCheck Result { get; set; } = new(false, false, null, "test"); public bool Timeout { get; set; } public bool Fail { get; set; } public Task<BlogCheck> CheckAsync(Gallery g, CancellationToken ct) => Timeout ? throw new TaskCanceledException("timeout") : Fail ? throw new HttpRequestException("test") : Task.FromResult(Result); }
