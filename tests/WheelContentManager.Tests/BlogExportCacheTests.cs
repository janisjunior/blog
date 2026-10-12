using System.Text.Json.Nodes;
using WheelContentManager.Core;
using WheelContentManager.GalleryProviders;
using Xunit;
namespace WheelContentManager.Tests;
public class BlogExportCacheTests
{
    private static Gallery Gallery()
    {
        var g = Fixtures.Gallery(); g.Images = [new() { Url = "https://jr-wheels.com/matched.jpg" }]; return g;
    }
    [Fact] public async Task WarmExportChecksAllLiveIndexPagesWithoutRefetchingOldArticleBodies()
    {
        using var handler = new BlogHandler(); using var http = new HttpClient(handler);
        var checker = new BlogPublicationChecker(new SiteClient(http)); await checker.PrepareArchiveAsync(WheelBrand.JR, default);
        var count = handler.Requests.Count;
        Assert.True((await checker.CheckForExportAsync(Gallery(), default)).Published);
        Assert.Equal(new[] { "/blog", "/blog?page=1" }, handler.Requests.Skip(count));
    }
    [Fact] public async Task NewPostBlocksExportImmediatelyEvenWithWarmNegativeArchive()
    {
        using var handler = new BlogHandler { SecondMatches = false }; using var http = new HttpClient(handler);
        var checker = new BlogPublicationChecker(new SiteClient(http)); await checker.PrepareArchiveAsync(WheelBrand.JR, default);
        Assert.False((await checker.CheckForExportAsync(Gallery(), default)).Published);
        handler.NewPost = true; var count = handler.Requests.Count;
        Assert.True((await checker.CheckForExportAsync(Gallery(), default)).Published);
        Assert.Contains("/blog/new", handler.Requests.Skip(count)); Assert.DoesNotContain("/blog/first", handler.Requests.Skip(count));
    }
    [Fact] public async Task WarmArchiveCannotHideFailedPagination()
    {
        using var handler = new BlogHandler { SecondMatches = false }; using var http = new HttpClient(handler);
        var checker = new BlogPublicationChecker(new SiteClient(http)); await checker.PrepareArchiveAsync(WheelBrand.JR, default);
        handler.FailIndex = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.CheckForExportAsync(Gallery(), default));
        handler.FailIndex = false; handler.NewPost = true;
        Assert.True((await checker.CheckForExportAsync(Gallery(), default)).Published);
    }
    [Fact] public async Task RestartReusesCompleteArchiveAndExpiredArchiveRefetchesEditedBodies()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wt-blog-cache-" + Guid.NewGuid());
        try
        {
            using var handler = new BlogHandler { SecondMatches = false }; using var http = new HttpClient(handler);
            var checker = new BlogPublicationChecker(new SiteClient(http), folder); await checker.PrepareArchiveAsync(WheelBrand.JR, default);
            var path = Path.Combine(folder, "JR.json"); var originalTime = JsonNode.Parse(await File.ReadAllTextAsync(path))!["Time"]!.ToString();
            var count = handler.Requests.Count;
            var restarted = new BlogPublicationChecker(new SiteClient(http), folder);
            Assert.False((await restarted.CheckForExportAsync(Gallery(), default)).Published);
            Assert.DoesNotContain("/blog/first", handler.Requests.Skip(count)); Assert.Contains("/blog?page=1", handler.Requests.Skip(count));
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(originalTime, saved["Time"]!.ToString()); // Successful exports must not keep old bodies alive indefinitely.
            saved["Time"] = DateTimeOffset.UtcNow.AddMinutes(-16); await File.WriteAllTextAsync(path, saved.ToJsonString());
            handler.FirstMatches = true; count = handler.Requests.Count;
            restarted = new BlogPublicationChecker(new SiteClient(http), folder);
            Assert.True((await restarted.CheckForExportAsync(Gallery(), default)).Published);
            Assert.Contains("/blog/first", handler.Requests.Skip(count)); Assert.Contains("/blog/second", handler.Requests.Skip(count));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact] public async Task CorruptDiskArchiveFallsBackToFullVerifiedFetch()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wt-blog-cache-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "JR.json"), "{broken");
            using var handler = new BlogHandler(); using var http = new HttpClient(handler);
            var checker = new BlogPublicationChecker(new SiteClient(http), folder);
            Assert.True((await checker.CheckForExportAsync(Gallery(), default)).Published);
            Assert.Contains("/blog/first", handler.Requests); Assert.Contains("/blog/second", handler.Requests);
        }
        finally { Directory.Delete(folder, true); }
    }
}
