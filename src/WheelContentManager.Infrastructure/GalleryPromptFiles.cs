using System.Text;
using System.Text.Json;
using Gallery = WheelContentManager.Core.Gallery;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using WheelContentManager.Core;
namespace WheelContentManager.Infrastructure;
public sealed partial class ContentService
{
    private static string ListingKey(WheelBrand brand) => "gallery-prompt-list-" + brand;
    public async Task<(List<Gallery> Galleries, DateTimeOffset? Checked)> CachedPromptGalleriesAsync(WheelBrand brand, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var stamp = await db.ApplicationSettings.FindAsync([ListingKey(brand)], ct);
        if (stamp == null) return ([], null);
        var date = JsonSerializer.Deserialize<DateTimeOffset>(stamp.Json);
        var rows = await db.FullGalleries.Where(g => g.Brand == brand && !g.BlogBlocked && g.BlogCheckedAt != null).AsNoTracking().ToListAsync(ct);
        return (rows.OrderBy(g => g.ListingOrder).ToList(), date);
    }
    public async Task<List<Gallery>> RefreshPromptGalleriesAsync(WheelBrand brand, IProgress<string>? progress, CancellationToken ct)
    {
        using var operation = OperationSession.Start(paths, "Sprawdzanie galerii " + brand.Name(), ct); ct = operation.Token; progress = operation.Progress(progress);
        await SyncInternalAsync(progress, ct, verifyLoadedBlogs: false, onlyBrands: new HashSet<WheelBrand> { brand }, strict: true);
        progress.Report("Sprawdzanie wszystkich podstron /blog — archiwum pobierane raz dla całej marki…");
        await blog.PrepareArchiveAsync(brand, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.FullGalleries.Where(g => g.Brand == brand).ToListAsync(ct); var checkedAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var g = rows[i];
            if (i % 50 == 0) progress.Report($"Porównywanie galerii z /blog: {i + 1}/{rows.Count}…");
            var evidence = new Gallery { Brand = g.Brand, Url = g.Url, Vehicle = g.Vehicle, Specification = g.Specification, Images = g.Images.ToList() };
            if (!string.IsNullOrWhiteSpace(g.ThumbnailUrl)) evidence.Images.Add(new() { Url = g.ThumbnailUrl });
            var match = await blog.CheckAsync(evidence, ct);
            g.BlogBlocked = match.Published || match.PossibleDuplicate; g.BlogStatus = match.Message; g.ExistingBlogUrl = match.Url; g.BlogCheckedAt = checkedAt;
        }
        var key = ListingKey(brand); var stamp = await db.ApplicationSettings.FindAsync([key], ct);
        if (stamp == null) { stamp = new() { Key = key }; db.Add(stamp); }
        stamp.Json = JsonSerializer.Serialize(checkedAt); await db.SaveChangesAsync(ct);
        return rows.Where(g => !g.BlogBlocked).OrderBy(g => g.ListingOrder).ToList();
    }
    public async Task<GalleryPromptPackage> ExportGalleryPromptFilesAsync(long galleryId, string output, IProgress<string>? progress, CancellationToken ct)
    {
        using var operation = OperationSession.Start(paths, "Przygotowanie pliku dla ChatGPT", ct); ct = operation.Token; progress = operation.Progress(progress);
        var topic = await BuildChatGptTopicAsync(galleryId, progress, ct, fresh: true, allowLocalArticle: true);
        await using var db = await factory.CreateDbContextAsync(ct); var gallery = await db.FullGalleries.SingleAsync(g => g.Id == galleryId, ct);
        var name = Normalization.SafeName(gallery.Vehicle.Display + "-" + gallery.Specification.Model + "-" + gallery.Id);
        var folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
        var instructions = Path.Combine(folder, "Polecenie do ChatGPT.txt"); var document = Path.Combine(folder, "Wrzuc do ChatGPT.docx");
        var task = "Przeczytaj ten plik jako kompletną instrukcję zadania i od razu wykonaj ją: przygotuj oba artykuły PL i EN. Nie pytaj, co zrobić z plikiem, i nie ograniczaj się do jego streszczenia.\n\n" + topic.Prompt;
        await File.WriteAllTextAsync(instructions, task, new UTF8Encoding(false), ct);
        using (var doc = WordprocessingDocument.Create(document, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart(); main.Document = new Document(new Body());
            var body = main.Document.Body!; body.Append(new Paragraph(new Run(new Text("WT - Blog Generator — kompletne zadanie dla ChatGPT"))));
            body.Append(Link("Galeria zdjęć", gallery.Url));
            if (!string.IsNullOrWhiteSpace(gallery.Specification.ProductUrl)) body.Append(Link("Oficjalna karta modelu felg", gallery.Specification.ProductUrl));
            foreach (var line in task.Split('\n')) { ct.ThrowIfCancellationRequested(); body.Append(new Paragraph(new Run(new Text(line.TrimEnd('\r')) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }))); }
            foreach (var image in gallery.Images.Take(4)) body.Append(Link("Zdjęcie galerii", image.Url));
            main.Document.Save();
            Paragraph Link(string label, string url)
            {
                var rel = main.AddHyperlinkRelationship(new Uri(url), true);
                return new Paragraph(new Hyperlink(new Run(new RunProperties(new Color { Val = "2563EB" }, new Underline { Val = UnderlineValues.Single }), new Text(label + ": " + url))) { Id = rel.Id });
            }
        }
        string? photo = null;
        try
        {
            progress.Report("Pobieranie zdjęcia do folderu z plikiem…");
            var images = await DownloadImagesAsync(gallery, 1, ct);
            if (images.Count > 0) { photo = Path.Combine(folder, "Zdjecie galerii" + Path.GetExtension(images[0])); File.Copy(images[0], photo, true); }
        }
        catch (Exception e) when (e is not OperationCanceledException) { await LogAsync("Zdjęcie", SafeError(e), ct: ct); }
        if (photo != null)
        {
            using var doc = WordprocessingDocument.Open(document, true);
            ExportService.AppendImages(doc.MainDocumentPart!, [new GalleryImage { LocalPath = photo }], ct);
            doc.MainDocumentPart!.Document!.Save();
        }
        return new(folder, document, instructions, photo, gallery.Url, gallery.Specification.ProductUrl ?? "", task);
    }
}
