using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WheelContentManager.Core;
namespace WheelContentManager.Infrastructure;

public sealed record WeeklyPromptItem(long GalleryId, WheelBrand Brand, string Model, string Reason, GalleryPromptPackage? Package);
public sealed class WeeklyPromptBatch
{
    public string Key { get; set; } = "";
    public DateTimeOffset Due { get; set; }
    public DateTimeOffset? PreparedAt { get; set; }
    public DateTimeOffset? DeliveryCheckedAt { get; set; }
    public DateTimeOffset? LastSiteCheckAt { get; set; }
    public DateTimeOffset? LastMailAttempt { get; set; }
    public string MailState { get; set; } = "Pending";
    public string MessageId { get; set; } = MimeKit.Utils.MimeUtils.GenerateMessageId();
    public string Status { get; set; } = "Przygotowanie paczek";
    public List<WeeklyPromptItem> Items { get; set; } = [];
}
public interface IWeeklyPromptMailer { Task SendAsync(WeeklyPromptBatch batch, CancellationToken ct); }
public sealed class PromptMailNotSentException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public sealed class WeeklyPromptService(IDbContextFactory<ContentDb> factory, ContentService content, SettingsService settings, IWeeklyPromptMailer mailer, AppPaths paths)
{
    private const string Prefix = "weekly-prompts-";
    public static TimeZoneInfo Poland => TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Central European Standard Time" : "Europe/Warsaw");
    public static DateTimeOffset FridayAtNoon(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Poland);
        var date = local.Date.AddDays(((int)DayOfWeek.Friday - (int)local.DayOfWeek + 7) % 7).AddHours(12);
        return new(date, Poland.GetUtcOffset(date));
    }
    public async Task<List<WeeklyPromptBatch>> BatchesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var json = await db.ApplicationSettings.Where(s => s.Key.StartsWith(Prefix)).Select(s => s.Json).ToListAsync(ct);
        return json.Select(j => JsonSerializer.Deserialize<WeeklyPromptBatch>(j)!).OrderByDescending(b => b.Due).ToList();
    }
    private async Task SaveAsync(WeeklyPromptBatch batch, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var key = Prefix + batch.Key;
        var row = await db.ApplicationSettings.FindAsync([key], ct);
        if (row == null) { row = new() { Key = key }; db.Add(row); }
        row.Json = JsonSerializer.Serialize(batch); await db.SaveChangesAsync(ct);
    }
    public static IReadOnlyList<Gallery> RankCandidates(WheelBrand brand, IEnumerable<Gallery> galleries, IReadOnlyList<RecentBlogPost> posts, IEnumerable<WeeklyPromptBatch> previous)
    {
        var lastPrepared = previous.OrderByDescending(b => b.Due).SelectMany(b => b.Items).FirstOrDefault(i => i.Brand == brand);
        var latest = posts.FirstOrDefault()?.WheelModels.Select(TopicPlanner.Model).ToHashSet() ?? [];
        if (lastPrepared != null) latest.Add(TopicPlanner.Model(lastPrepared.Model));
        var recent = posts.Take(3).SelectMany(p => p.WheelModels).Select(TopicPlanner.Model).ToArray();
        return galleries.Where(g => g.Brand == brand && !g.PromptUsed && !g.Used && !g.BlogBlocked && !string.IsNullOrWhiteSpace(g.Specification.Model) && !latest.Contains(TopicPlanner.Model(g.Specification.Model)))
            .OrderBy(g => recent.Count(m => m == TopicPlanner.Model(g.Specification.Model))).ThenBy(g => g.ListingOrder).ToArray();
    }
    public async Task<string> TickAsync(DateTimeOffset now, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var gate = OperationLock.Acquire(Path.Combine(paths.Root, "weekly-prompts.lock"));
        var s = await settings.LoadAsync(ct);
        if (!s.WeeklyPromptsEnabled) return "Przygotowanie cotygodniowe wyłączone.";
        if (OperationSession.Paused(paths)) return "Przygotowanie w tle wstrzymane. Kliknij Wznów.";
        var batches = await BatchesAsync(ct);
        var batch = batches.Where(b => b.Due <= now && b.MailState != "Sent").OrderBy(b => b.Due).FirstOrDefault();
        if (batch == null)
        {
            var due = FridayAtNoon(now);
            if (due < (s.WeeklyPromptEnabledAt ?? now)) due = FridayAtNoon(now.AddDays(7));
            batch = batches.FirstOrDefault(b => b.Due == due);
            if (batch?.MailState == "Sent") { due = FridayAtNoon(due.AddDays(7)); batch = batches.FirstOrDefault(b => b.Due == due); }
            if (batch == null) { batch = new() { Key = TimeZoneInfo.ConvertTime(due, Poland).ToString("yyyy-MM-dd"), Due = due }; await SaveAsync(batch, ct); }
        }
        if (batch.MailState is "Sending" or "Unknown") return "Niepewny wynik maila — sprawdź skrzynkę i potwierdź odbiór lub ponów wysyłkę w ustawieniach poczty.";
        var recheck = now >= batch.Due.AddMinutes(-15) && (batch.DeliveryCheckedAt == null || batch.DeliveryCheckedAt < batch.Due.AddMinutes(-15) || now - batch.DeliveryCheckedAt > TimeSpan.FromDays(1));
        foreach (var brand in Enum.GetValues<WheelBrand>())
        {
            ct.ThrowIfCancellationRequested(); if (OperationSession.Paused(paths)) throw new OperationCanceledException(ct);
            var item = batch.Items.SingleOrDefault(i => i.Brand == brand);
            if (item?.Package != null && File.Exists(item.Package.Document) && File.Exists(item.Package.Instructions) && !recheck) continue;
            var rows = await content.RefreshPromptGalleriesAsync(brand, progress, ct);
            var recent = await content.RecentPromptPostsAsync(brand, ct);
            if (item != null)
            {
                var existing = rows.SingleOrDefault(g => g.Id == item.GalleryId);
                if (existing == null || recent.FirstOrDefault()?.WheelModels.Any(m => TopicPlanner.Model(m) == TopicPlanner.Model(item.Model)) == true) { batch.Items.Remove(item); item = null; }
            }
            if (item == null)
            {
                var selected = RankCandidates(brand, rows, recent, batches.Where(b => b.Due < batch.Due)).FirstOrDefault()
                    ?? throw new InvalidOperationException("Brak nowej galerii " + brand.Name() + " z innym modelem niż ostatnio. Nie wysłano niepełnego zestawu.");
                item = new(selected.Id, brand, selected.Specification.Model!, "Nowa galeria; inny model niż w ostatnim wpisie i poprzedniej paczce.", null);
                batch.Items.Add(item); await SaveAsync(batch, ct);
            }
            var package = await content.ExportGalleryPromptFilesAsync(item.GalleryId, Path.Combine(paths.Exports, "Paczki tygodniowe", batch.Key), progress, ct, includePhoto: false);
            var actual = await content.GalleryAsync(item.GalleryId, ct);
            if (TopicPlanner.Model(actual?.Specification.Model) != TopicPlanner.Model(item.Model))
            {
                batch.Items.Remove(item); await SaveAsync(batch, ct);
                throw new InvalidOperationException("Pełna galeria wskazuje inny model niż lista — temat pominięto, aby nie powtórzyć modelu.");
            }
            batch.Items[batch.Items.IndexOf(item)] = item with { Package = package }; await SaveAsync(batch, ct);
        }
        batch.PreparedAt ??= now; if (recheck) batch.DeliveryCheckedAt = now;
        batch.LastSiteCheckAt ??= now;
        batch.Status = "3 paczki gotowe — EN → PL. Termin: piątek 12:00 (Polska)."; await SaveAsync(batch, ct);
        var deliver = now >= batch.Due && s.WeeklyPromptMailEnabled && (batch.LastMailAttempt == null || now - batch.LastMailAttempt >= TimeSpan.FromMinutes(15));
        if (!deliver && now - batch.LastSiteCheckAt < TimeSpan.FromHours(6)) return batch.Status + (!s.WeeklyPromptMailEnabled ? " Skonfiguruj i włącz pocztę." : "");
        // Recheck live indexes at delivery, even if packages were prepared earlier.
        foreach (var item in batch.Items)
        {
            var match = await content.CheckPromptDeliveryAsync(item.GalleryId, ct);
            var recent = await content.RecentPromptPostsAsync(item.Brand, ct);
            if (match.Published || match.PossibleDuplicate || recent.FirstOrDefault()?.WheelModels.Any(m => TopicPlanner.Model(m) == TopicPlanner.Model(item.Model)) == true)
            {
                batch.Items.Remove(item); batch.DeliveryCheckedAt = null; await SaveAsync(batch, ct);
                return "Temat pojawił się na /blog — kolejny przebieg przygotuje zastępczą paczkę.";
            }
        }
        batch.LastSiteCheckAt = now; await SaveAsync(batch, ct);
        if (!deliver) return batch.Status + (!s.WeeklyPromptMailEnabled ? " Skonfiguruj i włącz pocztę." : "");
        using var sending = OperationSession.Start(paths, "Wysyłanie 3 paczek mailem", ct);
        batch.MailState = "Sending"; batch.LastMailAttempt = now; await SaveAsync(batch, sending.Token);
        try { await mailer.SendAsync(batch, sending.Token); batch.MailState = "Sent"; batch.Status = "Wysłano 3 paczki na " + s.MailTo; }
        catch (PromptMailNotSentException e) { batch.MailState = "Pending"; batch.Status = e.Message; }
        catch { batch.MailState = "Unknown"; batch.Status = "Niepewny wynik wysyłki — sprawdź skrzynkę, aby uniknąć duplikatu."; throw; }
        finally { await SaveAsync(batch, CancellationToken.None); }
        return batch.Status;
    }
    public async Task ResolveMailAsync(string key, bool received, CancellationToken ct = default)
    {
        using var gate = OperationLock.Acquire(Path.Combine(paths.Root, "weekly-prompts.lock"));
        var batch = (await BatchesAsync(ct)).Single(b => b.Key == key);
        if (batch.MailState is not ("Unknown" or "Sending")) throw new InvalidOperationException("Ta wiadomość nie wymaga potwierdzenia.");
        batch.MailState = received ? "Sent" : "Pending"; batch.LastMailAttempt = null;
        batch.Status = received ? "Potwierdzono odbiór maila." : "Użytkownik zlecił ponowienie wysyłki."; await SaveAsync(batch, ct);
    }
}
