using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
using Xunit;
namespace WheelContentManager.Tests;
public class BackgroundPreparationTests
{
    [Fact] public async Task StockCreatesThreeIndependentBrandArticlesAndSecondCheckDoesNoNetworkOrAi()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync();
        await env.Content.PrepareReadySetAsync(null, default); var ready = await env.Content.ReadySetAsync();
        Assert.Equal(3, ready.Count); Assert.Equal(3, ready.Select(a => a.Gallery.Brand).Distinct().Count());
        var calls = env.Ai.Calls.Count; var requests = env.Providers.Sum(p => p.Discoveries);
        await env.Content.PrepareReadySetAsync(null, default);
        Assert.Equal(calls, env.Ai.Calls.Count); Assert.Equal(requests, env.Providers.Sum(p => p.Discoveries)); Assert.Equal(3, (await env.Content.ArticlesAsync()).Count);
        var mail = Assert.Single(await env.Services.GetRequiredService<NotificationService>().HistoryAsync()); Assert.EndsWith(":success", mail.RunId); Assert.Equal("Pending", mail.State);
    }
    [Fact] public async Task ExistingTwoReadyArticlesOnlyRequireTheMissingBrand()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync();
        foreach (var g in (await env.Content.GalleriesAsync()).Where(g => g.Brand != WheelBrand.Vesser)) await env.Content.GenerateAsync(g.Id, false, false, null, null, default);
        var calls = env.Ai.Calls.Count; var before = env.Providers.ToDictionary(p => p.Brand, p => p.Discoveries);
        await env.Content.PrepareReadySetAsync(null, default);
        Assert.Equal(5, env.Ai.Calls.Count - calls); Assert.Equal(3, (await env.Content.ReadySetAsync()).Count);
        Assert.All(env.Providers.Where(p => p.Brand != WheelBrand.Vesser), p => Assert.Equal(before[p.Brand], p.Discoveries));
        Assert.Equal(before[WheelBrand.Vesser] + 1, env.Providers.Single(p => p.Brand == WheelBrand.Vesser).Discoveries);
        var run = Assert.Single(await env.Content.RunsAsync()); Assert.Equal("Zakończono", run.Status);
        Assert.Single(await env.Content.ArticlesAsync(), a => a.AutomationRunId == run.Id);
        Assert.EndsWith(":success", Assert.Single(await env.Services.GetRequiredService<NotificationService>().HistoryAsync()).RunId);
    }
    [Fact] public async Task PublishedArticleIsReplenishedWhileOtherReadyArticlesStay()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); await env.Content.PrepareReadySetAsync(null, default);
        var previous = await env.Content.ReadySetAsync(); var jr = previous.Single(a => a.Gallery.Brand == WheelBrand.JR); await env.Content.SetStatusAsync(jr.Id, false); await env.Content.SetStatusAsync(jr.Id, true);
        await using (var db = await env.Factory.CreateDbContextAsync()) { var g = Fixtures.Gallery(); g.Url += "-new"; g.ExternalId += "-new"; g.Vehicle.Model = "M2"; g.Sources.Single(s => s.Field == "CarModel").Value = "M2"; g.Images[0].LocalPath = Path.Combine(env.Root, "fixture.png"); db.Galleries.Add(g); await db.SaveChangesAsync(); }
        var calls = env.Ai.Calls.Count; env.Ai.NarrativeVariant = "kolejny"; var report = await env.Content.PrepareReadySetAsync(null, default); var next = await env.Content.ReadySetAsync();
        Assert.True(next.Count == 3, report + "\n" + string.Join("\n", (await env.Content.ArticlesAsync()).Select(a => a.Display + " " + a.Warnings))); Assert.NotEqual(jr.Id, next.Single(a => a.Gallery.Brand == WheelBrand.JR).Id); Assert.Equal(5, env.Ai.Calls.Count - calls);
        Assert.All(previous.Where(a => a.Gallery.Brand != WheelBrand.JR), a => Assert.Contains(next, n => n.Id == a.Id));
    }
    [Fact] public async Task DisabledPreparationNeverDiscoversOrCallsAi()
    {
        await using var env = await TestEnvironment.CreateAsync(); var settings = env.Services.GetRequiredService<SettingsService>(); var s = await settings.LoadAsync(); s.BackgroundPreparationEnabled = false; await settings.SaveAsync(s);
        await env.Content.PrepareReadySetAsync(null, default); Assert.Empty(env.Ai.Calls); Assert.All(env.Providers, p => Assert.Equal(0, p.Discoveries));
    }
    [Fact] public async Task MissingKeyFailsBeforeAnyDiscoveryOrAi()
    {
        await using var env = await TestEnvironment.CreateAsync(); ((MemorySecrets)env.Services.GetRequiredService<ISecretStore>()).Missing = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.PrepareReadySetAsync(null, default)); Assert.Empty(env.Ai.Calls); Assert.All(env.Providers, p => Assert.Equal(0, p.Discoveries)); Assert.Empty(await env.Content.RunsAsync());
    }
    [Fact] public async Task PartialRunResumesUnderSameBudgetAndReusesSuccessfulBrands()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); env.Blog.RecentFailure = WheelBrand.Concaver;
        await env.Content.PrepareReadySetAsync(null, default); Assert.Equal(2, (await env.Content.ReadySetAsync()).Count); var original = Assert.Single(await env.Content.RunsAsync());
        var calls = env.Ai.Calls.Count; env.Blog.RecentFailure = null; await env.Content.PrepareReadySetAsync(null, default);
        Assert.Equal(3, (await env.Content.ReadySetAsync()).Count); Assert.Equal(original.Id, Assert.Single(await env.Content.RunsAsync()).Id); Assert.Equal(5, env.Ai.Calls.Count - calls);
        Assert.Contains(await env.Services.GetRequiredService<NotificationService>().HistoryAsync(), n => n.RunId.EndsWith(":partial") && n.State == "Superseded");
    }
    [Fact] public void BrokenLatestVersionPublishedAndIncompleteArticlesDoNotFillStock()
    {
        Article Make(ArticleStatus status) => new() { Gallery = Fixtures.Gallery(), Status = status, Versions = [new() { Language = "PL", Body = "tekst" }, new() { Language = "EN", Body = "text" }] };
        var good = Make(ArticleStatus.Ready); var approved = Make(ArticleStatus.Approved); var published = Make(ArticleStatus.Published); var broken = Make(ArticleStatus.Ready); broken.Versions.Add(new() { Language = "EN", Revision = 2, Body = "new", WarningsJson = "[\"error\"]" });
        Assert.True(ReadyArticles.Available(good)); Assert.True(ReadyArticles.Available(approved)); Assert.False(ReadyArticles.Available(published)); Assert.False(ReadyArticles.Available(broken)); Assert.Single(ReadyArticles.Set([good, approved, published, broken]));
    }
    [Fact] public async Task OldShortReadyTextIsNotAdvertisedAsACompleteCurrentArticle()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); await env.Content.PrepareReadySetAsync(null, default);
        await using (var db = await env.Factory.CreateDbContextAsync())
        {
            var jr = await db.FullArticles.SingleAsync(a => a.Gallery.Brand == WheelBrand.JR);
            foreach (var version in jr.Versions) version.Body = "Krótki stary tekst";
            await db.SaveChangesAsync();
        }
        var ready = await env.Content.ReadySetAsync(); Assert.Equal(2, ready.Count); Assert.DoesNotContain(ready, a => a.Gallery.Brand == WheelBrand.JR);
    }
    [Fact] public void BackgroundTaskStartsAtLogonAndRepeatsWithoutParallelOrElevatedExecutions()
    {
        var xml = XDocument.Parse(WindowsScheduler.BackgroundTaskXml(Fixtures.Settings(), @"C:\WT & Blog\Worker.exe", @"PC\User & Account")); XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Single(xml.Descendants(ns + "LogonTrigger")); Assert.Single(xml.Descendants(ns + "CalendarTrigger")); Assert.Equal("PT2H", xml.Descendants(ns + "Repetition").Single().Element(ns + "Interval")!.Value);
        Assert.Equal("IgnoreNew", xml.Descendants(ns + "MultipleInstancesPolicy").Single().Value); Assert.Equal("InteractiveToken", xml.Descendants(ns + "LogonType").Single().Value); Assert.Equal("--prepare-stock", xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal(@"C:\WT & Blog\Worker.exe", xml.Descendants(ns + "Command").Single().Value); Assert.Equal("true", xml.Descendants(ns + "StartWhenAvailable").Single().Value);
    }
}
