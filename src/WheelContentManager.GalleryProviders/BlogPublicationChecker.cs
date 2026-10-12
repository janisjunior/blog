using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WheelContentManager.Core;

namespace WheelContentManager.GalleryProviders;

public sealed record PublishedPost(string Url, string Text, HashSet<string> References);
public sealed class BlogPublicationChecker(SiteClient client, string? archiveCacheDirectory = null) : IBlogPublicationChecker
{
    private sealed record SavedArchive(int Version, DateTimeOffset Time, IReadOnlyList<PublishedPost> Posts);
    private static readonly TimeSpan ArchiveLifetime = TimeSpan.FromMinutes(15);
    private readonly HashSet<WheelBrand> ignoredDiskBrands = [];
    private sealed record TokenizedPost(string Text);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PublishedPost, TokenizedPost> normalizedPosts = new();
    private readonly Dictionary<WheelBrand, (DateTimeOffset Time, IReadOnlyList<PublishedPost> Posts)> cache = [];
    private readonly Dictionary<string, (DateTimeOffset Time, BlogCheck Result)> confirmed = [];
    private readonly Dictionary<WheelBrand, (DateTimeOffset Time, IReadOnlyList<RecentBlogPost> Posts)> recent = [];
    public async Task<IReadOnlyList<RecentBlogPost>> RecentAsync(WheelBrand brand, CancellationToken ct)
    {
        if (recent.TryGetValue(brand, out var snapshot) && DateTimeOffset.UtcNow - snapshot.Time < TimeSpan.FromMinutes(15)) return snapshot.Posts;
        var host = brand switch { WheelBrand.JR => "jr-wheels.com", WheelBrand.Concaver => "concaverwheels.com", _ => "vesserforged.com" };
        var page = new Uri($"https://{host}/blog"); var listing = Document(await client.GetAsync(page, false, ct));
        var urls = BlogLinks(listing, page).Where(u => !IsList(u)).Take(10).ToArray();
        if (urls.Length == 0) throw new InvalidOperationException("Nie rozpoznano ostatnich wpisów /blog. Sugestie są niedostępne.");
        var posts = new List<RecentBlogPost>();
        foreach (var uri in urls) posts.Add(ParseRecent(await client.GetAsync(uri, false, ct), uri));
        var result = posts.OrderByDescending(p => p.Published ?? DateTimeOffset.MinValue).ToArray();
        recent[brand] = (DateTimeOffset.UtcNow, result); return result;
    }
    public static RecentBlogPost ParseRecent(string html, Uri uri)
    {
        _ = ParsePost(html, uri); // require a recognised article, not a challenge or an error page
        var doc = Document(html);
        var title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1 | //div[@class='blog-details']/div[@class='title']")?.InnerText ?? "").Trim();
        if (title.Length == 0) throw new InvalidOperationException("Brak tytułu ostatniego wpisu /blog.");
        var time = doc.DocumentNode.SelectSingleNode("//time[@datetime]")?.GetAttributeValue("datetime", "");
        var header = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'blog-content-title') or contains(@class,'blog-header')]")?.InnerText ?? "";
        var date = Regex.Match(header, @"\b\d{1,2}[./-]\d{1,2}[./-](?:\d{4}|\d{2})\b").Value;
        DateTimeOffset? published = null;
        if (DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var timestamp)) published = timestamp;
        else if (DateTime.TryParseExact(date, new[] { "dd/MM/yy", "dd/MM/yyyy", "dd.MM.yyyy", "d.M.yyyy", "dd-MM-yyyy", "d/M/yy", "d/M/yyyy" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)) published = new DateTimeOffset(parsed, TimeSpan.Zero);
        var models = Regex.Matches(title, @"(?i)\b(?:JR|SL|CVR|VSR|VF)[ -]?\d+\b").Select(m => TopicPlanner.Model(m.Value)).Distinct().ToArray();
        return new(uri.AbsoluteUri, title, published, models);
    }
    public Task<BlogCheck> CheckFreshAsync(Gallery gallery, CancellationToken ct)
    {
        InvalidateArchive(gallery.Brand);
        foreach (var key in confirmed.Keys.Where(key => key.StartsWith(gallery.Brand + ":", StringComparison.Ordinal)).ToArray()) confirmed.Remove(key);
        return CheckAsync(gallery, ct);
    }
    public async Task PrepareArchiveAsync(WheelBrand brand, CancellationToken ct)
    {
        InvalidateArchive(brand);
        foreach (var key in confirmed.Keys.Where(key => key.StartsWith(brand + ":", StringComparison.Ordinal)).ToArray()) confirmed.Remove(key);
        var posts = await LoadArchiveAsync(brand, null, ct);
        await SaveArchiveAsync(brand, DateTimeOffset.UtcNow, posts, ct);
    }
    public async Task<BlogCheck> CheckForExportAsync(Gallery gallery, CancellationToken ct)
    {
        // Always walk the live index, including pagination. Only reuse recent, fully
        // parsed article bodies; new URLs are fetched immediately and failures block export.
        var snapshot = RecentArchive(gallery.Brand);
        var reuse = snapshot?.Posts.ToDictionary(p => p.Url, StringComparer.Ordinal);
        var posts = await LoadArchiveAsync(gallery.Brand, null, ct, reuse);
        // Reusing articles must not extend their original freshness deadline.
        await SaveArchiveAsync(gallery.Brand, snapshot?.Time ?? DateTimeOffset.UtcNow, posts, ct);
        var result = Match(gallery, posts);
        return result with { Message = result.Message + $" · aktualna lista /blog, {posts.Count} wpisów; treści sprawdzane co najwyżej 15 minut temu" };
    }
    private void InvalidateArchive(WheelBrand brand)
    {
        cache.Remove(brand); ignoredDiskBrands.Add(brand);
        if (archiveCacheDirectory == null) return;
        try { File.Delete(Path.Combine(archiveCacheDirectory, brand + ".json")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private SavedArchive? RecentArchive(WheelBrand brand)
    {
        if (cache.TryGetValue(brand, out var memory) && IsRecent(memory.Time)) return new(1, memory.Time, memory.Posts);
        if (archiveCacheDirectory == null || ignoredDiskBrands.Contains(brand)) return null;
        try
        {
            var path = Path.Combine(archiveCacheDirectory, brand + ".json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) return null;
            var saved = System.Text.Json.JsonSerializer.Deserialize<SavedArchive>(File.ReadAllText(path));
            if (saved == null || saved.Version != 1 || !IsRecent(saved.Time) || saved.Posts == null || saved.Posts.Count == 0 ||
                saved.Posts.Any(p => p == null || p.Text == null || p.References == null || !Uri.TryCreate(p.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != Host(brand) || !uri.AbsolutePath.StartsWith("/blog/", StringComparison.Ordinal)) ||
                saved.Posts.Select(p => p.Url).Distinct().Count() != saved.Posts.Count) return null;
            return saved;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }
    private static bool IsRecent(DateTimeOffset time) => time <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - time <= ArchiveLifetime;
    private static string Host(WheelBrand brand) => brand switch { WheelBrand.JR => "jr-wheels.com", WheelBrand.Concaver => "concaverwheels.com", _ => "vesserforged.com" };
    private async Task SaveArchiveAsync(WheelBrand brand, DateTimeOffset time, IReadOnlyList<PublishedPost> posts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); cache[brand] = (time, posts);
        if (archiveCacheDirectory == null) return;
        var path = Path.Combine(archiveCacheDirectory, brand + ".json"); var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(archiveCacheDirectory);
            await File.WriteAllTextAsync(temp, System.Text.Json.JsonSerializer.Serialize(new SavedArchive(1, time, posts)), ct);
            File.Move(temp, path, true); ignoredDiskBrands.Remove(brand);
        }
        catch (IOException) { /* Disk cache is optional; verified in-memory results remain usable. */ }
        catch (UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public async Task<BlogCheck> CheckAsync(Gallery gallery, CancellationToken ct)
    {
        var key = gallery.Brand + ":" + Normalization.Url(gallery.Url);
        if (confirmed.TryGetValue(key, out var previous) && DateTimeOffset.UtcNow - previous.Time <= TimeSpan.FromMinutes(15)) return previous.Result;
        if (!cache.TryGetValue(gallery.Brand, out var snapshot) || DateTimeOffset.UtcNow - snapshot.Time > TimeSpan.FromMinutes(15))
        {
            var posts = await LoadArchiveAsync(gallery.Brand, gallery, ct);
            if (confirmed.TryGetValue(key, out var match)) return match.Result;
            snapshot = (DateTimeOffset.UtcNow, posts); await SaveArchiveAsync(gallery.Brand, snapshot.Time, posts, ct);
        }
        var result = Match(gallery, snapshot.Posts);
        return result with { Message = result.Message + $" · sprawdzono {snapshot.Posts.Count} wpisów" };
    }
    private async Task<IReadOnlyList<PublishedPost>> LoadArchiveAsync(WheelBrand brand, Gallery? gallery, CancellationToken ct, IReadOnlyDictionary<string, PublishedPost>? reuse = null)
    {
            var host = Host(brand);
            var queue = new Queue<Uri>(); queue.Enqueue(new($"https://{host}/blog")); var pages = new HashSet<string>(); var urls = new HashSet<string>(); var posts = new List<PublishedPost>();
            while (queue.TryDequeue(out var page))
            {
                if (!pages.Add(page.AbsoluteUri)) continue;
                if (pages.Count > 100) throw new InvalidOperationException("Kontrola /blog przekroczyła limit stron; generowanie zatrzymano.");
                var doc = Document(await client.GetAsync(page, false, ct));
                var links = BlogLinks(doc, page);
                if (links.Count == 0) throw new InvalidOperationException("Nie rozpoznano listy /blog; generowanie zatrzymano.");
                foreach (var uri in links)
                {
                    if (IsList(uri)) { if (!pages.Contains(uri.AbsoluteUri)) queue.Enqueue(uri); continue; }
                    if (!urls.Add(uri.AbsoluteUri)) continue;
                    var post = reuse != null && reuse.TryGetValue(uri.AbsoluteUri, out var previous) ? previous : ParsePost(await client.GetAsync(uri, false, ct), uri); posts.Add(post);
                    var exact = gallery == null ? null : Match(gallery, [post]);
                    if (exact?.Published == true)
                    {
                        var found = exact with { Message = exact.Message + " · potwierdzone dopasowanie" };
                        confirmed[gallery!.Brand + ":" + Normalization.Url(gallery.Url)] = (DateTimeOffset.UtcNow, found); return posts;
                    }
                }
            }
            if (posts.Count == 0) throw new InvalidOperationException("Nie znaleziono wpisów /blog; nie można potwierdzić braku duplikatu.");
        return posts;
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
            var text = normalizedPosts.GetValue(post, p => new(Tokens(p.Text))).Text;
            if (car.Trim().Length > 1 && wheel.Trim().Length > 1 && text.Contains(car) && text.Contains(wheel)) return new(false, true, post.Url, "Możliwy duplikat: samochód i felgi — sprawdź wpis");
        }
        return new(false, false, null, "Sprawdzono /blog: nie znaleziono powiązanego wpisu");
    }
}
