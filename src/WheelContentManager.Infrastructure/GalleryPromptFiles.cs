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
    private static string PromptUsedKey(long id) => "prompt-gallery-used-" + id;
    private static async Task<List<Gallery>> OrderPromptGalleriesAsync(ContentDb db, IEnumerable<Gallery> galleries, CancellationToken ct)
    {
        var keys = (await db.ApplicationSettings.Where(s => s.Key.StartsWith("prompt-gallery-used-")).Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var rows = galleries.ToList(); foreach (var g in rows) g.PromptUsed = keys.Contains(PromptUsedKey(g.Id));
        return rows.OrderBy(g => g.PromptUsed).ThenBy(g => g.ListingOrder).ToList();
    }
    private static async Task SavePromptUsageAsync(ContentDb db, long id, bool used, CancellationToken ct)
    {
        var key = PromptUsedKey(id); var row = await db.ApplicationSettings.FindAsync([key], ct);
        if (used && row == null) db.Add(new ApplicationSetting { Key = key, Json = JsonSerializer.Serialize(DateTimeOffset.UtcNow) });
        if (!used && row != null) db.Remove(row);
        await db.SaveChangesAsync(ct);
    }
    public async Task SetPromptGalleryUsedAsync(long id, bool used, CancellationToken ct = default)
    {
        using var operation = OperationSession.Start(paths, "Oznaczanie wykorzystania galerii", ct); ct = operation.Token;
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Galleries.AnyAsync(g => g.Id == id, ct)) throw new InvalidOperationException("Nie znaleziono galerii.");
        await SavePromptUsageAsync(db, id, used, ct);
    }
    public async Task<(List<Gallery> Galleries, DateTimeOffset? Checked)> CachedPromptGalleriesAsync(WheelBrand brand, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var stamp = await db.ApplicationSettings.FindAsync([ListingKey(brand)], ct);
        if (stamp == null) return ([], null);
        var date = JsonSerializer.Deserialize<DateTimeOffset>(stamp.Json);
        var rows = await db.FullGalleries.Where(g => g.Brand == brand && !g.BlogBlocked && g.BlogCheckedAt != null).AsNoTracking().ToListAsync(ct);
        return (await OrderPromptGalleriesAsync(db, rows, ct), date);
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
        return await OrderPromptGalleriesAsync(db, rows.Where(g => !g.BlogBlocked), ct);
    }
    public async Task<GalleryPromptPackage> ExportGalleryPromptFilesAsync(long galleryId, string output, IProgress<string>? progress, CancellationToken ct)
    {
        using var operation = OperationSession.Start(paths, "Przygotowanie pliku dla ChatGPT", ct); ct = operation.Token; progress = operation.Progress(progress);
        var topic = await BuildChatGptTopicAsync(galleryId, progress, ct, fresh: true, allowLocalArticle: true);
        progress.Report("Zapisywanie polecenia i dokumentu DOCX…");
        await using var db = await factory.CreateDbContextAsync(ct); var gallery = await db.FullGalleries.SingleAsync(g => g.Id == galleryId, ct);
        var name = Normalization.SafeName(gallery.Vehicle.Display + "-" + gallery.Specification.Model + "-" + gallery.Id);
        var folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
        var instructions = Path.Combine(folder, "Polecenie do ChatGPT.txt"); var document = Path.Combine(folder, "Wrzuc do ChatGPT.docx");
        var galleryHeader = "GALERIA ŹRÓDŁOWA" + (string.IsNullOrWhiteSpace(gallery.ExternalId) ? "" : " — numer / ID na stronie: " + gallery.ExternalId) + "\n" + gallery.Url + "\n\n";
        var task = galleryHeader + "Przeczytaj ten plik jako kompletną instrukcję zadania i od razu wykonaj ją: przygotuj najpierw artykuł EN, następnie artykuł PL. Nie pytaj, co zrobić z plikiem, i nie ograniczaj się do jego streszczenia.\n\n" + topic.Prompt;
        await File.WriteAllTextAsync(instructions, task, new UTF8Encoding(false), ct);
        using (var doc = WordprocessingDocument.Create(document, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart(); main.Document = new Document(new Body());
            var body = main.Document.Body!;
            body.Append(Link("Galeria źródłowa" + (string.IsNullOrWhiteSpace(gallery.ExternalId) ? "" : " — ID " + gallery.ExternalId), gallery.Url));
            body.Append(new Paragraph(new Run(new Text("WT - Blog Generator — kompletne zadanie dla ChatGPT (EN → PL)"))));
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
            using var photoTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            photoTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            var images = await DownloadImagesAsync(gallery, 1, photoTimeout.Token);
            if (images.Count > 0) { photo = Path.Combine(folder, "Zdjecie galerii" + Path.GetExtension(images[0])); File.Copy(images[0], photo, true); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { progress.Report("Zdjęcie pobiera się zbyt długo — dokument zachowuje klikalne linki do galerii."); }
        catch (Exception e) when (e is not OperationCanceledException) { await LogAsync("Zdjęcie", SafeError(e), ct: ct); }
        if (photo != null)
        {
            using var doc = WordprocessingDocument.Open(document, true);
            ExportService.AppendImages(doc.MainDocumentPart!, [new GalleryImage { LocalPath = photo }], ct);
            doc.MainDocumentPart!.Document!.Save();
        }
        ct.ThrowIfCancellationRequested();
        await SavePromptUsageAsync(db, galleryId, true, ct);
        return new(folder, document, instructions, photo, gallery.Url, gallery.Specification.ProductUrl ?? "", task);
    }
}
