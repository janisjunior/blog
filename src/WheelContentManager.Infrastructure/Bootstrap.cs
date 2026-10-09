using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WheelContentManager.AI;
using WheelContentManager.Core;
using WheelContentManager.GalleryProviders;

namespace WheelContentManager.Infrastructure;

public static class Bootstrap
{
    public static IHost Create(string[] args, string? dataRoot = null)
    {
        var builder = Host.CreateApplicationBuilder(args); builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning); builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning); var paths = new AppPaths(dataRoot);
        builder.Services.AddSingleton(paths);
        builder.Services.AddDbContextFactory<ContentDb>(options => options.UseSqlite($"Data Source={paths.Database};Default Timeout=30"));
        builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>(); builder.Services.AddSingleton<SettingsService>(); builder.Services.AddSingleton<PromptService>(); builder.Services.AddSingleton<ExportService>(); builder.Services.AddSingleton<NotificationService>(); builder.Services.AddSingleton<WindowsScheduler>(); builder.Services.AddSingleton<ContentService>(); builder.Services.AddSingleton<IBlogPublicationChecker, BlogPublicationChecker>();
        builder.Services.AddHttpClient<OpenAiProvider>(h => h.Timeout = TimeSpan.FromMinutes(5)); builder.Services.AddHttpClient<AnthropicProvider>(h => h.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>()); builder.Services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
        builder.Services.AddHttpClient<SiteClient>(h => { h.Timeout = TimeSpan.FromSeconds(45); h.DefaultRequestHeaders.UserAgent.ParseAdd("WheelContentManager/1.0"); });
        var profiles = Path.Combine(paths.Root, "Profiles"); Directory.CreateDirectory(profiles);
        var shipped = Path.Combine(AppContext.BaseDirectory, "Profiles");
        if (Directory.Exists(shipped)) foreach (var profile in Directory.EnumerateFiles(shipped, "*.json")) if (!File.Exists(Path.Combine(profiles, Path.GetFileName(profile)))) File.Copy(profile, Path.Combine(profiles, Path.GetFileName(profile)));
        builder.Services.AddTransient<IGalleryProvider>(sp => new JrGalleryProvider(sp.GetRequiredService<SiteClient>(), profiles));
        builder.Services.AddTransient<IGalleryProvider>(sp => new ConcaverGalleryProvider(sp.GetRequiredService<SiteClient>(), profiles));
        builder.Services.AddTransient<IGalleryProvider>(sp => new VesserGalleryProvider(sp.GetRequiredService<SiteClient>(), profiles));
        builder.Services.AddHttpClient("images", h => { h.Timeout = TimeSpan.FromSeconds(45); h.DefaultRequestHeaders.UserAgent.ParseAdd("WheelContentManager/1.0"); });
        return builder.Build();
    }
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var paths = services.GetRequiredService<AppPaths>(); using var gate = OperationLock.Acquire(Path.Combine(paths.Root, "database.lock"));
        await using var db = await services.GetRequiredService<IDbContextFactory<ContentDb>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        foreach (var brand in Enum.GetValues<WheelBrand>())
        {
            if (!await db.Brands.AnyAsync(x => x.Code == brand, ct)) db.Brands.Add(new() { Code = brand, Name = brand.Name() });
            var template = await db.PromptTemplates.Include(x => x.Versions).SingleOrDefaultAsync(x => x.Brand == brand, ct);
            if (template == null) db.PromptTemplates.Add(new() { Brand = brand, Versions = [new() { Revision = 1, Content = PromptService.Default(brand), Origin = PromptService.DefaultOrigin, EditorialDocumentVerified = true }] });
            else
            {
                var current = template.Versions.MaxBy(x => x.Revision)!;
                // Aktualizuj wyłącznie oryginalny, nietknięty szablon roboczy; zachowaj edycje użytkownika i historię.
                if (current.Revision == 1 && current.Origin == PromptService.LegacyDefaultOrigin && !current.EditorialDocumentVerified && current.Content.StartsWith("# Szablon roboczy — wymaga dokumentu redakcyjnego"))
                    template.Versions.Add(new() { Revision = 2, Content = PromptService.Default(brand), Origin = PromptService.DefaultOrigin, EditorialDocumentVerified = true });
            }
        }
        await db.SaveChangesAsync(ct);
        try
        {
            using var active = OperationLock.Acquire(Path.Combine(paths.Root, "operation.lock"));
            var interrupted = await db.GenerationJobs.Where(x => x.Status == "W toku").ToListAsync(ct);
            foreach (var job in interrupted) { job.Status = "Przerwane — możliwe wznowienie"; job.Updated = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException) { /* Operacja innego procesu trwa: nie zmieniaj jej statusu. */ }
    }
}
