using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WheelContentManager.Core;

namespace WheelContentManager.GalleryProviders;

public sealed record PublishedPost(string Url, string Text, HashSet<string> References);
public sealed class BlogPublicationChecker(SiteClient client) : IBlogPublicationChecker
{
    private readonly Dictionary<WheelBrand, (DateTimeOffset Time, IReadOnlyList<PublishedPost> Posts)> cache = [];
    public async Task<BlogCheck> CheckAsync(Gallery gallery, CancellationToken ct)
    {
        if (!cache.TryGetValue(gallery.Brand, out var snapshot) || DateTimeOffset.UtcNow - snapshot.Time > TimeSpan.FromMinutes(15))
        {
            var host = gallery.Brand switch { WheelBrand.JR => "jr-wheels.com", WheelBrand.Concaver => "concaverwheels.com", _ => "vesserforged.com" };
            var queue = new Queue<Uri>(); queue.Enqueue(new($"https://{host}/blog")); var pages = new HashSet<string>(); var urls = new HashSet<string>();
            while (queue.TryDequeue(out var page))
            {
                if (!pages.Add(page.AbsoluteUri)) continue;
                if (pages.Count > 100) throw new InvalidOperationException("Kontrola /blog przekroczyła limit stron; generowanie zatrzymano.");
                var doc = Document(await client.GetAsync(page, false, ct));
                var links = BlogLinks(doc, page);
                if (links.Count == 0) throw new InvalidOperationException("Nie rozpoznano listy /blog; generowanie zatrzymano.");
                foreach (var uri in links)
                    if (IsList(uri)) { if (!pages.Contains(uri.AbsoluteUri)) queue.Enqueue(uri); }
                    else urls.Add(uri.AbsoluteUri);
            }
            if (urls.Count == 0) throw new InvalidOperationException("Nie znaleziono wpisów /blog; nie można potwierdzić braku duplikatu.");
            var posts = new List<PublishedPost>();
            foreach (var url in urls) posts.Add(ParsePost(await client.GetAsync(new(url), false, ct), new(url)));
            snapshot = (DateTimeOffset.UtcNow, posts); cache[gallery.Brand] = snapshot;
        }
        return Match(gallery, snapshot.Posts);
    }
    private static HtmlDocument Document(string html) { var doc = new HtmlDocument(); doc.LoadHtml(html); return doc; }
    private static bool IsList(Uri uri) => Regex.IsMatch(uri.AbsolutePath.TrimEnd('/'), @"^/blog(?:/\d+)?$");
    public static IReadOnlyList<Uri> BlogLinks(HtmlDocument doc, Uri page)
    {
        var basis = Basis(doc, page); var result = new Dictionary<string, Uri>();
        foreach (var a in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            if (!Uri.TryCreate(basis, HtmlEntity.DeEntitize(a.GetAttributeValue("href", "")), out var uri) || uri.Scheme != "https" || uri.Host != page.Host) continue;
            if (!uri.AbsolutePath.StartsWith("/blog/", StringComparison.Ordinal) && uri.AbsolutePath != "/blog") continue;
            if (uri.Query.Contains("page=-")) continue;
            var normalized = new Uri(Normalization.Url(uri.GetLeftPart(UriPartial.Path)) + uri.Query);
            result[normalized.AbsoluteUri] = normalized;
        }
        return result.Values.ToArray();
    }
    private static Uri Basis(HtmlDocument doc, Uri uri) => Uri.TryCreate(uri, doc.DocumentNode.SelectSingleNode("//base[@href]")?.GetAttributeValue("href", ""), out var basis) && basis.Host == uri.Host ? basis : new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    public static PublishedPost ParsePost(string html, Uri uri)
    {
        var doc = Document(html); var basis = Basis(doc, uri);
        var roots = doc.DocumentNode.SelectNodes("//*[contains(concat(' ',normalize-space(@class),' '),' blog-content ') or contains(concat(' ',normalize-space(@class),' '),' subpage-content-blog ') or contains(concat(' ',normalize-space(@class),' '),' blog-header ')]");
        if (roots == null) throw new InvalidOperationException("Nie rozpoznano treści wpisu /blog; kontrola duplikatów zatrzymana.");
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots) foreach (var node in root.SelectNodes(".//*[@href or @src or @data-src]") ?? Enumerable.Empty<HtmlNode>())
            foreach (var attr in new[] { "href", "src", "data-src" })
                if (Uri.TryCreate(basis, HtmlEntity.DeEntitize(node.GetAttributeValue(attr, "")), out var link) && link.Scheme == "https" && link.Host == uri.Host) references.Add(Normalization.Url(link.GetLeftPart(UriPartial.Path)));
        return new(uri.AbsoluteUri, HtmlEntity.DeEntitize(string.Join(" ", roots.Select(x => x.InnerText))), references);
    }
    public static BlogCheck Match(Gallery g, IReadOnlyList<PublishedPost> posts)
    {
        var gallery = Normalization.Url(g.Url); var images = g.Images.Select(x => Normalization.Url(new Uri(x.Url).GetLeftPart(UriPartial.Path))).ToHashSet();
        foreach (var post in posts) if (post.References.Contains(gallery) || post.References.Overlaps(images)) return new(true, false, post.Url, "Opublikowano: ta sama galeria lub zdjęcie");
        static string Tokens(string s) => " " + Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim() + " ";
        var car = Tokens(g.Vehicle.Model ?? ""); var wheel = Tokens(g.Specification.Model ?? "");
        foreach (var post in posts)
        {
            var text = Tokens(post.Text);
            if (car.Trim().Length > 1 && wheel.Trim().Length > 1 && text.Contains(car) && text.Contains(wheel)) return new(false, true, post.Url, "Możliwy duplikat: samochód i felgi — sprawdź wpis");
        }
        return new(false, false, null, "Sprawdzono /blog: nie znaleziono powiązanego wpisu");
    }
}
