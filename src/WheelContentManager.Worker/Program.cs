using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Infrastructure;

var rootIndex = Array.IndexOf(args, "--data-root");
var root = rootIndex >= 0 && rootIndex + 1 < args.Length ? args[rootIndex + 1] : null;
using var host = Bootstrap.Create(args, root);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    await Bootstrap.InitializeAsync(host.Services, cancellation.Token);
    var service = host.Services.GetRequiredService<ContentService>();
    var progress = new Progress<string>(Console.WriteLine);
    if (args.Contains("--prepare-stock"))
    {
        Console.WriteLine(await service.PrepareReadySetAsync(progress, cancellation.Token));
        return (await service.ReadySetAsync()).Count == 3 || !(await host.Services.GetRequiredService<SettingsService>().LoadAsync()).BackgroundPreparationEnabled ? 0 : 1;
    }
    else if (args.Contains("--run-weekly"))
    {
        var configuration = await host.Services.GetRequiredService<SettingsService>().LoadAsync();
        if (configuration.ScheduleEnabled && configuration.BackgroundPreparationEnabled)
        {
            Console.WriteLine(await service.PrepareReadySetAsync(progress, cancellation.Token));
            return (await service.ReadySetAsync()).Count == 3 ? 0 : 1;
        }
        Console.WriteLine(await service.RunCycleAsync(true, progress, cancellation.Token));
        var settings = await host.Services.GetRequiredService<SettingsService>().LoadAsync();
        if (settings.ScheduleEnabled)
        {
            var run = (await service.RunsAsync()).FirstOrDefault();
            var sent = await host.Services.GetRequiredService<NotificationService>().HistoryAsync();
            if (run == null || run.Status != "Zakończono" || !sent.Any(x => x.RunId == run.Id + ":success" && x.State == "Sent")) return 1;
        }
    }
    else if (Array.IndexOf(args, "--write-background-task") is var wi && wi >= 0 && wi + 1 < args.Length)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Eksport zadania Windows wymaga Windows.");
        var configuration = await host.Services.GetRequiredService<SettingsService>().LoadAsync();
        var xml = WindowsScheduler.BackgroundTaskXml(configuration, Environment.ProcessPath!, System.Security.Principal.WindowsIdentity.GetCurrent().Name);
        await File.WriteAllTextAsync(args[wi + 1], xml, System.Text.Encoding.Unicode, cancellation.Token);
    }
    else if (args.Contains("--sync")) Console.WriteLine(await service.SyncAsync(progress, cancellation.Token));
    else if (Array.IndexOf(args, "--check-blog") is var bi && bi >= 0 && bi + 1 < args.Length && long.TryParse(args[bi + 1], out var blogId)) Console.WriteLine(await service.CheckBlogAsync(blogId, cancellation.Token));
    else if (args.Contains("--suggest-topics"))
    {
        foreach (var topic in await service.SuggestTopicsAsync(ct: cancellation.Token)) Console.WriteLine($"{topic.Gallery.Id}: {topic.Gallery.Display}\n{topic.Reason}\nOstatni wpis: {topic.LatestPostUrl ?? "lokalny artykuł"}");
    }
    else if (args.Contains("--diagnose"))
    {
        var paths = host.Services.GetRequiredService<AppPaths>();
        Console.WriteLine($"Baza: {paths.Database}\nGalerie: {(await service.GalleriesAsync()).Count}\nArtykuły: {(await service.ArticlesAsync()).Count}");
        foreach (var log in (await service.LogsAsync()).Take(10)) Console.WriteLine($"{log.Created:g} {log.Operation}: {log.Message}");
    }
    else if (args.Length == 2 && args[0] == "--import-prompts") { await host.Services.GetRequiredService<PromptService>().ImportAsync(args[1], cancellation.Token); Console.WriteLine("Zaimportowano sekcje. Dostarczony z projektem dokument ma gotowe zweryfikowane szablony; inny dokument wymaga sprawdzenia w Prompty AI."); }
    else if (args.Length == 2 && args[0] == "--dry-run" && long.TryParse(args[1], out var id))
    {
        var article = await service.GenerateAsync(id, false, true, null, progress, cancellation.Token);
        Console.WriteLine($"Test: {article.Status}. Bez wiadomości e-mail i bez wykorzystania galerii.");
        foreach (var v in article.Versions) Console.WriteLine($"{v.Language}: {v.Title}\n{v.Intro}\n{v.Body}");
    }
    else Console.WriteLine("WT - Blog Generator\n--prepare-stock: uzupełnianie 3 gotowych artykułów w tle\n--run-weekly: cykl według zapisanych ustawień\n--sync: pobieranie galerii\n--diagnose: lokalna diagnostyka\n--import-prompts <plik.docx>: import sekcji JR/CVR/VSR\n--dry-run <ID galerii>: pełny test z AI (koszt API), bez e-maila i oznaczania galerii.");
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Anulowano operację. Postęp pozostaje w bazie."); return 2; }
catch (Exception e) { Console.Error.WriteLine(e is InvalidOperationException or PlatformNotSupportedException ? e.Message : $"Błąd: {e.GetType().Name}. Sprawdź ustawienia i dziennik aplikacji."); return 1; }
