using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WheelContentManager.Core;
using D = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace WheelContentManager.Infrastructure;

public sealed class ExportService
{
    public async Task<string> ExportAsync(Article article, string root, string? language = null, CancellationToken ct = default, bool includeImages = true)
    {
        var g = article.Gallery; var folder = Path.Combine(root, Normalization.SafeName(g.Brand.Name()), Normalization.SafeName(g.Vehicle.Display + "-" + g.Specification.Model) + "-" + article.Id + (includeImages ? "" : "-text"));
        Directory.CreateDirectory(folder); if (includeImages) Directory.CreateDirectory(Path.Combine(folder, "images"));
        foreach (var v in article.Versions.GroupBy(x => x.Language).Select(x => x.MaxBy(y => y.Revision)!).Where(x => language == null || x.Language == language))
        {
            var metadata = $"{g.Vehicle.Display} | {g.Brand.Name()} {g.Specification.Model} | {g.Specification.Finish}\nPrzód: {g.Specification.FrontSize ?? "nieznany"}; tył: {g.Specification.RearSize ?? "nieznany"}\nŹródło: {g.Url}";
            var text = $"{v.Title}\n\n{v.Intro}\n\n{v.Body}\n\n{metadata}";
            var stem = Path.Combine(folder, "article_" + v.Language);
            await File.WriteAllTextAsync(stem + ".txt", text, new UTF8Encoding(false), ct);
            var e = HtmlEncoder.Default;
            var photos = g.Images.Where(x => includeImages && x.UsageAllowed && x.LocalPath != null && File.Exists(x.LocalPath)).ToArray();
            var photoHtml = string.Concat(photos.Select(x => "<figure><img alt=\"" + e.Encode(g.Vehicle.Display) + "\" src=\"images/" + e.Encode(Path.GetFileName(x.LocalPath!)) + "\" style=\"max-width:100%\"></figure>"));
            var html = "<!doctype html><html lang=\"" + v.Language.ToLowerInvariant() + "\"><meta charset=\"utf-8\"><title>" + e.Encode(v.Title) + "</title><body><article><h1>" + e.Encode(v.Title) + "</h1><p>" + e.Encode(v.Intro) + "</p>" + string.Concat(v.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => "<p>" + e.Encode(p) + "</p>")) + photoHtml + "</article><footer><pre>" + e.Encode(metadata) + "</pre></footer></body></html>";
            await File.WriteAllTextAsync(stem + ".html", html, ct);
            await Task.Run(() => WriteDocument(stem + ".docx", text, photos, ct), ct);
        }
        foreach (var image in g.Images.Where(x => includeImages && x.UsageAllowed && x.LocalPath != null && File.Exists(x.LocalPath)))
            File.Copy(image.LocalPath!, Path.Combine(folder, "images", Path.GetFileName(image.LocalPath!)), true);
        await File.WriteAllTextAsync(Path.Combine(folder, "metadata.json"), JsonSerializer.Serialize(new { article.Id, article.Status, article.Created, article.PromptVersionId, Gallery = new { g.Brand, g.Vehicle.Make, g.Vehicle.Model, g.Url, g.Specification, Sources = g.Sources.Select(x => new { x.Field, x.Value, x.Url, x.Confirmed }), Images = g.Images.Select(x => new { x.Url, x.UsageAllowed }) } }, new JsonSerializerOptions { WriteIndented = true }), ct);
        return folder;
    }
    private static void WriteDocument(string file, string text, IReadOnlyList<GalleryImage> photos, CancellationToken ct)
    {
        using var doc = WordprocessingDocument.Create(file, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart(); main.Document = new(new Body());
        foreach (var line in text.Split('\n')) main.Document.Body!.Append(new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
        uint id = 1;
        foreach (var photo in photos)
        {
            ct.ThrowIfCancellationRequested(); var path = photo.LocalPath!; var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png")) continue; // WebP pozostaje w HTML i folderze images; brak konwersji stratnej.
            var part = main.AddImagePart(extension == ".png" ? ImagePartType.Png : ImagePartType.Jpeg);
            using (var stream = File.OpenRead(path)) part.FeedData(stream);
            var (width, height) = ImageSize(path); const long cx = 5486400; var cy = Math.Min(7315200, cx * height / Math.Max(1, width));
            var relation = main.GetIdOfPart(part);
            var picture = new PIC.Picture(new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = id, Name = Path.GetFileName(path) }, new PIC.NonVisualPictureDrawingProperties()), new PIC.BlipFill(new D.Blip { Embed = relation }, new D.Stretch(new D.FillRectangle())), new PIC.ShapeProperties(new D.Transform2D(new D.Offset { X = 0, Y = 0 }, new D.Extents { Cx = cx, Cy = cy }), new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));
            var inline = new DW.Inline(new DW.Extent { Cx = cx, Cy = cy }, new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 }, new DW.DocProperties { Id = id++, Name = "Zdjęcie galerii" }, new DW.NonVisualGraphicFrameDrawingProperties(new D.GraphicFrameLocks { NoChangeAspect = true }), new D.Graphic(new D.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })) { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 };
            main.Document.Body!.Append(new Paragraph(new Run(new Drawing(inline))));
        }
        main.Document.Save();
    }
    private static (long Width, long Height) ImageSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 24 && bytes[0] == 137 && bytes[1] == 80) return (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)), System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
        if (bytes.Length > 4 && bytes[0] == 255 && bytes[1] == 216)
        {
            var p = 2;
            while (p + 4 < bytes.Length)
            {
                if (bytes[p++] != 255) continue; while (p < bytes.Length && bytes[p] == 255) p++; if (p >= bytes.Length) break;
                var marker = bytes[p++]; if (marker is 0xd8 or 0xd9) continue; if (p + 2 > bytes.Length) break;
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p, 2)); if (length < 2 || p + length > bytes.Length) break;
                if (marker >= 0xc0 && marker <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc) && length >= 7) return (System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p + 5, 2)), System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p + 3, 2)));
                p += length;
            }
        }
        return (600, 400);
    }
    public static string CreateZip(IEnumerable<string> folders, string target)
    {
        if (File.Exists(target)) File.Delete(target);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var folder in folders.Distinct()) foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) zip.CreateEntryFromFile(file, Path.GetFileName(folder) + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/'));
        return target;
    }
}
