using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WheelContentManager.Core;

public enum WheelBrand { JR, Concaver, Vesser }
public enum ArticleStatus { Generating, NeedsCorrection, Ready, Approved, Published }
public static class BrandNames
{
    public static string Name(this WheelBrand brand) => brand switch { WheelBrand.JR => "JR Wheels", WheelBrand.Concaver => "Concaver Wheels", _ => "Vesser Forged" };
    public static string Label(this ArticleStatus s) => s switch { ArticleStatus.Generating => "Generowanie", ArticleStatus.NeedsCorrection => "Wymaga poprawy", ArticleStatus.Ready => "Gotowy do sprawdzenia", ArticleStatus.Approved => "Zatwierdzony", _ => "Opublikowany" };
}
public record Fact(string? Value, string SourceUrl, bool Confirmed);
public class Brand { public int Id { get; set; } public WheelBrand Code { get; set; } public string Name { get; set; } = ""; }
public class Vehicle { public long Id { get; set; } public string? Make { get; set; } public string? Model { get; set; } public string? Version { get; set; } public string Display => $"{Make ?? "Producent nieznany"} {Model ?? "Model nieznany"}".Trim(); }
public class Gallery
{
    public DateTimeOffset? BlogCheckedAt { get; set; } public string BlogStatus { get; set; } = "Jeszcze nie sprawdzono /blog"; public string? ExistingBlogUrl { get; set; } public bool BlogBlocked { get; set; }
    public bool DetailsLoaded { get; set; } public int ListingOrder { get; set; } public string? ThumbnailUrl { get; set; }
    public long Id { get; set; }
    public WheelBrand Brand { get; set; }
    public string ExternalId { get; set; } = "";
    public string Url { get; set; } = "";
    public Vehicle Vehicle { get; set; } = new();
    public WheelSpecification Specification { get; set; } = new();
    public List<GalleryImage> Images { get; set; } = [];
    public List<SourceReference> Sources { get; set; } = [];
    public DateTimeOffset FirstDetected { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SourcePublished { get; set; }
    public bool Used { get; set; }
    public string MissingData { get; set; } = "";
    public string? VisualAnalysis { get; set; }
    public string Display => $"{Brand.Name()} · {Vehicle.Display} · {Specification.Model ?? "model felg nieznany"}";
    public bool Complete => !string.IsNullOrWhiteSpace(Vehicle.Make) && !string.IsNullOrWhiteSpace(Vehicle.Model) && !string.IsNullOrWhiteSpace(Specification.Model) && Images.Count > 0;
}
public class GalleryImage { public long Id { get; set; } public long GalleryId { get; set; } public string Url { get; set; } = ""; public string? LocalPath { get; set; } public bool UsageAllowed { get; set; } }
public class WheelSpecification
{
    public long Id { get; set; }
    public string? Model { get; set; }
    public string? Finish { get; set; }
    public string? FrontSize { get; set; }
    public string? RearSize { get; set; }
    public string? Diameter { get; set; }
    public string? FrontWidth { get; set; }
    public string? RearWidth { get; set; }
    public string? Et { get; set; }
    public string? Pcd { get; set; }
    public string? ProductUrl { get; set; }
    public string? AvailableSizes { get; set; }
    public string? Certifications { get; set; }
    public string? ProductDetails { get; set; }
    public string FactsJson { get; set; } = "{}";
}
public class SourceReference { public bool Manual { get; set; } public long Id { get; set; } public long GalleryId { get; set; } public string Field { get; set; } = ""; public string? Value { get; set; } public string Url { get; set; } = ""; public bool Confirmed { get; set; } }
public class Article
{
    public long Id { get; set; } public long GalleryId { get; set; } public Gallery Gallery { get; set; } = null!;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; } public DateTimeOffset? PublishedAt { get; set; }
    public ArticleStatus Status { get; set; } = ArticleStatus.Generating;
    public long PromptVersionId { get; set; } public string Warnings { get; set; } = "";
    public string? ExportFolder { get; set; } public string? AutomationRunId { get; set; }
    public List<ArticleVersion> Versions { get; set; } = [];
    public string Display => $"{Gallery.Display} · {Status.Label()}";
}
public class ArticleVersion
{
    public long Id { get; set; } public long ArticleId { get; set; } public string Language { get; set; } = "PL";
    public int Revision { get; set; } = 1; public string Title { get; set; } = ""; public string Intro { get; set; } = ""; public string Body { get; set; } = "";
    public string SourcesJson { get; set; } = "[]"; public string WarningsJson { get; set; } = "[]";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow; public long PromptVersionId { get; set; }
    public int InputTokens { get; set; } public int OutputTokens { get; set; }
}
public class PromptTemplate { public long Id { get; set; } public WheelBrand Brand { get; set; } public List<PromptVersion> Versions { get; set; } = []; }
public class PromptVersion { public long Id { get; set; } public long PromptTemplateId { get; set; } public int Revision { get; set; } public string Content { get; set; } = ""; public string Origin { get; set; } = ""; public bool EditorialDocumentVerified { get; set; } public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow; }
public class GenerationJob { public int InputTokens { get; set; } public int OutputTokens { get; set; } public long Id { get; set; } public long GalleryId { get; set; } public string? RunId { get; set; } public string Step { get; set; } = ""; public string Status { get; set; } = "W toku"; public string? Error { get; set; } public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow; }
public class AutomationRun { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string PeriodKey { get; set; } = ""; public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset? Finished { get; set; } public string Status { get; set; } = "W toku"; public string? Report { get; set; } }
public class NotificationHistory { public long Id { get; set; } public string RunId { get; set; } = ""; public string State { get; set; } = "Pending"; public string MessageId { get; set; } = ""; public DateTimeOffset? SentAt { get; set; } }
public class ApplicationSetting { public string Key { get; set; } = ""; public string Json { get; set; } = "{}"; }
public class ErrorLog { public long Id { get; set; } public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow; public string Level { get; set; } = "Informacja"; public string Operation { get; set; } = ""; public string Message { get; set; } = ""; }
public class AppSettings
{
    public string AiProvider { get; set; } = "OpenAI"; public string AiModel { get; set; } = "";
    public const int ReferenceMinWords = 2200, ReferenceMaxWords = 2600;
    public int EditorialPolicyVersion { get; set; } = 1;
    public int MinWords { get; set; } = ReferenceMinWords; public int MaxWords { get; set; } = ReferenceMaxWords;
    public bool ExportImages { get; set; }
    public int MaxImages { get; set; } = 4; public int MaxRetries { get; set; } = 2;
    public int MaxOutputTokens { get; set; } = 12000; public int MaxTokensPerCycle { get; set; } = 120000;
    public decimal MaxCycleCost { get; set; } = 10m; public decimal InputPricePerMillion { get; set; } = 0; public decimal OutputPricePerMillion { get; set; } = 0;
    public string SmtpHost { get; set; } = ""; public int SmtpPort { get; set; } = 587; public string SmtpSecurity { get; set; } = "StartTls";
    public string SmtpUser { get; set; } = ""; public string MailFrom { get; set; } = ""; public string MailTo { get; set; } = "";
    public bool AttachZip { get; set; } = true; public string ExportFolder { get; set; } = "";
    public bool ScheduleEnabled { get; set; } public DayOfWeek ScheduleDay { get; set; } = DayOfWeek.Monday; public int ScheduleHour { get; set; } = 8; public int ScheduleMinute { get; set; }
    public int ArticlesPerBrand { get; set; } = 1; public string SelectionMode { get; set; } = "Najnowsze niewykorzystane";
    public long? ManualJrGalleryId { get; set; } public long? ManualConcaverGalleryId { get; set; } public long? ManualVesserGalleryId { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool BackgroundPreparationEnabled { get; set; } = true;
    public bool SetupCompleted { get; set; }
    public void Validate()
    {
        if (MinWords < 100 || MaxWords < MinWords || MaxWords > 10000) throw new InvalidOperationException("Nieprawidłowy zakres długości artykułu (100–10000 słów).");
        if (MaxImages is < 1 or > 10 || MaxRetries is < 0 or > 3 || ArticlesPerBrand is < 1 or > 5) throw new InvalidOperationException("Nieprawidłowe limity zdjęć, prób lub artykułów.");
        if (ScheduleHour is < 0 or > 23 || ScheduleMinute is < 0 or > 59 || SmtpPort is < 1 or > 65535) throw new InvalidOperationException("Nieprawidłowa godzina lub port SMTP.");
        if (MaxOutputTokens is < 1024 or > 32000 || MaxTokensPerCycle < MaxOutputTokens || MaxCycleCost <= 0 || InputPricePerMillion < 0 || OutputPricePerMillion < 0) throw new InvalidOperationException("Nieprawidłowe limity kosztów lub tokenów.");
        if (SelectionMode is not ("Najnowsze niewykorzystane" or "Ręczny")) throw new InvalidOperationException("Nieprawidłowy tryb wyboru galerii.");
        if (SelectionMode == "Ręczny" && ArticlesPerBrand != 1) throw new InvalidOperationException("Tryb ręczny cyklu wybiera jedną galerię na markę. Dla wielu artykułów wybierz tryb automatyczny.");
        if (AiProvider is not ("OpenAI" or "Anthropic")) throw new InvalidOperationException("Wybierz OpenAI lub Anthropic.");
        if (SmtpSecurity is not ("StartTls" or "SslOnConnect")) throw new InvalidOperationException("SMTP wymaga STARTTLS albo SSL/TLS.");
    }
}
public record AiResult(string Text, int InputTokens = 0, int OutputTokens = 0);
public record AiArticle(string Title, string Intro, string Body, string Language, string[] Sources, string[] Warnings);
public interface IAiProvider
{
    string Name { get; }
    Task<IReadOnlyList<string>> ModelsAsync(string key, CancellationToken ct);
    Task<AiResult> CompleteAsync(string key, string model, string instruction, string data, IReadOnlyList<string> imagePaths, int maxTokens, CancellationToken ct);
}
public interface IGalleryProvider
{
    WheelBrand Brand { get; } Uri IndexUrl { get; }
    Task<IReadOnlyList<Gallery>> DiscoverAsync(CancellationToken ct);
    Task<Gallery> LoadAsync(string url, CancellationToken ct) => throw new NotSupportedException("Dostawca nie implementuje szczegółów galerii.");
}
public static partial class Normalization
{
    public static string Url(string url) { var b = new UriBuilder(url) { Fragment = "" }; b.Host = b.Host.ToLowerInvariant(); b.Path = b.Path.TrimEnd('/'); return b.Uri.AbsoluteUri.TrimEnd('/'); }
    public static string? Size(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value.Replace(',', '.'), @"(?<d>\d{2})\s*[x×X]\s*(?<w>\d{1,2}(?:\.\d+)?)");
        if (!match.Success) return value.Trim();
        return $"{decimal.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture):0}x{decimal.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.InvariantCulture)}";
    }
    public static string SafeName(string value) => Regex.Replace(value, @"[^\p{L}\p{N}._-]+", "-").Trim('.', '-')[..Math.Min(100, Regex.Replace(value, @"[^\p{L}\p{N}._-]+", "-").Trim('.', '-').Length)];
    public static int Words(string value) => Regex.Matches(value, @"\p{L}[\p{L}\p{N}'’\-]*").Count;
    public static DateTime NextRun(AppSettings s, DateTime now)
    {
        var candidate = now.Date.AddHours(s.ScheduleHour).AddMinutes(s.ScheduleMinute);
        candidate = candidate.AddDays(((int)s.ScheduleDay - (int)candidate.DayOfWeek + 7) % 7);
        return candidate <= now ? candidate.AddDays(7) : candidate;
    }
}
public static class PromptRenderer
{
    public static string Render(string template, Gallery g)
    {
        var s = g.Specification;
        var data = new Dictionary<string, string?> { ["CAR_MAKE"] = g.Vehicle.Make, ["CAR_MODEL"] = g.Vehicle.Model, ["CAR_VERSION"] = g.Vehicle.Version, ["WHEEL_BRAND"] = g.Brand.Name(), ["WHEEL_MODEL"] = s.Model, ["WHEEL_FINISH"] = s.Finish, ["FRONT_SIZE"] = s.FrontSize, ["REAR_SIZE"] = s.RearSize, ["AVAILABLE_SIZES"] = s.AvailableSizes, ["PRODUCT_URL"] = s.ProductUrl, ["GALLERY_URL"] = g.Url, ["VERIFIED_CERTIFICATIONS"] = s.Certifications, ["PHOTO_ANALYSIS"] = g.VisualAnalysis, ["VERIFIED_PRODUCT_DETAILS"] = s.ProductDetails };
        foreach (var pair in data) template = template.Replace("{" + pair.Key + "}", pair.Value ?? "Niepotwierdzone — nie używaj jako faktu");
        if (Regex.IsMatch(template, @"\{[A-Z_]+\}")) throw new InvalidOperationException("Prompt zawiera nierozpoznane zmienne.");
        return template;
    }
}
public static class AiResponseParser
{
    public static AiArticle Parse(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        foreach (var field in new[] { "title", "intro", "body", "language" }) if (!root.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String) throw new JsonException("Brak pola tekstowego odpowiedzi AI.");
        foreach (var field in new[] { "sources", "warnings" }) if (!root.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) throw new JsonException("Brak prawidłowej listy w odpowiedzi AI.");
        return new(root.GetProperty("title").GetString()!, root.GetProperty("intro").GetString()!, root.GetProperty("body").GetString()!, root.GetProperty("language").GetString()!, root.GetProperty("sources").EnumerateArray().Select(x => x.GetString()!).ToArray(), root.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }
}
public static class ArticleValidator
{
    public static List<string> Validate(AiArticle a, Gallery g, AppSettings s, string language)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(a.Title)) errors.Add("Brak tytułu.");
        if (string.IsNullOrWhiteSpace(a.Intro) || a.Intro.EnumerateRunes().Count() > 200) errors.Add("Intro musi mieć 1–200 znaków.");
        if (a.Language != language) errors.Add("Nieprawidłowe oznaczenie języka.");
        var count = Normalization.Words(a.Body ?? "");
        if (count < s.MinWords || count > s.MaxWords) errors.Add($"Długość {count} słów poza zakresem {s.MinWords}–{s.MaxWords}.");
        var title = a.Title ?? "";
        var text = title + " " + a.Intro + " " + a.Body;
        if (string.IsNullOrWhiteSpace(g.Vehicle.Model) || !text.Contains(g.Vehicle.Model, StringComparison.OrdinalIgnoreCase) || !text.Contains(g.Specification.Model ?? "[brak]", StringComparison.OrdinalIgnoreCase)) errors.Add("Brak prawidłowej nazwy samochodu lub felg.");
        if (g.Brand == WheelBrand.Concaver && !text.Contains("Concaver " + g.Specification.Model?.Replace("Concaver ", ""), StringComparison.OrdinalIgnoreCase)) errors.Add("Użyj pełnej nazwy Concaver z modelem.");
        if (g.Brand == WheelBrand.Vesser && (!title.Contains("Vesser", StringComparison.OrdinalIgnoreCase) || !title.Contains(g.Vehicle.Model ?? "[brak modelu]", StringComparison.OrdinalIgnoreCase) || !title.Contains(g.Specification.Model ?? "[brak]", StringComparison.OrdinalIgnoreCase))) errors.Add("Tytuł Vesser musi zawierać markę, model felg i samochodu.");
        if (Regex.IsMatch(a.Body ?? "", @"(?m)^\s*(#{1,6}\s|[-*•]\s|\d+[.)]\s|<h[1-6]|<li)|\*\*[^*]+\*\*")) errors.Add("Treść zawiera nagłówki lub listy.");
        if (Regex.IsMatch(text, @"(?i)\b(SEO|keywords|słowa kluczowe|dealer|as an AI|jako model AI|buy now|kup teraz)\b")) errors.Add("Treść zawiera zabronione sformułowania.");
        if (!g.Sources.Any(x => x.Field == "Certifications" && x.Confirmed && !string.IsNullOrWhiteSpace(x.Value)) && Regex.IsMatch(text, @"(?i)TÜV|TUV|homologac|certified|certyfikat")) errors.Add("Niepotwierdzona certyfikacja.");
        var verified = g.Sources.Where(x => x.Confirmed).Select(x => x.Value).Where(x => x != null).Select(x => Normalization.Size(x));
        foreach (Match m in Regex.Matches(text, @"\d{2}\s*[x×]\s*\d{1,2}(?:[.,]\d+)?", RegexOptions.IgnoreCase))
            if (!verified.Contains(Normalization.Size(m.Value))) errors.Add($"Niepotwierdzony rozmiar {m.Value}.");
        foreach (var source in a.Sources ?? []) if (!g.Sources.Select(x => x.Url).Append(g.Url).Append(g.Specification.ProductUrl).Contains(source)) errors.Add("Nieznane źródło odpowiedzi AI.");
        return errors.Distinct().ToList();
    }
    public static double Similarity(string first, string second)
    {
        static HashSet<string> Shingles(string v) { var words = Regex.Matches(v.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).ToArray(); return Enumerable.Range(0, Math.Max(0, words.Length - 4)).Select(i => string.Join(' ', words.Skip(i).Take(5))).ToHashSet(); }
        var a = Shingles(first); var b = Shingles(second); return a.Count == 0 || b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / Math.Min(a.Count, b.Count);
    }
}

public sealed record BlogCheck(bool Published, bool PossibleDuplicate, string? Url, string Message);
public record RecentBlogPost(string Url, string Title, DateTimeOffset? Published, IReadOnlyList<string> WheelModels);
public interface IBlogPublicationChecker
{
    Task<BlogCheck> CheckAsync(Gallery gallery, CancellationToken ct);
    Task<IReadOnlyList<RecentBlogPost>> RecentAsync(WheelBrand brand, CancellationToken ct);
}
public record TopicSuggestion(Gallery Gallery, string Reason, string? LatestPostUrl);
