using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using MimeKit.Utils;
using WheelContentManager.Core;

namespace WheelContentManager.Infrastructure;

public sealed class NotificationService(IDbContextFactory<ContentDb> factory, SettingsService settings, ISecretStore secrets, AppPaths paths)
{
    public static bool IsFullSuccess(IReadOnlyList<Article> articles, int count) => Enum.GetValues<WheelBrand>().All(b => articles.Count(x => x.Gallery.Brand == b && x.Status is ArticleStatus.Ready or ArticleStatus.Approved or ArticleStatus.Published && new[] { "PL", "EN" }.All(l => x.Versions.Any(v => v.Language == l))) >= count);
    private async Task SendAsync(AppSettings s, string subject, string text, string messageId, string? attachment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.MailFrom) || string.IsNullOrWhiteSpace(s.MailTo)) throw new InvalidOperationException("Uzupełnij serwer SMTP, nadawcę i odbiorcę.");
        var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse(s.MailFrom)); message.To.Add(MailboxAddress.Parse(s.MailTo)); message.Subject = subject; message.MessageId = messageId;
        var body = new BodyBuilder { TextBody = text }; if (attachment != null) await body.Attachments.AddAsync(attachment, ct); message.Body = body.ToMessageBody();
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(s.SmtpHost, s.SmtpPort, s.SmtpSecurity == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrWhiteSpace(s.SmtpUser)) await smtp.AuthenticateAsync(s.SmtpUser, secrets.Read("smtp") ?? throw new InvalidOperationException("Brak hasła SMTP."), ct);
        await smtp.SendAsync(message, ct); await smtp.DisconnectAsync(true, ct);
    }
    public async Task<List<NotificationHistory>> HistoryAsync()
    {
        await using var db = await factory.CreateDbContextAsync(); return await db.NotificationHistory.OrderByDescending(x => x.Id).Take(100).AsNoTracking().ToListAsync();
    }
    public async Task MarkReceivedAsync(long id, CancellationToken ct)
    {
        using var gate = OperationLock.Acquire(Path.Combine(paths.Root, "operation.lock")); await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.NotificationHistory.SingleAsync(x => x.Id == id, ct);
        if (row.State is not ("Unknown" or "Sending")) throw new InvalidOperationException("Potwierdzenie odbioru dotyczy tylko wiadomości o niepewnym wyniku.");
        row.State = "Sent"; row.SentAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
    }
    public async Task RetryAsync(long id, CancellationToken ct)
    {
        using var gate = OperationLock.Acquire(Path.Combine(paths.Root, "operation.lock")); await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.NotificationHistory.SingleAsync(x => x.Id == id, ct);
        if (row.State is "Sent" or "Superseded") throw new InvalidOperationException("Ta wiadomość została już wysłana. Nie utworzono duplikatu.");
        row.State = "Pending"; await db.SaveChangesAsync(ct);
        var runId = row.RunId.Split(':')[0]; var run = await db.AutomationRuns.SingleAsync(x => x.Id == runId, ct);
        var articles = await db.FullArticles.AsNoTracking().Where(x => x.AutomationRunId == runId).ToListAsync(ct);
        await NotifyAsync(run, articles, run.Report ?? "Raport cyklu", ct);
    }
    public async Task TestAsync(CancellationToken ct = default) => await SendAsync(await settings.LoadAsync(ct), "WT - Blog Generator – wiadomość testowa", "Połączenie SMTP działa. Ta wiadomość nie oznacza zakończenia generowania artykułów.", MimeUtils.GenerateMessageId(), null, ct);
    public async Task NotifyAsync(AutomationRun run, IReadOnlyList<Article> articles, string report, CancellationToken ct)
    {
        var s = await settings.LoadAsync(ct);
        var stock = run.PeriodKey.StartsWith("stock-", StringComparison.Ordinal);
        if (stock && run.Status == "Zakończono") articles = articles.Where(ReadyArticles.Prepared).ToList();
        var complete = stock ? run.Status == "Zakończono" && articles.Count > 0 && articles.All(ReadyArticles.Prepared) : IsFullSuccess(articles, s.ArticlesPerBrand); var key = run.Id + (complete ? ":success" : ":partial");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (complete)
        {
            var obsolete = await db.NotificationHistory.Where(n => n.RunId == run.Id + ":partial" && n.State == "Pending").ToListAsync(ct);
            foreach (var old in obsolete) old.State = "Superseded";
            if (obsolete.Count > 0) await db.SaveChangesAsync(ct);
        }
        var row = await db.NotificationHistory.SingleOrDefaultAsync(x => x.RunId == key, ct);
        if (row?.State == "Sent") return;
        if (row?.State is "Sending" or "Unknown") throw new InvalidOperationException("Wysyłka poprzedniego maila ma niepewny wynik. Sprawdź skrzynkę i historię; program nie wyśle automatycznego duplikatu.");
        if (row == null) { row = new() { RunId = key, MessageId = MimeUtils.GenerateMessageId() }; db.Add(row); await db.SaveChangesAsync(ct); }
        s.Validate();
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || !MailboxAddress.TryParse(s.MailFrom, out _) || !MailboxAddress.TryParse(s.MailTo, out _)) throw new InvalidOperationException("Skonfiguruj prawidłowy serwer SMTP, adres nadawcy i odbiorcy. Powiadomienie oczekuje w bazie.");
        if (!string.IsNullOrWhiteSpace(s.SmtpUser) && string.IsNullOrWhiteSpace(secrets.Read("smtp"))) throw new InvalidOperationException("Brak hasła SMTP. Powiadomienie oczekuje w bazie.");
        string? attachment = null;
        if (complete && s.AttachZip)
        {
            var dirs = articles.Where(x => x.ExportFolder != null).Select(x => x.ExportFolder!);
            attachment = await Task.Run(() => ExportService.CreateZip(dirs, Path.Combine(paths.Root, "cykl-" + run.Id + ".zip")), ct);
        }
        var subject = complete ? $"WT - Blog Generator – {articles.Count} nowe artykuły gotowe" : "WT - Blog Generator – raport częściowego wykonania";
        var text = $"Data: {DateTime.Now:g}\n{report}\n\n" + string.Join("\n", articles.Select(a => $"{a.Gallery.Display}: {a.Status.Label()} — PL: {a.Versions.Any(v => v.Language == "PL")}, EN: {a.Versions.Any(v => v.Language == "EN")}. Pliki: {a.ExportFolder ?? "brak"}")) + "\n\nArtykuły wymagają zatwierdzenia. Program nie publikuje treści.";
        row.State = "Sending"; await db.SaveChangesAsync(ct);
        try { await SendAsync(s, subject, text, row.MessageId, attachment, ct); row.State = "Sent"; row.SentAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(CancellationToken.None); }
        catch { row.State = "Unknown"; await db.SaveChangesAsync(CancellationToken.None); throw; }
    }
}
