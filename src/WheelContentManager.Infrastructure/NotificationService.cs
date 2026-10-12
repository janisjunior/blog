using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using MimeKit.Utils;
using WheelContentManager.Core;

namespace WheelContentManager.Infrastructure;

public sealed class NotificationService(IDbContextFactory<ContentDb> factory, SettingsService settings, ISecretStore secrets, AppPaths paths) : IWeeklyPromptMailer
{
    public static bool IsFullSuccess(IReadOnlyList<Article> articles, int count) => Enum.GetValues<WheelBrand>().All(b => articles.Count(x => x.Gallery.Brand == b && x.Status is ArticleStatus.Ready or ArticleStatus.Approved or ArticleStatus.Published && new[] { "PL", "EN" }.All(l => x.Versions.Any(v => v.Language == l))) >= count);
    private async Task SendAsync(AppSettings s, string subject, string text, string messageId, string? attachment, CancellationToken ct, string? html = null)
    {
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.MailFrom) || string.IsNullOrWhiteSpace(s.MailTo)) throw new InvalidOperationException("Uzupełnij serwer SMTP, nadawcę i odbiorcę.");
        var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse(s.MailFrom)); message.To.Add(MailboxAddress.Parse(s.MailTo)); message.Subject = subject; message.MessageId = messageId;
        var body = new BodyBuilder { TextBody = text, HtmlBody = html }; if (attachment != null) await body.Attachments.AddAsync(attachment, ct); message.Body = body.ToMessageBody();
        using var smtp = new SmtpClient { Timeout = 30000 };
        string? tlsDetails = null;
        smtp.ServerCertificateValidationCallback = (_, certificate, chain, errors) =>
        {
            tlsDetails = SmtpTlsDiagnostics.Describe(s, certificate, chain, errors);
            return errors == System.Net.Security.SslPolicyErrors.None;
        };
        try
        {
            await smtp.ConnectAsync(s.SmtpHost, s.SmtpPort, s.SmtpSecurity == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            if (!string.IsNullOrWhiteSpace(s.SmtpUser)) await smtp.AuthenticateAsync(s.SmtpUser, secrets.Read("smtp") ?? throw new InvalidOperationException("Brak hasła SMTP."), ct);
        }
        catch (SslHandshakeException e)
        {
            throw new PromptMailNotSentException(tlsDetails ?? $"Nie ustanowiono TLS z {s.SmtpHost}:{s.SmtpPort} ({s.SmtpSecurity}). Serwer nie przekazał poprawnego certyfikatu lub nie obsługuje wybranego wariantu TLS. Logowanie nie zostało rozpoczęte.", e);
        }
        catch (Exception e) { throw new PromptMailNotSentException("Nie wysłano maila: sprawdź serwer SMTP, TLS i hasło aplikacji. Błąd: " + e.GetType().Name, e); }
        await smtp.SendAsync(message, ct); await smtp.DisconnectAsync(true, ct);
    }
    async Task IWeeklyPromptMailer.SendAsync(WeeklyPromptBatch batch, CancellationToken ct)
    {
        var s = await settings.LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || !MailboxAddress.TryParse(s.MailFrom, out _) || !MailboxAddress.TryParse(s.MailTo, out _) ||
            (!string.IsNullOrWhiteSpace(s.SmtpUser) && string.IsNullOrWhiteSpace(secrets.Read("smtp"))))
            throw new PromptMailNotSentException("Uzupełnij SMTP, adresy i hasło aplikacji w ustawieniach poczty.");
        if (batch.Items.Count != 3 || Enum.GetValues<WheelBrand>().Any(b => batch.Items.Count(i => i.Brand == b && i.Package != null && File.Exists(i.Package.Document)) != 1))
            throw new PromptMailNotSentException("Nie ma trzech kompletnych paczek — mail nie został wysłany.");
        var zip = ExportService.CreateZip(batch.Items.Select(i => i.Package!.Folder), Path.Combine(paths.Root, "paczki-" + batch.Key + ".zip"));
        var text = "Gotowe 3 paczki do własnego ChatGPT: po jednej dla JR, Concaver i Vesser. Rozpakuj załączony ZIP i wrzuć każdy DOCX do ChatGPT bez dodatkowego polecenia. Najpierw otrzymasz EN, następnie PL. Zdjęcia pobierzesz z linków w dokumentach.\n\n" +
            string.Join("\n\n", batch.Items.Select(i => i.Brand.Name() + " — " + i.Package!.GalleryUrl + "\n" + i.Reason)) + "\n\nOtwórz program: wtblog://weekly/" + batch.Key + "\nPaczki są też w sekcji Paczki tygodniowe programu. Link wymaga zainstalowanej aplikacji na tym komputerze; jeśli poczta go nie obsługuje, użyj załącznika.";
        var html = "<p>Gotowe trzy paczki do ChatGPT: JR, Concaver i Vesser.</p><p>Rozpakuj załącznik ZIP i wrzuć DOCX do ChatGPT. Kolejność artykułów: EN, potem PL.</p><ul>" +
            string.Join("", batch.Items.Select(i => "<li>" + System.Net.WebUtility.HtmlEncode(i.Brand.Name()) + ": <a href=\"" + System.Net.WebUtility.HtmlEncode(i.Package!.GalleryUrl) + "\">Galeria źródłowa</a></li>")) +
            "</ul><p><a href=\"wtblog://weekly/" + batch.Key + "\">Otwórz WT - Blog Generator</a></p><p>Jeśli link nie jest obsługiwany przez pocztę, użyj załącznika albo sekcji Paczki tygodniowe w programie.</p>";
        await SendAsync(s, "WT - Blog Generator — 3 paczki do ChatGPT — " + batch.Key, text, batch.MessageId, zip, ct, html);
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
    public async Task TestAsync(CancellationToken ct = default) => await SendAsync(await settings.LoadAsync(ct), "WT - Blog Generator – wiadomość testowa", "Połączenie SMTP działa. To test ustawień poczty, a nie zestaw tygodniowy.", MimeUtils.GenerateMessageId(), null, ct);
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
