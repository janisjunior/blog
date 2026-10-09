using System.Text.RegularExpressions;
namespace WheelContentManager.Core;

public static class TopicPlanner
{
    public static string Model(string? text)
    {
        var model = Regex.Match(text ?? "", @"(?i)\b(?:JR|SL|CVR|VSR|VF)[ -]?\d+\b");
        return model.Success ? Regex.Replace(model.Value.ToUpperInvariant(), @"[ -]", "") : (text ?? "").Trim().ToUpperInvariant();
    }
    public static IReadOnlyList<TopicSuggestion> Rank(WheelBrand brand, IEnumerable<Gallery> galleries, IReadOnlyList<RecentBlogPost> posts, IEnumerable<Article> local)
    {
        var history = posts.Select((p, i) => (Models: p.WheelModels.Select(Model).ToArray(), Time: p.Published ?? DateTimeOffset.MinValue.AddDays(posts.Count - i), Url: (string?)p.Url))
            .Concat(local.Where(a => a.Gallery.Brand == brand && a.Status is ArticleStatus.Ready or ArticleStatus.Approved or ArticleStatus.Published)
                .Select(a => (Models: new[] { Model(a.Gallery.Specification.Model) }, Time: a.PublishedAt ?? a.Created, Url: (string?)null)))
            .OrderByDescending(x => x.Time).Take(10).ToArray();
        var last = history.FirstOrDefault();
        return galleries.Where(g => g.Brand == brand && !g.Used && (!g.BlogBlocked || g.BlogCheckedAt == null) && !string.IsNullOrWhiteSpace(g.Specification.Model))
            .Select(g => new { Gallery = g, Model = Model(g.Specification.Model), Adjacent = last.Models?.Contains(Model(g.Specification.Model)) == true,
                Recent = history.Take(3).Count(p => p.Models.Contains(Model(g.Specification.Model))), Total = history.Count(p => p.Models.Contains(Model(g.Specification.Model))) })
            .OrderBy(x => x.Adjacent).ThenBy(x => x.Recent).ThenBy(x => x.Total)
            .ThenByDescending(x => x.Gallery.SourcePublished ?? x.Gallery.FirstDetected).ThenBy(x => x.Gallery.ListingOrder)
            .Select(x => new TopicSuggestion(x.Gallery,
                x.Adjacent ? $"{x.Model} występuje w ostatnim wpisie; lepiej wybrać inny model, jeśli jest dostępny." :
                x.Total == 0 ? $"{x.Model} nie występuje w ostatnich {history.Length} wpisach i gotowych artykułach; urozmaica kolejność." :
                $"{x.Model} różni się od ostatniego modelu; wystąpił {x.Total} razy w ostatnich {history.Length} wpisach i gotowych artykułach.", last.Url)).ToArray();
    }
}
