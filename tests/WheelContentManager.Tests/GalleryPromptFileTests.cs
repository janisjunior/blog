using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
using WheelContentManager.GalleryProviders;
using Xunit;
namespace WheelContentManager.Tests;
public class GalleryPromptFileTests
{
    [Fact] public async Task ExportLoadsGalleryAndProductOnceAndRefreshesThemOnNextExport()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await env.Content.RefreshPromptGalleriesAsync(WheelBrand.JR, null, default);
        var g = (await env.Content.GalleriesAsync()).Single(); var provider = env.Providers.Single(p => p.Brand == g.Brand);
        var before = provider.Loads;
        await env.Content.ExportGalleryPromptFilesAsync(g.Id, Path.Combine(env.Root, "files"), null, default);
        Assert.Equal(before + 1, provider.Loads);
        provider.WrongProduct = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.ExportGalleryPromptFilesAsync(g.Id, Path.Combine(env.Root, "files"), null, default));
        Assert.Equal(before + 2, provider.Loads); Assert.Empty(env.Ai.Calls);
        provider.WrongProduct = false; provider.MissingProduct = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.ExportGalleryPromptFilesAsync(g.Id, Path.Combine(env.Root, "files"), null, default));
    }
    [Fact] public async Task LocalArticleDoesNotHideGalleryOrBlockCompletePromptDocument()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.Content.SyncAsync(null, default);
        var g = (await env.Content.GalleriesAsync())[0]; await env.Content.GenerateAsync(g.Id, false, false, null, null, default);
        Assert.True((await env.Content.GalleriesAsync()).Single(x => x.Id == g.Id).Used);
        var available = await env.Content.RefreshPromptGalleriesAsync(g.Brand, null, default); Assert.Contains(available, x => x.Id == g.Id);
        var calls = env.Ai.Calls.Count;
        var package = await env.Content.ExportGalleryPromptFilesAsync(g.Id, Path.Combine(env.Root, "files"), null, default);
        Assert.Equal(calls, env.Ai.Calls.Count); Assert.Single(await env.Content.ArticlesAsync());
        Assert.True(File.Exists(package.Document)); Assert.True(File.Exists(package.Instructions)); Assert.True(File.Exists(package.Photo));
        using var doc = WordprocessingDocument.Open(package.Document, false); var main = doc.MainDocumentPart!;
        Assert.DoesNotContain("[ARTYKUL_", package.Prompt);
        Assert.StartsWith("GALERIA ŹRÓDŁOWA", package.Prompt);
        Assert.Contains(g.Url, package.Prompt.Split("Przeczytaj")[0]);
        Assert.Contains("najpierw wersja angielska EN, potem niezależna wersja polska PL", package.Prompt);
        Assert.Contains(g.Url, main.Document!.Body!.Elements<Paragraph>().First().InnerText);
        Assert.True((await env.Content.GalleryAsync(g.Id))!.PromptUsed);
        Assert.Contains("od razu wykonaj", main.Document!.InnerText); Assert.Contains("2200–2600", main.Document.InnerText);
        Assert.Contains(main.HyperlinkRelationships, link => link.Uri.AbsoluteUri == g.Url);
        Assert.Contains(main.HyperlinkRelationships, link => link.Uri.AbsoluteUri.Contains("/product/"));
        Assert.Single(main.ImageParts); Assert.NotEmpty(main.Document.Descendants<Drawing>());
        var again = await env.Content.ExportGalleryPromptFilesAsync(g.Id, Path.Combine(env.Root, "files"), null, default);
        Assert.Equal(package.Folder, again.Folder); Assert.Equal(calls, env.Ai.Calls.Count);
    }
    [Fact] public async Task UsedPromptGalleryStaysVisibleAtEndAndCanBeRestoredWithoutChangingArticleUsage()
    {
        await using var env = await TestEnvironment.CreateAsync();
        var rows = await env.Content.RefreshPromptGalleriesAsync(WheelBrand.JR, null, default); var original = rows.Single();
        await using (var db = await env.Factory.CreateDbContextAsync())
        {
            var extra = Fixtures.Gallery(WheelBrand.JR); extra.ExternalId = "extra"; extra.Url += "-extra";
            extra.BlogCheckedAt = DateTimeOffset.UtcNow; extra.ListingOrder = original.ListingOrder + 1; db.Add(extra); await db.SaveChangesAsync();
        }
        await env.Content.SetPromptGalleryUsedAsync(original.Id, true);
        rows = (await env.Content.CachedPromptGalleriesAsync(WheelBrand.JR)).Galleries;
        Assert.Equal(2, rows.Count); Assert.Equal(original.Id, rows.Last().Id); Assert.True(rows.Last().PromptUsed);
        Assert.False((await env.Content.GalleryAsync(original.Id))!.Used);
        rows = await env.Content.RefreshPromptGalleriesAsync(WheelBrand.JR, null, default);
        Assert.Equal(original.Id, rows.Last().Id); Assert.True(rows.Last().PromptUsed);
        await env.Content.SetPromptGalleryUsedAsync(original.Id, false);
        rows = (await env.Content.CachedPromptGalleriesAsync(WheelBrand.JR)).Galleries;
        Assert.Equal(original.Id, rows.First().Id); Assert.False(rows.First().PromptUsed);
    }
    [Theory][InlineData(true, false)][InlineData(false, true)] public async Task WebsitePublicationBlocksListAndExport(bool published, bool possible)
    {
        await using var env = await TestEnvironment.CreateAsync(); env.Blog.Result = new(published, possible, "https://fixture.example/blog/existing", "Duplikat");
        Assert.Empty(await env.Content.RefreshPromptGalleriesAsync(WheelBrand.JR, null, default));
        var g = (await env.Content.GalleriesAsync()).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.ExportGalleryPromptFilesAsync(g.Id, Path.Combine(env.Root, "files"), null, default));
        Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task ArchiveFailureDoesNotCreateVerifiedAvailableList()
    {
        await using var env = await TestEnvironment.CreateAsync(); env.Blog.Fail = true;
        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => env.Content.RefreshPromptGalleriesAsync(WheelBrand.JR, null, default));
        Assert.Empty((await env.Content.CachedPromptGalleriesAsync(WheelBrand.JR)).Galleries); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task WarmArchiveVisitsAllPagesEvenIfFirstEntryMatchesAndChecksNeedNoMoreHttp()
    {
        using var handler = new BlogHandler { FirstMatches = true, FailSecond = true }; using var http = new HttpClient(handler);
        var checker = new BlogPublicationChecker(new SiteClient(http));
        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.PrepareArchiveAsync(WheelBrand.JR, default));
        handler.FailSecond = false; await checker.PrepareArchiveAsync(WheelBrand.JR, default);
        var before = handler.Requests.Count; var g = Fixtures.Gallery(); g.Images = [new() { Url = "https://jr-wheels.com/matched.jpg" }];
        Assert.True((await checker.CheckAsync(g, default)).Published); Assert.Equal(before, handler.Requests.Count);
    }
}
