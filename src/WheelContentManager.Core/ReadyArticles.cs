using System.Text.Json;
namespace WheelContentManager.Core;
public static class ReadyArticles
{
    public static bool Prepared(Article article)
    {
        if (article.Status is not (ArticleStatus.Ready or ArticleStatus.Approved or ArticleStatus.Published)) return false;
        try
        {
            return new[] { "PL", "EN" }.All(language => article.Versions.Where(v => v.Language == language).MaxBy(v => v.Revision) is { } v &&
                !string.IsNullOrWhiteSpace(v.Body) && JsonSerializer.Deserialize<string[]>(v.WarningsJson) is { Length: 0 });
        }
        catch (JsonException) { return false; }
    }
    public static bool Available(Article article) => article.Status != ArticleStatus.Published && Prepared(article);
    public static IReadOnlyList<Article> Set(IEnumerable<Article> articles) => articles.Where(Available)
        .GroupBy(a => a.Gallery.Brand).Select(g => g.OrderByDescending(a => a.Created).First()).OrderBy(a => a.Gallery.Brand).ToArray();
}
