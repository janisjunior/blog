using WheelContentManager.Core;
namespace WheelContentManager.Infrastructure;
public sealed partial class ContentService
{
    private sealed class DeliveryProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
    internal async Task<IReadOnlyList<long>> CheckPromptDeliveriesAsync(IReadOnlyList<WeeklyPromptItem> items, CancellationToken ct, IProgress<string>? progress)
    {
        using var operation = OperationSession.Start(paths, "Kontrola trzech blogów przed mailem", ct);
        var output = operation.Progress(progress); var sync = new object();
        var stages = items.ToDictionary(i => i.Brand, _ => "Rozpoczynanie kontroli; mail nie został jeszcze wysłany.");
        var checks = items.Select(async item =>
        {
            var brandProgress = new DeliveryProgress(message =>
            {
                lock (sync) { stages[item.Brand] = message; output.Report(string.Join("\n", stages.OrderBy(s => s.Key).Select(s => s.Key.Name() + " — " + s.Value))); }
            });
            brandProgress.Report("Sprawdzanie archiwum /blog…");
            var match = await CheckBlogAsync(item.GalleryId, operation.Token, forExport: true, progress: brandProgress);
            var recent = await blog.RecentFreshAsync(item.Brand, operation.Token, brandProgress);
            var invalid = match.Published || match.PossibleDuplicate || recent.FirstOrDefault()?.WheelModels.Any(m => TopicPlanner.Model(m) == TopicPlanner.Model(item.Model)) == true;
            brandProgress.Report(invalid ? "Temat już opublikowany lub powtarza ostatni model." : "Kontrola zakończona.");
            return (item.GalleryId, Invalid: invalid);
        });
        var results = await Task.WhenAll(checks);
        return results.Where(r => r.Invalid).Select(r => r.GalleryId).ToArray();
    }
    public async Task<IReadOnlyList<RecentBlogPost>> RecentPromptPostsAsync(WheelBrand brand, CancellationToken ct, IProgress<string>? progress = null)
    {
        using var operation = OperationSession.Start(paths, "Sprawdzanie ostatnich modeli na /blog", ct);
        return await blog.RecentFreshAsync(brand, operation.Token, operation.Progress(progress));
    }
    public async Task<BlogCheck> CheckPromptDeliveryAsync(long id, CancellationToken ct, IProgress<string>? progress = null)
    {
        using var operation = OperationSession.Start(paths, "Kontrola /blog przed wysyłką paczki", ct);
        return await CheckBlogAsync(id, operation.Token, forExport: true, progress: operation.Progress(progress));
    }
}
