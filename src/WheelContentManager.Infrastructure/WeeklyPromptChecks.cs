using WheelContentManager.Core;
namespace WheelContentManager.Infrastructure;
public sealed partial class ContentService
{
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
