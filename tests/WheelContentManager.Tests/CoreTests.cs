using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using WheelContentManager.Core;
using WheelContentManager.GalleryProviders;
using WheelContentManager.Infrastructure;
using Xunit;
using Gallery = WheelContentManager.Core.Gallery;

namespace WheelContentManager.Tests;
public class CoreTests
{
    [Theory][InlineData("20x8.5", "20x8.5")][InlineData("20x8,5", "20x8.5")][InlineData("20 × 10,0", "20x10")][InlineData(null, null)][InlineData("", null)]
    public void NormalizesWheelSizes(string? input, string? expected) => Assert.Equal(expected, Normalization.Size(input));
    [Fact] public void UrlsDeduplicateFragmentsAndTrailingSlash() => Assert.Equal(Normalization.Url("https://EXAMPLE.com/album/"), Normalization.Url("https://example.com/album#photo"));
    [Fact] public void SafeNamesCannotEscapeFolder() { var s = Normalization.SafeName("../../BMW:M4?*"); Assert.DoesNotContain('/', s); Assert.DoesNotContain(':', s); Assert.False(s.StartsWith('.')); }
    [Fact] public void UsesSpecifiedAxlesInsteadOfWidthOrder()
    {
        var g = Fixtures.Gallery(); g.Specification.FrontSize = "20x11"; g.Specification.RearSize = "20x9";
        Assert.Equal("20x11 / 20x9", PromptRenderer.Render("{FRONT_SIZE} / {REAR_SIZE}", g));
    }
    [Fact] public void PromptReplacesAllSupportedVariablesAndFlagsUnknown()
    {
        var g = Fixtures.Gallery(); var rendered = PromptRenderer.Render(PromptService.Baseline, g); Assert.DoesNotContain("{CAR_", rendered);
        Assert.Throws<InvalidOperationException>(() => PromptRenderer.Render("{UNKNOWN}", g)); Assert.Contains("Niepotwierdzone", PromptRenderer.Render("{VERIFIED_CERTIFICATIONS}", g));
    }
    [Fact] public void IntroCountsUnicodeCharactersRatherThanUtf16Units()
    {
        var g = Fixtures.Gallery(); var a = Fixtures.ArticleData(g) with { Intro = string.Concat(Enumerable.Repeat("🚗", 200)) };
        Assert.DoesNotContain(ArticleValidator.Validate(a, g, Fixtures.Settings(), "PL"), x => x.Contains("Intro"));
        Assert.Contains(ArticleValidator.Validate(a with { Intro = a.Intro + "a" }, g, Fixtures.Settings(), "PL"), x => x.Contains("Intro"));
    }
    [Theory][InlineData("# nagłówek")][InlineData("- punkt")][InlineData("1. punkt")][InlineData("**Nagłówek**")][InlineData("<h2>Nagłówek</h2>")]
    public void RejectsHeadingsAndLists(string forbidden)
    {
        var g = Fixtures.Gallery(); var a = Fixtures.ArticleData(g); Assert.Contains(ArticleValidator.Validate(a with { Body = forbidden + "\n" + a.Body }, g, Fixtures.Settings(), "PL"), x => x.Contains("nagłówki"));
    }
    [Fact] public void RejectsUnverifiedCertificationAndSizes()
    {
        var g = Fixtures.Gallery(); var a = Fixtures.ArticleData(g); var errors = ArticleValidator.Validate(a with { Body = a.Body + " TÜV 22x12" }, g, Fixtures.Settings(), "PL"); Assert.Contains(errors, x => x.Contains("certyfikacja")); Assert.Contains(errors, x => x.Contains("22x12"));
    }
    [Fact] public void WrongLanguageShortBodyAndUnknownSourcesFail()
    {
        var g = Fixtures.Gallery(); var a = Fixtures.ArticleData(g) with { Language = "EN", Body = "krótki", Sources = ["https://fake.invalid"] };
        var errors = ArticleValidator.Validate(a, g, Fixtures.Settings(), "PL"); Assert.Contains(errors, x => x.Contains("języka")); Assert.Contains(errors, x => x.Contains("Długość")); Assert.Contains(errors, x => x.Contains("źródło"));
    }
    [Fact] public void DetectsReusedArticles() { var text = string.Join(' ', Enumerable.Range(0, 50).Select(x => "słowo" + x)); Assert.Equal(1, ArticleValidator.Similarity(text, text)); Assert.Equal(0, ArticleValidator.Similarity("alfa beta gamma delta epsilon", "jeden dwa trzy cztery pięć")); }
    [Fact] public void ScheduleSelectsNextWeekAtExactBoundary()
    {
        var s = Fixtures.Settings(); var monday = new DateTime(2026, 10, 12, 8, 0, 0); Assert.Equal(monday.AddDays(7), Normalization.NextRun(s, monday)); Assert.Equal(monday, Normalization.NextRun(s, monday.AddMinutes(-1)));
    }
    [Fact] public void SchedulerXmlEscapesPathsAndUsesSafeWindowsSettings()
    {
        var s = Fixtures.Settings(); var xml = XDocument.Parse(WindowsScheduler.TaskXml(s, @"C:\Wheel & Manager\Worker.exe", @"PC\User")); XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal("IgnoreNew", xml.Descendants(ns + "MultipleInstancesPolicy").Single().Value); Assert.Equal("true", xml.Descendants(ns + "StartWhenAvailable").Single().Value); Assert.Equal("2", xml.Descendants(ns + "RestartOnFailure").Single().Element(ns + "Count")!.Value); Assert.Equal("InteractiveToken", xml.Descendants(ns + "LogonType").Single().Value); Assert.Equal(@"C:\Wheel & Manager\Worker.exe", xml.Descendants(ns + "Command").Single().Value);
    }
    [Fact] public void FileLockRejectsConcurrentExecutionAndReleasesAfterDispose()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lock"); using (OperationLock.Acquire(path)) Assert.Throws<InvalidOperationException>(() => OperationLock.Acquire(path)); using (OperationLock.Acquire(path)) { } File.Delete(path);
    }
    [Fact] public void RobotsRulesAreRespected() { Assert.False(SiteClient.RobotsAllowed("User-agent: *\nDisallow: /private\nAllow: /private/public", "/private/a")); Assert.True(SiteClient.RobotsAllowed("User-agent: *\nDisallow: /private\nAllow: /private/public", "/private/public/a")); }
    [Fact] public void ParserEngineUsesOnlyExplicitFixtureProfile()
    {
        var doc = new HtmlDocument(); doc.LoadHtml(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gallery.html")));
        var profile = new SourceProfile { FieldsXPath = new() { ["CarMake"] = "//span[@id='make']", ["CarModel"] = "//span[@id='car']", ["WheelModel"] = "//span[@id='wheel']", ["FrontSize"] = "//span[@id='front']", ["RearSize"] = "//span[@id='rear']" }, ImagesXPath = "//a[@class='photo']" };
        var gallery = ProfileGalleryProvider.Parse(doc, new("https://fixture.example/gallery/1"), WheelBrand.JR, profile);
        Assert.Equal("BMW", gallery.Vehicle.Make); Assert.Equal("20x11", gallery.Specification.FrontSize); Assert.Equal("20x9", gallery.Specification.RearSize); Assert.Single(gallery.Images); Assert.All(gallery.Sources, s => Assert.True(s.Confirmed)); Assert.False(gallery.Images[0].UsageAllowed);
    }
    [Fact] public async Task MissingVerifiedSourceProfileFailsWithoutInventingVehicles()
    {
        var provider = new JrGalleryProvider(new SiteClient(new HttpClient()), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())); await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DiscoverAsync(default));
    }
    [Fact] public void ImportsFullDocumentSectionsWithoutTruncation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document)) { var m = doc.AddMainDocumentPart(); m.Document = new(new Body()); foreach (var text in new[] { "JR", "pełne wymagania JR", "CVR", "pełne wymagania CVR", "VSR", "pełne wymagania VSR" }) m.Document.Body!.Append(new Paragraph(new Run(new Text(text)))); m.Document.Save(); }
        var result = PromptService.ReadDocument(path); Assert.Equal(3, result.Count); Assert.Equal("pełne wymagania CVR", result[WheelBrand.Concaver]); File.Delete(path);
    }
    [Fact] public async Task ExportProducesReadableDocxHtmlTextAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var g = Fixtures.Gallery(); var data = Fixtures.ArticleData(g);
        var a = new Article { Id = 1, Gallery = g, Versions = [new() { Language = "PL", Title = data.Title, Intro = data.Intro, Body = data.Body }] };
        var folder = await new ExportService().ExportAsync(a, root);
        using (var doc = WordprocessingDocument.Open(Path.Combine(folder, "article_PL.docx"), false)) Assert.Contains(data.Title, doc.MainDocumentPart!.Document!.InnerText);
        Assert.Contains("lang=\"pl\"", await File.ReadAllTextAsync(Path.Combine(folder, "article_PL.html"))); Assert.Contains(g.Url, await File.ReadAllTextAsync(Path.Combine(folder, "article_PL.txt"))); using (JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "metadata.json")))) { }
        var zip = ExportService.CreateZip([folder], Path.Combine(root, "export.zip")); using (var archive = System.IO.Compression.ZipFile.OpenRead(zip)) Assert.Contains(archive.Entries, x => x.Name == "article_PL.docx"); Directory.Delete(root, true);
    }
    [Fact] public async Task ExportEmbedsPermittedPhotosAndProducesValidOpenXml()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root); var image = Path.Combine(root, "photo.png");
        await File.WriteAllBytesAsync(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/u94AAAAASUVORK5CYII="));
        var g = Fixtures.Gallery(); g.Images[0].LocalPath = image; g.Images[0].UsageAllowed = true;
        var article = new Article { Id = 9, Gallery = g, Versions = [new() { Title = "Tytuł", Intro = "Intro", Body = "Tekst", Language = "PL" }] };
        var folder = await new ExportService().ExportAsync(article, root);
        using (var doc = WordprocessingDocument.Open(Path.Combine(folder, "article_PL.docx"), false)) { Assert.Single(doc.MainDocumentPart!.ImageParts); Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc)); }
        Assert.True(File.Exists(Path.Combine(folder, "images", "photo.png"))); Assert.Contains("images/photo.png", await File.ReadAllTextAsync(Path.Combine(folder, "article_PL.html"))); Directory.Delete(root, true);
    }
    [Fact] public void NotificationRequiresAllThreeBrandsAndBothLanguages()
    {
        var articles = Enum.GetValues<WheelBrand>().Select(b => new Article { Gallery = Fixtures.Gallery(b), Status = ArticleStatus.Ready, Versions = [new() { Language = "PL" }, new() { Language = "EN" }] }).ToList();
        Assert.True(NotificationService.IsFullSuccess(articles, 1)); articles[0].Status = ArticleStatus.NeedsCorrection; Assert.False(NotificationService.IsFullSuccess(articles, 1)); articles[0].Status = ArticleStatus.Ready; articles[1].Versions.RemoveAt(1); Assert.False(NotificationService.IsFullSuccess(articles, 1));
    }
}
internal static class Fixtures
{
    public static Gallery Gallery(WheelBrand brand = WheelBrand.JR) => new() { DetailsLoaded = true, Brand = brand, ExternalId = "https://fixture.example/" + brand, Url = "https://fixture.example/" + brand, Vehicle = new() { Make = "BMW", Model = "M4" }, Specification = new() { Model = brand switch { WheelBrand.JR => "JR52", WheelBrand.Concaver => "Concaver CVR5", _ => "Vesser VF1" }, FrontSize = "20x11", RearSize = "20x9" }, Images = [new() { Url = "https://fixture.example/car.jpg" }], Sources = [new() { Field = "CarModel", Value = "M4", Url = "https://fixture.example", Confirmed = true }, new() { Field = "WheelModel", Value = brand switch { WheelBrand.JR => "JR52", WheelBrand.Concaver => "Concaver CVR5", _ => "Vesser VF1" }, Url = "https://fixture.example", Confirmed = true }, new() { Field = "FrontSize", Value = "20x11", Url = "https://fixture.example", Confirmed = true }, new() { Field = "RearSize", Value = "20x9", Url = "https://fixture.example", Confirmed = true }] };
    public static AppSettings Settings() => new() { MinWords = 100, MaxWords = 300, AiModel = "mock-vision", InputPricePerMillion = .01m, OutputPricePerMillion = .01m };
    public static AiArticle ArticleData(Gallery g, string language = "PL") => new($"{g.Brand.Name()} {g.Specification.Model} {g.Vehicle.Display}", "Konfiguracja testowa — dane syntetyczne do testów.", g.Vehicle.Display + " " + g.Specification.Model + " " + string.Join(' ', Enumerable.Range(0, 140).Select(i => (g.Brand == WheelBrand.JR ? "jasny" : g.Brand == WheelBrand.Concaver ? "ciemny" : "srebrny") + i)), language, [g.Url], []);
}
