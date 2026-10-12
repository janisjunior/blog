using System.Text.Json;
using WheelContentManager.Core;
using Xunit;
namespace WheelContentManager.Tests;
public class ChatGptResponseParserTests
{
    private static object Text(string language) => new { title = "Tytuł", intro = "Intro", body = "Akapit pierwszy.\n\nAkapit drugi.", language, sources = new[] { "https://jr-wheels.com/gallery/test" } };
    [Fact] public void MarkdownEscapedUrlColonIsRecoveredWithoutChangingArticleText()
    {
        var original = JsonSerializer.Serialize(new[] { Text("PL"), Text("EN") });
        var broken = original.Replace("https://", @"https\://");
        var recovered = ChatGptResponseParser.Parse(broken);
        var expected = ChatGptResponseParser.Parse(original);
        Assert.Equal(expected[0].Body, recovered[0].Body);
        Assert.Equal(expected[1].Body, recovered[1].Body);
        Assert.All(recovered, article => Assert.Equal("https://jr-wheels.com/gallery/test", Assert.Single(article.Sources)));
    }
    [Fact] public void ValidLiteralBackslashAndUnrelatedInvalidEscapesAreNotChanged()
    {
        var response = JsonSerializer.Serialize(new[] { new { title = "T", intro = "I", body = @"Literal https\://example.test and C:\wheels", language = "PL" }, Text("EN") });
        Assert.Equal(@"Literal https\://example.test and C:\wheels", ChatGptResponseParser.Parse(response)[0].Body);
        Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse("{\"articles\":[{\"body\":\"bad\\q\"}]}"));
    }
    [Fact] public void MissingWarningsPrefaceAndMultipleCodeBlocksAreAccepted()
    {
        var response = "Oto oba artykuły:\n```json\n" + JsonSerializer.Serialize(Text("pl")) + "\n```\nDruga wersja:\n```json\n" + JsonSerializer.Serialize(Text("en")) + "\n```";
        var articles = ChatGptResponseParser.Parse(response); Assert.Equal(2, articles.Count);
        Assert.Equal("PL", articles[0].Language); Assert.Empty(articles[0].Warnings); Assert.Contains("\n\n", articles[0].Body);
    }
    [Fact] public void ActualLineBreaksInsideJsonStringAndTrailingCommasPreserveBody()
    {
        var first = JsonSerializer.Serialize(Text("PL")).Replace("Akapit pierwszy.\\n\\nAkapit drugi.", "Akapit pierwszy.\n\nAkapit drugi.");
        var response = "{\"articles\":[" + first + "," + JsonSerializer.Serialize(Text("EN")) + ",]}";
        Assert.Equal("Akapit pierwszy.\n\nAkapit drugi.", ChatGptResponseParser.Parse(response)[0].Body);
    }
    [Fact] public void LanguageKeyedObjectsAndParagraphArraysAreAccepted()
    {
        var response = JsonSerializer.Serialize(new { PL = new { Title = "Tytuł", Intro = "Wstęp", Body = new[] { "Pierwszy", "Drugi" } }, EN = Text("EN") });
        Assert.Equal("Pierwszy\n\nDrugi", ChatGptResponseParser.Parse(response)[0].Body);
    }
    [Fact] public void MarkedPlainTextNeedsNoEscapingAndKeepsQuotesAndParagraphs()
    {
        var pl = "[ARTYKUL_PL]\n[TYTUL]\nAuto\n[INTRO]\nWstęp\n[TEKST]\nTak zwany \"fitment\".\n\nDrugi akapit.\n[ZRODLA]\nhttps://jr-wheels.com/gallery/test\n[KONIEC_ARTYKUL_PL]";
        var en = pl.Replace("ARTYKUL_PL", "ARTYKUL_EN"); var result = ChatGptResponseParser.Parse(pl + "\n" + en);
        Assert.Equal("Tak zwany \"fitment\".\n\nDrugi akapit.", result[0].Body); Assert.Single(result[0].Sources);
    }
    [Fact] public void MissingEnglishAndTruncatedJsonGiveDistinctActionableErrors()
    {
        Assert.Contains("EN", Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse(JsonSerializer.Serialize(Text("PL")))).Message);
        Assert.Contains("urywa", Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse("{\"articles\":[{\"body\":\"Nieukończony tekst")).Message);
    }
    [Fact] public void MissingFieldNamesExactProblemInsteadOfCallingCompleteTextTruncated()
    {
        var response = "{\"articles\":[{\"title\":\"T\",\"body\":\"Treść\",\"language\":\"PL\"}]}";
        Assert.Contains("intro", Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse(response)).Message);
    }
    [Fact] public void MalformedJsonReportsPositionWithoutIncludingTheArticle()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse("{\"articles\": [1,]}"));
        Assert.Contains("tytuł", error.Message);
        var syntax = Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse("{\"articles\": [ {\"title\": \"sekretny tekst\" \"intro\": \"i\"} ]}"));
        Assert.Contains("linia", syntax.Message); Assert.DoesNotContain("sekretny tekst", syntax.Message);
    }
    [Fact] public void DuplicateLanguageAndIncompleteMarkedTextAreNotRepairedIntoArticles()
    {
        Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse(JsonSerializer.Serialize(new[] { Text("PL"), Text("PL") })));
        Assert.Contains("kompletnej", Assert.Throws<InvalidOperationException>(() => ChatGptResponseParser.Parse("[ARTYKUL_PL]\n[TYTUL]\nT\n[TEKST]\nUrwany tekst")).Message);
    }
}
