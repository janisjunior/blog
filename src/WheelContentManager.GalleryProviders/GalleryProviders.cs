using System.Net;
using System.Text.Json;
using HtmlAgilityPack;
using WheelContentManager.Core;

namespace WheelContentManager.GalleryProviders;

// Profile zawierają wyłącznie selektory potwierdzone podczas inspekcji witryny.
// Brak profilu jest błędem, a nie pretekstem do generowania danych demonstracyjnych.
public sealed class SourceProfile
{
    public bool Verified { get; set; } public string Evidence { get; set; } = "";
    public string GalleryLinksXPath { get; set; } = ""; public string? NextPageXPath { get; set; }
    public Dictionary<string, string> FieldsXPath { get; set; } = [];
    public string ImagesXPath { get; set; } = ""; public string? ProductLinkXPath { get; set; }
    public string? ProductNameXPath { get; set; }
    public string? VehicleGalleryMarkerXPath { get; set; }
    public string ListCardsXPath { get; set; } = ""; public string ListLinkXPath { get; set; } = "."; public string MakesXPath { get; set; } = "";
    public Dictionary<string, string> ListFieldsXPath { get; set; } = []; public Dictionary<string, string> ProductFieldsXPath { get; set; } = [];
    public bool RequiresJavaScript { get; set; } public int MaxPages { get; set; } = 20;
}
public sealed class SiteClient(HttpClient http)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastRequest;
    private readonly Dictionary<string, string> robots = [];
    private async Task<string> DownloadAsync(Uri uri, CancellationToken ct)
    {
        for (int i = 0; ; i++)
        {
            HttpResponseMessage downloaded;
            try { downloaded = await http.GetAsync(uri, ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (i < 2) { await Task.Delay(TimeSpan.FromSeconds(2 + i * 3), ct); continue; }
                throw new InvalidOperationException($"Przekroczono czas oczekiwania na {uri.Host}{uri.AbsolutePath}. Kontrola źródła nie jest kompletna.");
            }
            using var response = downloaded;
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(ct);
            if (i < 2 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)) { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 2 + i * 3, 1, 30)), ct); continue; }
            throw new InvalidOperationException($"Źródło {uri.Host}: HTTP {(int)response.StatusCode}. Bez obchodzenia kontroli dostępu.");
        }
    }
    public static bool RobotsAllowed(string robotsText, string path)
    {
        var rules = new List<(bool Allow, string Path)>(); var relevant = false; var seenDirective = false;
        foreach (var raw in robotsText.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim(); var i = line.IndexOf(':'); if (i < 0) continue;
            var name = line[..i].Trim().ToLowerInvariant(); var value = line[(i + 1)..].Trim();
            if (name == "user-agent") { if (seenDirective) { relevant = false; seenDirective = false; } relevant |= value == "*" || value.Equals("WheelContentManager", StringComparison.OrdinalIgnoreCase); }
            else if (name is "allow" or "disallow") { seenDirective = true; if (relevant && value.Length > 0) rules.Add((name == "allow", value)); }
        }
        var matched = rules.Where(x => System.Text.RegularExpressions.Regex.IsMatch(path, "^" + System.Text.RegularExpressions.Regex.Escape(x.Path).Replace(@"\*", ".*").Replace(@"\$", "$"))).OrderByDescending(x => x.Path.Length).ThenByDescending(x => x.Allow).ToList();
        return matched.Count == 0 || matched[0].Allow;
    }
    public async Task<string> GetAsync(Uri uri, bool javascript, CancellationToken ct)
    {
        if (uri.Scheme != "https") throw new InvalidOperationException("Źródła muszą korzystać z HTTPS.");
        await gate.WaitAsync(ct);
        try
        {
            var delay = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - lastRequest); if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            if (!robots.TryGetValue(uri.Host, out var rules))
            {
                using var r = await http.GetAsync(new Uri(uri, "/robots.txt"), ct);
                if (r.StatusCode == HttpStatusCode.NotFound) rules = "";
                else { if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"Nie można sprawdzić robots.txt: HTTP {(int)r.StatusCode}."); rules = await r.Content.ReadAsStringAsync(ct); }
                robots[uri.Host] = rules;
            }
            if (!RobotsAllowed(rules, uri.PathAndQuery)) throw new InvalidOperationException("robots.txt nie zezwala na pobranie tej strony.");
            string html;
            if (!javascript) html = await DownloadAsync(uri, ct);
            else
            {
                // Nawigacja przechodzi przez kontrolę HTTP; brak obchodzenia CAPTCHA.
                await DownloadAsync(uri, ct);
                using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
                var page = await browser.NewPageAsync(new() { UserAgent = "WheelContentManager/1.0" });
                var response = await page.GotoAsync(uri.AbsoluteUri, new() { WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle, Timeout = 30000 });
                if (response == null || !response.Ok) throw new InvalidOperationException("Przeglądarka nie pobrała strony źródłowej.");
                html = await page.ContentAsync(); ct.ThrowIfCancellationRequested();
            }
            if (html.Contains("captcha", StringComparison.OrdinalIgnoreCase) && html.Length < 30000) throw new InvalidOperationException("Strona wymaga CAPTCHA. Pobieranie zatrzymano.");
            return html;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException($"Przekroczono czas oczekiwania na {uri.Host}. Spróbuj ponownie; nie można potwierdzić kompletności źródła."); }
        finally { lastRequest = DateTime.UtcNow; gate.Release(); }
    }
}
public abstract class ProfileGalleryProvider(SiteClient client, string profilesFolder) : IGalleryProvider
{
    public abstract WheelBrand Brand { get; } public abstract Uri IndexUrl { get; }
    private SourceProfile LoadProfile()
    {
        var path = Path.Combine(profilesFolder, Brand + ".json");
        if (!File.Exists(path)) throw new InvalidOperationException($"{Brand.Name()}: brak zweryfikowanego profilu HTML dla {IndexUrl}. Sprawdź pliki Profiles w paczce aplikacji i docs/ZRODLA.md.");
        var profile = JsonSerializer.Deserialize<SourceProfile>(File.ReadAllText(path))!;
        if (!profile.Verified || string.IsNullOrWhiteSpace(profile.Evidence) || string.IsNullOrWhiteSpace(profile.GalleryLinksXPath) || string.IsNullOrWhiteSpace(profile.ImagesXPath)) throw new InvalidOperationException("Niepotwierdzony lub niekompletny profil źródła.");
        if (Brand == WheelBrand.Vesser && string.IsNullOrWhiteSpace(profile.VehicleGalleryMarkerXPath)) throw new InvalidOperationException("Vesser wymaga potwierdzonego rozróżnienia galerii pojazdu od produktu i wideo.");
        return profile;
    }
    public async Task<IReadOnlyList<Gallery>> DiscoverAsync(CancellationToken ct)
    {
        var p = LoadProfile(); var found = new Dictionary<string, Gallery>(); var seenPages = new HashSet<string>(); Uri? next = IndexUrl; var detected = DateTimeOffset.UtcNow;
        for (int page = 0; next != null && page < Math.Clamp(p.MaxPages, 1, 100); page++)
        {
            if (!seenPages.Add(Normalization.Url(next.AbsoluteUri))) throw new InvalidOperationException("Wykryto zapętloną paginację.");
            var doc = new HtmlDocument(); doc.LoadHtml(await client.GetAsync(next, p.RequiresJavaScript, ct));
            var basis = BaseUri(doc, next); var cards = doc.DocumentNode.SelectNodes(p.ListCardsXPath);
            if (cards == null || cards.Count == 0) throw new InvalidOperationException($"{Brand.Name()}: brak galerii. Możliwa zmiana struktury witryny.");
            var makes = (doc.DocumentNode.SelectNodes(p.MakesXPath) ?? Enumerable.Empty<HtmlNode>()).Select(x => (Id: x.GetAttributeValue("value", ""), Name: Clean(x.InnerText))).Where(x => x.Id is not ("" or "0") && x.Name.Length > 0).ToDictionary(x => x.Id, x => x.Name);
            foreach (var card in cards)
            {
                var link = card.SelectSingleNode(p.ListLinkXPath); var url = link == null ? null : Resolve(basis, Value(link)); if (url == null || found.ContainsKey(url)) continue;
                string F(string key) => p.ListFieldsXPath.TryGetValue(key, out var xpath) ? Clean(card.CreateNavigator().Evaluate("string(" + xpath + ")")?.ToString() ?? "") : "";
                var title = F("CarDisplay"); var make = makes.GetValueOrDefault(card.GetAttributeValue("data-brand", "")) ?? makes.Values.OrderByDescending(x => x.Length).FirstOrDefault(x => title.StartsWith(x + " ", StringComparison.OrdinalIgnoreCase));
                var model = make == null ? title : title.StartsWith(make + " ", StringComparison.OrdinalIgnoreCase) ? title[(make.Length + 1)..] : title;
                var thumbnail = F("Thumbnail"); var wheel = F("WheelModel"); var finish = F("Finish");
                var g = new Gallery { Brand = Brand, Url = url, ExternalId = url, ListingOrder = found.Count, FirstDetected = detected, ThumbnailUrl = Resolve(basis, thumbnail), Vehicle = new() { Make = make, Model = string.IsNullOrWhiteSpace(model) ? null : model }, Specification = new() { Model = wheel.Length == 0 ? null : wheel, Finish = finish.Length == 0 ? null : finish }, MissingData = wheel.Length == 0 ? "Źródło nie podaje modelu felg; szczegóły niepobrane" : "Szczegóły do pobrania" };
                foreach (var pair in new Dictionary<string,string?> { ["CarMake"] = make, ["CarModel"] = model, ["WheelModel"] = g.Specification.Model, ["Finish"] = g.Specification.Finish }) if (!string.IsNullOrWhiteSpace(pair.Value)) g.Sources.Add(new() { Field = pair.Key, Value = pair.Value, Url = IndexUrl.AbsoluteUri, Confirmed = true });
                found[url] = g;
            }
            var node = p.NextPageXPath == null ? null : doc.DocumentNode.SelectSingleNode(p.NextPageXPath); var resolved = node == null ? null : Resolve(basis, Value(node)); next = resolved == null ? null : new(resolved);
            if (next != null && page + 1 >= p.MaxPages) throw new InvalidOperationException("Osiągnięto limit paginacji. Synchronizacja nie jest kompletna.");
        }
        return found.Values.ToArray();
    }
    public async Task<Gallery> LoadAsync(string url, CancellationToken ct)
    {
        var p = LoadProfile(); var uri = new Uri(url); if (uri.Host != IndexUrl.Host) throw new InvalidOperationException("Galeria spoza witryny marki.");
        var doc = new HtmlDocument(); doc.LoadHtml(await client.GetAsync(uri, p.RequiresJavaScript, ct));
        if (Brand == WheelBrand.Vesser && doc.DocumentNode.SelectSingleNode(p.VehicleGalleryMarkerXPath!) == null) throw new InvalidOperationException("Wpis Vesser nie jest galerią pojazdu.");
        var g = Parse(doc, uri, Brand, p); g.DetailsLoaded = true;
        if (Brand == WheelBrand.Vesser && string.IsNullOrWhiteSpace(g.Vehicle.Make))
        {
            // Nazwy marek pochodzą z rzeczywistego filtra galerii, nie z domysłów co do modelu.
            var index = new HtmlDocument(); index.LoadHtml(await client.GetAsync(IndexUrl, false, ct));
            var makes = index.DocumentNode.SelectNodes(p.MakesXPath)?.Select(x => Clean(x.InnerText)).Where(x => x.Length > 0).OrderByDescending(x => x.Length).AsEnumerable() ?? [];
            var make = makes.FirstOrDefault(x => (g.Vehicle.Model ?? "").StartsWith(x + " ", StringComparison.OrdinalIgnoreCase));
            if (make != null) { g.Vehicle.Make = make; g.Vehicle.Model = g.Vehicle.Model![(make.Length + 1)..]; g.Sources.RemoveAll(x => x.Field is "CarMake" or "CarModel"); g.Sources.AddRange([new() { Field = "CarMake", Value = make, Url = IndexUrl.AbsoluteUri, Confirmed = true }, new() { Field = "CarModel", Value = g.Vehicle.Model, Url = g.Url, Confirmed = true }]); }
        }
        if (g.Specification.ProductUrl == null || p.ProductFieldsXPath.Count == 0) throw new InvalidOperationException("Brak karty modelu felg. Uzupełnij źródło przed przygotowaniem artykułu.");
        if (g.Specification.ProductUrl != null && p.ProductFieldsXPath.Count > 0)
        {
            var productUri = new Uri(g.Specification.ProductUrl); var product = new HtmlDocument(); product.LoadHtml(await client.GetAsync(productUri, false, ct));
            var productName = p.ProductNameXPath == null ? "" : Clean(product.DocumentNode.CreateNavigator().Evaluate("string(" + p.ProductNameXPath + ")")?.ToString() ?? "");
            var modelToken = g.Specification.Model?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (modelToken == null || TopicPlanner.Model(productName) != TopicPlanner.Model(modelToken)) throw new InvalidOperationException("Karta produktu nie potwierdza modelu felg z galerii.");
            g.Sources.Add(new() { Field = "ProductModel", Value = g.Specification.Model, Url = g.Specification.ProductUrl, Confirmed = true });
            foreach (var field in p.ProductFieldsXPath)
            {
                var matches = product.DocumentNode.CreateNavigator().Select(field.Value); var values = new List<string>(); while (matches.MoveNext()) values.Add(Clean(matches.Current!.Value)); var value = string.Join(", ", values.Where(x => x.Length > 0).Distinct()); if (value.Length == 0) continue;
                if (field.Key == "ProductDetails") g.Specification.ProductDetails = value;
                else if (field.Key == "AvailableSizes") g.Specification.AvailableSizes = value;
                else if (field.Key == "Certifications") g.Specification.Certifications = value;
                g.Sources.Add(new() { Field = field.Key, Value = value, Url = g.Specification.ProductUrl, Confirmed = true });
            }
        }
        g.MissingData = Missing(g); return g;
    }
    private static string Clean(string value) => System.Text.RegularExpressions.Regex.Replace(HtmlEntity.DeEntitize(value), @"\s+", " ").Trim();
    private static Uri BaseUri(HtmlDocument doc, Uri url) => Uri.TryCreate(url, doc.DocumentNode.SelectSingleNode("//base[@href]")?.GetAttributeValue("href", ""), out var basis) && basis.Host == url.Host ? basis : url;
    private static string Missing(Gallery g) => string.Join(", ", new[] { string.IsNullOrWhiteSpace(g.Vehicle.Make) ? "producent samochodu" : null, string.IsNullOrWhiteSpace(g.Vehicle.Model) ? "model samochodu" : null, g.Specification.Model == null ? "model felg" : null, g.Images.Count == 0 ? "zdjęcia" : null, g.Specification.FrontSize == null ? "rozmiar przód" : null, g.Specification.RearSize == null ? "rozmiar tył" : null }.Where(x => x != null));
    public static Gallery Parse(HtmlDocument doc, Uri url, WheelBrand brand, SourceProfile profile)
    {
        string? Field(string key) => profile.FieldsXPath.TryGetValue(key, out var xpath) ? Clean(doc.DocumentNode.CreateNavigator().Evaluate("string(" + xpath + ")")?.ToString() ?? "") is { Length: > 0 } v ? v : null : null;
        var basis = BaseUri(doc, url);
        var g = new Gallery { DetailsLoaded = true, Brand = brand, Url = Normalization.Url(url.AbsoluteUri), ExternalId = Normalization.Url(url.AbsoluteUri), Vehicle = new() { Make = Field("CarMake"), Model = Field("CarModel"), Version = Field("CarVersion") }, Specification = new() { Model = Field("WheelModel"), Finish = Field("Finish"), FrontSize = Normalization.Size(Field("FrontSize")), RearSize = Normalization.Size(Field("RearSize")), Diameter = Field("Diameter"), FrontWidth = Field("FrontWidth"), RearWidth = Field("RearWidth"), Et = Field("ET"), Pcd = Field("PCD") } };
        if (Field("CarDisplay") is { } display && !string.IsNullOrWhiteSpace(g.Vehicle.Make)) { var prefix = display.StartsWith(g.Vehicle.Make + " ", StringComparison.OrdinalIgnoreCase) ? g.Vehicle.Make : g.Vehicle.Make == "Mercedes-Benz" && display.StartsWith("Mercedes ") ? "Mercedes" : null; if (prefix != null) g.Vehicle.Model = display[(prefix.Length + 1)..]; }
        if (g.Specification.FrontSize is { } front) { var parts = front.Split('x'); if (parts.Length == 2) { g.Specification.Diameter = parts[0]; g.Specification.FrontWidth = parts[1]; } }
        if (g.Specification.RearSize is { } rear) { var parts = rear.Split('x'); if (parts.Length == 2) g.Specification.RearWidth = parts[1]; }
        if (DateTimeOffset.TryParse(Field("Published"), out var date)) g.SourcePublished = date;
        foreach (var pair in profile.FieldsXPath) if (Field(pair.Key) is { } value) g.Sources.Add(new() { Field = pair.Key == "CarModel" && Field("CarDisplay") != null ? "CarModelGroup" : pair.Key, Value = value, Url = g.Url, Confirmed = true });
        if (!g.Sources.Any(x => x.Field == "CarModel")) g.Sources.Add(new() { Field = "CarModel", Value = g.Vehicle.Model, Url = g.Url, Confirmed = true });
        foreach (var image in doc.DocumentNode.SelectNodes(profile.ImagesXPath) ?? Enumerable.Empty<HtmlNode>())
        {
            var src = Resolve(basis, Value(image)); if (src == null) continue;
            if (!g.Images.Any(x => x.Url == src)) g.Images.Add(new() { Url = src, UsageAllowed = false });
        }
        var product = profile.ProductLinkXPath == null ? null : doc.DocumentNode.SelectSingleNode(profile.ProductLinkXPath);
        g.Specification.ProductUrl = product == null ? null : Resolve(basis, Value(product));
        g.MissingData = string.Join(", ", new[] { string.IsNullOrWhiteSpace(g.Vehicle.Make) ? "producent samochodu" : null, string.IsNullOrWhiteSpace(g.Vehicle.Model) ? "model samochodu" : null, g.Specification.Model == null ? "model felg" : null, g.Images.Count == 0 ? "zdjęcia" : null, g.Specification.FrontSize == null ? "rozmiar przód" : null, g.Specification.RearSize == null ? "rozmiar tył" : null }.Where(x => x != null));
        return g;
    }
    private static string Value(HtmlNode? n) => n == null ? "" : n.GetAttributeValue("href", n.GetAttributeValue("data-src", n.GetAttributeValue("src", n.GetAttributeValue("content", n.InnerText))));
    private static string? Resolve(Uri basis, string value)
    {
        if (!Uri.TryCreate(basis, HtmlEntity.DeEntitize(value), out var u) || u.Scheme != "https" || u.Host != basis.Host) return null;
        return Normalization.Url(u.AbsoluteUri);
    }
}
public sealed class JrGalleryProvider(SiteClient c, string folder) : ProfileGalleryProvider(c, folder) { public override WheelBrand Brand => WheelBrand.JR; public override Uri IndexUrl => new("https://jr-wheels.com/vehicle-gallery"); }
public sealed class ConcaverGalleryProvider(SiteClient c, string folder) : ProfileGalleryProvider(c, folder) { public override WheelBrand Brand => WheelBrand.Concaver; public override Uri IndexUrl => new("https://concaverwheels.com/vehicle_gallery"); }
public sealed class VesserGalleryProvider(SiteClient c, string folder) : ProfileGalleryProvider(c, folder) { public override WheelBrand Brand => WheelBrand.Vesser; public override Uri IndexUrl => new("https://vesserforged.com/galleries/"); }
