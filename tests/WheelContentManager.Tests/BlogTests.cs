using HtmlAgilityPack;
using WheelContentManager.Core;
using WheelContentManager.GalleryProviders;
using Xunit;

namespace WheelContentManager.Tests;
public class BlogTests
{
    [Theory][InlineData("JR", "jr-wheels.com")][InlineData("Concaver", "concaverwheels.com")][InlineData("Vesser", "vesserforged.com")]
    public void RealPostsMatchTheirGalleryPhotos(string brand, string host)
    {
        var post = BlogPublicationChecker.ParsePost(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Live", brand + "-blog.html")), new($"https://{host}/blog/test"));
        var photo = post.References.First(x => x.Contains("/upload/gallery_") || x.Contains("/zdjecia/2026/07/29/"));
        var gallery = Fixtures.Gallery(Enum.Parse<WheelBrand>(brand)); gallery.Images = [new() { Url = photo + "?cache=1" }];
        var result = BlogPublicationChecker.Match(gallery, [post]); Assert.True(result.Published); Assert.Equal(post.Url, result.Url);
        gallery.Images = [new() { Url = $"https://{host}/different.jpg" }]; gallery.Vehicle.Model = "Unique test model"; Assert.False(BlogPublicationChecker.Match(gallery, [post]).Published);
    }
    [Theory][InlineData("https://jr-wheels.com/blog", "/blog?page=1")][InlineData("https://concaverwheels.com/blog", "blog/11")][InlineData("https://vesserforged.com/blog", "blog/2/")]
    public void DiscoversPaginationAndArticleLinks(string root, string pagination)
    {
        var doc = new HtmlDocument(); doc.LoadHtml($"<base href='{new Uri(root).GetLeftPart(UriPartial.Authority)}/'><a href='{pagination}'>older</a><a href='/blog/article-name'>entry</a><a href='/blog?page=-1'>newer</a><a href='https://external.invalid/blog/entry'>external</a>");
        var links = BlogPublicationChecker.BlogLinks(doc, new(root)); Assert.Equal(2, links.Count); Assert.Contains(links, x => x.AbsolutePath == "/blog/article-name");
    }
    [Fact] public void MatchingCarAndWheelsIsPossibleRatherThanConfirmedDuplicate()
    {
        var g = Fixtures.Gallery(); g.Vehicle.Model = "M4"; g.Specification.Model = "CVR3";
        var result = BlogPublicationChecker.Match(g, [new("https://concaverwheels.com/blog/bmw", "BMW M4 on CVR3", [])]); Assert.False(result.Published); Assert.True(result.PossibleDuplicate);
        Assert.False(BlogPublicationChecker.Match(g, [new("https://concaverwheels.com/blog/bmw", "BMW M440 on CVR30", [])]).PossibleDuplicate);
    }
    [Fact] public void NavigationAndRelatedPhotosDoNotCountAsArticleEvidence()
    {
        var post = BlogPublicationChecker.ParsePost("<nav><a href='/vehicle-gallery/7'>gallery</a></nav><div class='blog-content'>Article</div><footer><img src='/photo.jpg'></footer>", new("https://jr-wheels.com/blog/test")); Assert.DoesNotContain("https://jr-wheels.com/vehicle-gallery/7", post.References); Assert.DoesNotContain("https://jr-wheels.com/photo.jpg", post.References);
        Assert.Throws<InvalidOperationException>(() => BlogPublicationChecker.ParsePost("<div>Unexpected structure</div>", new("https://jr-wheels.com/blog/test")));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task FullCrawlerIncludesSecondPageAndNeverCachesPartialFailure(bool failure)
    {
        using var handler = new BlogHandler { FailSecond = failure }; using var http = new HttpClient(handler); var checker = new BlogPublicationChecker(new SiteClient(http));
        var g = Fixtures.Gallery(); g.Images = [new() { Url = "https://jr-wheels.com/matched.jpg" }];
        if (failure)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => checker.CheckAsync(g, default)); handler.FailSecond = false;
        }
        var match = await checker.CheckAsync(g, default); Assert.True(match.Published); Assert.EndsWith("/blog/second", match.Url);
        Assert.Contains("/blog?page=1", handler.Requests); var count = handler.Requests.Count; await checker.CheckAsync(g, default); Assert.Equal(count, handler.Requests.Count);
    }
    [Fact] public async Task NetworkTimeoutIsNotMistakenForUserCancellation()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); env.Blog.Timeout = true;
        var gallery = (await env.Content.GalleriesAsync())[0]; var error = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(gallery.Id, false, true, null, null, default));
        Assert.Contains("czas oczekiwania", error.Message); Assert.Empty(env.Ai.Calls); Assert.True((await env.Content.GalleriesAsync()).Single(x => x.Id == gallery.Id).BlogBlocked);
    }
    [Theory][InlineData(false, true)][InlineData(false, false)][InlineData(true, false)]
    public async Task PublishedPossibleAndFailedChecksBlockAiBeforeAnyExpense(bool failure, bool published)
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.PrepareAsync(); env.Blog.Fail = failure; env.Blog.Result = new(published, !published, "https://jr-wheels.com/blog/test", "Istniejący wpis");
        var gallery = (await env.Content.GalleriesAsync())[0]; await Assert.ThrowsAsync<InvalidOperationException>(() => env.Content.GenerateAsync(gallery.Id, true, true, null, null, default));
        Assert.Empty(env.Ai.Calls); Assert.True((await env.Content.GalleriesAsync()).Single(x => x.Id == gallery.Id).BlogBlocked); Assert.Empty(await env.Content.ArticlesAsync());
    }
}

internal sealed class BlogHandler : HttpMessageHandler
{
    public bool FailSecond { get; set; } public List<string> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.PathAndQuery; Requests.Add(path);
        var html = path switch
        {
            "/robots.txt" => "User-agent: *\nAllow: /",
            "/blog" => "<a href='/blog/first'>first</a><a href='/blog?page=1'>older</a>",
            "/blog?page=1" => "<a href='/blog/second'>second</a><a href='/blog'>newer</a>",
            "/blog/first" => "<div class='blog-content'>Unrelated entry</div>",
            "/blog/second" => FailSecond ? "changed HTML" : "<div class='blog-content'><img src='/matched.jpg'></div>",
            _ => throw new InvalidOperationException("Unexpected request: " + path)
        };
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(html) });
    }
}
