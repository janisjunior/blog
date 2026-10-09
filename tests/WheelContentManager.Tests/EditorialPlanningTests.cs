using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Core;
using WheelContentManager.GalleryProviders;
using WheelContentManager.Infrastructure;
using Xunit;

namespace WheelContentManager.Tests;
public class EditorialPlanningTests
{
    [Theory][InlineData("JR", "https://jr-wheels.com/blog/example", "SL04", 30, 7)][InlineData("Concaver", "https://concaverwheels.com/blog/example", "CVR1", 21, 9)][InlineData("Vesser", "https://vesserforged.com/blog/example", "VSR9", 21, 9)]
    public void ReadsActualRecentPostTitleDateAndModel(string brand, string url, string model, int day, int month)
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Live", brand + "-blog.html"));
        var post = BlogPublicationChecker.ParseRecent(html, new(url));
        Assert.Contains(model, post.WheelModels); Assert.Equal(day, post.Published!.Value.Day); Assert.Equal(month, post.Published.Value.Month);
    }
    [Fact] public void AlternativesOutrankNewestGalleryWithSameWheelAndLocalBatchChangesNextChoice()
    {
        var a = Fixtures.Gallery(); a.Id = 1; a.Specification.Model = "JR52"; a.FirstDetected = DateTimeOffset.UtcNow;
        var b = Fixtures.Gallery(); b.Id = 2; b.Specification.Model = "JR46"; b.FirstDetected = a.FirstDetected.AddDays(-3);
        var posts = new[] { new RecentBlogPost("https://jr-wheels.com/blog/last", "BMW JR52", DateTimeOffset.UtcNow.AddDays(-1), ["JR 52"]) };
        var ranked = TopicPlanner.Rank(WheelBrand.JR, [a, b], posts, []);
        Assert.Equal(2, ranked[0].Gallery.Id); Assert.Contains("urozmaica", ranked[0].Reason); Assert.Equal(posts[0].Url, ranked[0].LatestPostUrl);
        var ready = new Article { Gallery = b, Status = ArticleStatus.Ready, Created = DateTimeOffset.UtcNow }; b.Used = true;
        Assert.Equal(1, TopicPlanner.Rank(WheelBrand.JR, [a, b], posts, [ready])[0].Gallery.Id);
    }
    [Fact] public void UsedBlockedAndOtherBrandGalleriesAreExcludedButOnlyRepeatedModelRemainsAvailable()
    {
        var g = Fixtures.Gallery(); var used = Fixtures.Gallery(); used.Used = true;
        var blocked = Fixtures.Gallery(); blocked.BlogBlocked = true; blocked.BlogCheckedAt = DateTimeOffset.UtcNow;
        var posts = new[] { new RecentBlogPost("https://jr-wheels.com/blog/last", "JR52", DateTimeOffset.UtcNow, ["JR52"]) };
        var ranked = TopicPlanner.Rank(WheelBrand.JR, [g, used, blocked, Fixtures.Gallery(WheelBrand.Concaver)], posts, []);
        Assert.Single(ranked); Assert.Contains("lepiej wybrać inny model", ranked[0].Reason);
    }
    [Theory][InlineData(true, false)][InlineData(false, true)]
    public async Task MissingOrWrongFreshCardBlocksAiEvenWhenGalleryWasPreviouslyLoaded(bool missing, bool wrong)
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync();
        var provider = env.Providers.First(p => p.Brand == WheelBrand.JR); provider.MissingProduct = missing; provider.WrongProduct = wrong;
        var g = (await env.Content.GalleriesAsync()).First(g => g.Brand == WheelBrand.JR);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(g.Id, false, false, null, null, default));
        Assert.Empty(env.Ai.Calls); Assert.False((await env.Content.GalleriesAsync()).Single(x => x.Id == g.Id).Used);
    }
    [Fact] public async Task EveryGenerationRefreshesCardAndTextExportKeepsPhotosOnlyForAi()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync();
        var g = (await env.Content.GalleriesAsync()).First(g => g.Brand == WheelBrand.JR); g.Images[0].UsageAllowed = true;
        await env.Content.SaveGalleryAsync(g, false); var provider = env.Providers.First(p => p.Brand == WheelBrand.JR);
        var a = await env.Content.GenerateAsync(g.Id, false, false, null, null, default);
        await env.Content.GenerateAsync(g.Id, true, false, null, null, default); Assert.Equal(2, provider.Loads);
        Assert.Contains(env.Ai.Calls, c => c.Images == 1); Assert.False(Directory.Exists(Path.Combine(a.ExportFolder!, "images")));
        using (var doc = WordprocessingDocument.Open(Path.Combine(a.ExportFolder!, "article_PL.docx"), false)) Assert.Empty(doc.MainDocumentPart!.ImageParts);
        var zip = ExportService.CreateZip([a.ExportFolder!], Path.Combine(env.Root, "texts.zip"));
        using var archive = ZipFile.OpenRead(zip); Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("/images/"));
        var saved = (await env.Content.GalleriesAsync()).Single(x => x.Id == g.Id);
        Assert.Contains(saved.Sources, s => s.Field == "ProductDetails" && s.Confirmed && s.Url == saved.Specification.ProductUrl);
    }
    [Fact] public async Task UpgradeAppliesReferenceLengthWithoutLosingMailFolderOrBudgetAndIsIdempotent()
    {
        await using var env = await TestEnvironment.CreateAsync(); var service = env.Services.GetRequiredService<SettingsService>();
        var old = await service.LoadAsync(); old.MinWords = 1200; old.MaxWords = 1800; old.MaxOutputTokens = 6500;
        old.SmtpHost = "smtp.example.test"; old.MailTo = "owner@example.test"; old.ExportImages = true; old.MaxCycleCost = 7m;
        var json = JsonSerializer.SerializeToNode(old)!.AsObject(); json.Remove("EditorialPolicyVersion");
        await using (var db = await env.Factory.CreateDbContextAsync()) { var row = await db.ApplicationSettings.FindAsync("settings"); row!.Json = json.ToJsonString(); await db.SaveChangesAsync(); }
        var s = await service.LoadAsync(); Assert.Equal(2200, s.MinWords); Assert.Equal(2600, s.MaxWords); Assert.False(s.ExportImages);
        Assert.Equal(12000, s.MaxOutputTokens); Assert.Equal(old.ExportFolder, s.ExportFolder); Assert.Equal(old.SmtpHost, s.SmtpHost); Assert.Equal(old.MailTo, s.MailTo); Assert.Equal(7m, s.MaxCycleCost);
        Assert.Equal(JsonSerializer.Serialize(s), JsonSerializer.Serialize(await service.LoadAsync()));
    }
    [Fact] public async Task DashboardSuggestionsShowDifferentWheelModels()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync();
        await using (var db = await env.Factory.CreateDbContextAsync())
        {
            foreach (var (model, id) in new[] { ("JR46", "a"), ("JR46", "b"), ("JR49", "c") })
            {
                var g = Fixtures.Gallery(); g.Url += id; g.ExternalId += id; g.Specification.Model = model; db.Galleries.Add(g);
            }
            await db.SaveChangesAsync();
        }
        var suggestions = await env.Content.SuggestTopicsAsync(WheelBrand.JR);
        Assert.Equal(3, suggestions.Count); Assert.Equal(3, suggestions.Select(s => s.Gallery.Specification.Model).Distinct().Count());
    }
    [Fact] public void SourceProfilesUpgradeOnlyUntouchedCopiesAndReadAdditionalProductInformation()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "Profiles"); var target = Path.Combine(Path.GetTempPath(), "wcm-profile-" + Guid.NewGuid()); Directory.CreateDirectory(target);
        try
        {
            var current = File.ReadAllText(Path.Combine(shipped, "JR.json"));
            var old = current.Replace("//meta[@name='description']/@content | //div[@class='product_variant_li_max_method']", "//meta[@name='description']/@content");
            File.WriteAllText(Path.Combine(target, "JR.json"), old); File.WriteAllText(Path.Combine(target, "Concaver.json"), "custom profile");
            SourceProfileInstaller.Install(shipped, target);
            Assert.Equal(current, File.ReadAllText(Path.Combine(target, "JR.json"))); Assert.Equal("custom profile", File.ReadAllText(Path.Combine(target, "Concaver.json")));
            var profile = JsonSerializer.Deserialize<SourceProfile>(current)!; var doc = new HtmlAgilityPack.HtmlDocument(); doc.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Live", "JR-product.html"));
            var nodes = doc.DocumentNode.CreateNavigator().Select(profile.ProductFieldsXPath["ProductDetails"]); var values = new List<string>(); while (nodes.MoveNext()) values.Add(nodes.Current!.Value.Trim());
            Assert.Contains("FlowForm", values);
        }
        finally { Directory.Delete(target, true); }
    }
    [Fact] public async Task FailedRecentFeedStillFinishesPartialCycleAndQueuesNotification()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); env.Blog.RecentFailure = WheelBrand.Concaver;
        var report = await env.Content.RunCycleAsync(false, null, default);
        Assert.Contains("nie udało się dobrać tematu", report); Assert.Contains("częściowo", report);
        Assert.Equal(2, (await env.Content.ArticlesAsync()).Count);
        var notification = Assert.Single(await env.Services.GetRequiredService<NotificationService>().HistoryAsync());
        Assert.EndsWith(":partial", notification.RunId); Assert.Equal("Pending", notification.State); // no real SMTP configured
    }
    [Fact] public async Task OnlyExactOldBuiltInPromptsUpgradePreservingVersionHistory()
    {
        await using var env = await TestEnvironment.CreateAsync(); var prompts = env.Services.GetRequiredService<PromptService>();
        var old = PromptService.Default(WheelBrand.JR).Replace("Stały zakres w aplikacji to 2200–2600 słów na język, według obszernego wpisu Nissan Z / SL03. Rozwijaj różne aspekty konfiguracji bez powtórzeń i bez dopisywania niepotwierdzonych faktów.", "Domyślne 1200–1800 słów nie skraca wymagań stylistycznych dokumentu.");
        Assert.True(PromptService.IsPreviousBuiltIn(WheelBrand.JR, old));
        await prompts.SaveAsync(WheelBrand.JR, old, "previous built-in", true);
        await prompts.SaveAsync(WheelBrand.Concaver, "Moja edycja {CAR_MODEL}", "user", true);
        await Bootstrap.InitializeAsync(env.Services);
        Assert.Equal(3, (await prompts.CurrentAsync(WheelBrand.JR)).Revision); Assert.Equal(PromptService.Default(WheelBrand.JR), (await prompts.CurrentAsync(WheelBrand.JR)).Content);
        Assert.Equal("Moja edycja {CAR_MODEL}", (await prompts.CurrentAsync(WheelBrand.Concaver)).Content);
    }
}
