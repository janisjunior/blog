using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WheelContentManager.Core;

namespace WheelContentManager.Infrastructure;

public sealed class SettingsService(IDbContextFactory<ContentDb> factory, AppPaths paths)
{
    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var value = await db.ApplicationSettings.FindAsync(["settings"], ct);
        var s = value == null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(value.Json)!;
        if (string.IsNullOrWhiteSpace(s.ExportFolder)) s.ExportFolder = paths.Exports;
        return s;
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        settings.Validate(); Directory.CreateDirectory(settings.ExportFolder);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ApplicationSettings.FindAsync(["settings"], ct);
        if (row == null) { row = new() { Key = "settings" }; db.Add(row); }
        row.Json = JsonSerializer.Serialize(settings); await db.SaveChangesAsync(ct);
    }
}
public interface ISecretStore { string? Read(string name); void Save(string name, string value); }
public sealed class DpapiSecretStore(AppPaths paths) : ISecretStore
{
    private string FileName(string name) => Path.Combine(paths.Root, "Secrets", Normalization.SafeName(name) + ".bin");
    public string? Read(string name)
    {
        var file = FileName(name); if (!File.Exists(file)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Odczyt sekretów DPAPI wymaga Windows i tego samego konta użytkownika.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser));
    }
    public void Save(string name, string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Bezpieczny zapis kluczy wymaga Windows.");
        var file = FileName(name);
        if (string.IsNullOrWhiteSpace(value)) return; // puste pole zachowuje poprzedni sekret
        var tmp = file + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); File.Move(tmp, file, true);
    }
}
public sealed class OperationLock : IDisposable
{
    private readonly FileStream stream;
    private OperationLock(FileStream stream) => this.stream = stream;
    public static OperationLock Acquire(string path)
    {
        try { return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { throw new InvalidOperationException("Inny proces wykonuje operację. Poczekaj na zakończenie lub sprawdź historię zadań."); }
    }
    public void Dispose() => stream.Dispose();
}
public sealed class PromptService(IDbContextFactory<ContentDb> factory)
{
    public const string EditorialDocumentSha256 = "7c5a625e388ad16842d244c07be70f43fd8c2a5b9fed339f1549acbdce305d89";
    public const string DefaultOrigin = "Wpisy na bloga.docx — zweryfikowane szablony JR/CVR/VSR";
    public const string LegacyDefaultOrigin = "Wymagania projektu — oczekuje na DOCX";
    public const string Baseline = """
    Przygotuj niezależny, długi artykuł automotive lifestyle. Nie tłumacz innej wersji językowej.
    PL: dynamiczny, obrazowy, emocjonalny. EN: premium, elegancki, lifestyle, lekko techniczny.
    Zwróć JSON: title, intro (maksymalnie 200 znaków), body (spójna narracja bez nagłówków i list), language, sources (adresy URL), warnings (lista).
    Nie wspominaj o SEO, słowach kluczowych, klientach ani dealerach. Bez wezwań sprzedażowych i bez bezpośrednich zwrotów do czytelnika.
    Opisz auto, projekt ramion, proporcje, widoczne concave, wykończenie, fitment i charakter zdjęć. Oddziel subiektywny wygląd od zweryfikowanych danych technicznych.
    Nie zgaduj masy, ET, PCD, rozmiarów, TÜV ani homologacji. Nie używaj danych niepotwierdzonych.
    Concaver: zawsze pełna nazwa Concaver z modelem; elegancja, premium, concave. Zakres rozmiarów i certyfikację potwierdź dla konkretnego modelu.
    Vesser: tytuł zawiera Vesser, model felg i samochód; luxury/performance, stance, wykonanie, technologia forged i personalizacja tylko w potwierdzonym zakresie.
    JR: długi, angażujący automotive lifestyle; naturalne alloy/custom/performance/aftermarket/concave wheels, fitment, felgi aluminiowe/sportowe/tuningowe bez sztucznego upakowania fraz.
    Dane: {CAR_MAKE} {CAR_MODEL} {CAR_VERSION}; {WHEEL_BRAND} {WHEEL_MODEL}; {WHEEL_FINISH}.
    Przód: {FRONT_SIZE}; tył: {REAR_SIZE}. Nie zamieniaj osi według szerokości.
    Dostępne rozmiary: {AVAILABLE_SIZES}. Certyfikaty: {VERIFIED_CERTIFICATIONS}. Szczegóły: {VERIFIED_PRODUCT_DETAILS}.
    Produkt: {PRODUCT_URL}; galeria: {GALLERY_URL}. Analiza zdjęć: {PHOTO_ANALYSIS}.
    Przykłady z instrukcji redakcyjnej nie są faktami o tej galerii; używaj wyłącznie dostarczonych potwierdzonych danych.
    """;
    public static string Default(WheelBrand brand)
    {
        using var stream = typeof(PromptService).Assembly.GetManifestResourceStream("WheelContentManager.Infrastructure.Templates." + brand + ".md") ?? throw new InvalidOperationException("Brak szablonu domyślnego.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public async Task<PromptVersion> CurrentAsync(WheelBrand brand, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var template = await db.PromptTemplates.Include(x => x.Versions).SingleAsync(x => x.Brand == brand, ct);
        return template.Versions.MaxBy(x => x.Revision)!;
    }
    public async Task<List<PromptVersion>> HistoryAsync(WheelBrand brand)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.PromptVersions.Where(x => db.PromptTemplates.Any(t => t.Id == x.PromptTemplateId && t.Brand == brand)).OrderByDescending(x => x.Revision).ToListAsync();
    }
    public async Task SaveAsync(WheelBrand brand, string content, string origin, bool verified, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Prompt nie może być pusty.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var template = await db.PromptTemplates.Include(x => x.Versions).SingleAsync(x => x.Brand == brand, ct);
        template.Versions.Add(new() { Revision = template.Versions.Max(x => x.Revision) + 1, Content = content, Origin = origin, EditorialDocumentVerified = verified }); await db.SaveChangesAsync(ct);
    }
    public static Dictionary<WheelBrand, string> ReadDocument(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var sections = new Dictionary<WheelBrand, StringBuilder>(); WheelBrand? current = null;
        var paragraphs = doc.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>() ?? [];
        foreach (var paragraph in paragraphs)
        {
            var text = paragraph.InnerText.Trim();
            var heading = text.Trim(' ', ':', '.', '#').ToUpperInvariant();
            if (heading is "JR" or "JR WHEELS" or "CVR" or "CONCAVER" or "CONCAVER WHEELS" or "VSR" or "VESSER" or "VESSER FORGED")
            {
                current = heading.StartsWith("JR") ? WheelBrand.JR : heading is "CVR" or "CONCAVER" or "CONCAVER WHEELS" ? WheelBrand.Concaver : WheelBrand.Vesser;
                sections.TryAdd(current.Value, new());
            }
            else if (current != null) sections[current.Value].AppendLine(text);
        }
        if (sections.Count != 3 || sections.Any(x => string.IsNullOrWhiteSpace(x.Value.ToString()))) throw new InvalidOperationException("Nie odnaleziono pełnych trzech sekcji JR, CVR, VSR. Dokument nie został zaimportowany.");
        return sections.ToDictionary(x => x.Key, x => x.Value.ToString().Trim());
    }
    public async Task ImportAsync(string path, CancellationToken ct = default)
    {
        var sections = ReadDocument(path);
        using var input = File.OpenRead(path);
        var known = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, ct)).Equals(EditorialDocumentSha256, StringComparison.OrdinalIgnoreCase);
        foreach (var item in sections)
        {
            var content = known ? Default(item.Key) : item.Value + "\n\n" + Default(item.Key);
            var current = await CurrentAsync(item.Key, ct);
            if (known && current.EditorialDocumentVerified && current.Content == content) continue;
            await SaveAsync(item.Key, content, known ? DefaultOrigin : Path.GetFileName(path), known, ct);
        }
    }
}
