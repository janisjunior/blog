using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace WheelContentManager.Core;

public static class ChatGptResponseParser
{
    public static IReadOnlyList<AiArticle> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) throw new InvalidOperationException("Wklej odpowiedź z ChatGPT.");
        var articles = new List<AiArticle>();
        if (response.Contains("[ARTYKUL_PL]", StringComparison.OrdinalIgnoreCase) || response.Contains("[ARTYKUL_EN]", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var language in new[] { "PL", "EN" })
            {
                var pattern = @"\[ARTYKUL_" + language + @"\]\s*\[TYTUL\](?<title>.*?)\[INTRO\](?<intro>.*?)\[TEKST\](?<body>.*?)\[ZRODLA\](?<sources>.*?)\[KONIEC_ARTYKUL_" + language + @"\]";
                var match = Regex.Match(response, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (!match.Success) throw new InvalidOperationException($"Brakuje kompletnej wersji {language}. Wklej cały artykuł od [ARTYKUL_{language}] do [KONIEC_ARTYKUL_{language}], z polami TYTUL, INTRO, TEKST i ZRODLA.");
                var sources = Regex.Matches(match.Groups["sources"].Value, @"https://[^\s<>]+")
                    .Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']')).Distinct().ToArray();
                articles.Add(new(match.Groups["title"].Value.Trim(), match.Groups["intro"].Value.Trim(), match.Groups["body"].Value.Trim(), language, sources, []));
            }
        }
        else
        {
            var documents = ExtractDocuments(response);
            if (documents.Count == 0) throw new InvalidOperationException("Nie rozpoznano artykułów. Wklej całą odpowiedź z oznaczeniami [ARTYKUL_PL] i [ARTYKUL_EN] albo wcześniejszy JSON.");
            foreach (var document in documents)
            {
                try
                {
                    using var json = JsonDocument.Parse(EscapeLineBreaks(document), new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    Read(json.RootElement, articles);
                }
                catch (JsonException e)
                {
                    throw new InvalidOperationException($"Niepoprawny zapis odpowiedzi: linia {(e.LineNumber ?? 0) + 1}, pozycja {(e.BytePositionInLine ?? 0) + 1}. Sprawdź cudzysłowy i przecinki w tym miejscu. Nie usuwaj tekstu; możesz poprosić ChatGPT o zwrócenie go w formacie oznaczonych artykułów [ARTYKUL_PL] i [ARTYKUL_EN].", e);
                }
            }
        }
        if (articles.Count != 2 || articles.Count(a => a.Language == "PL") != 1 || articles.Count(a => a.Language == "EN") != 1)
        {
            var found = string.Join(", ", articles.Select(a => a.Language));
            throw new InvalidOperationException($"Odczytano wersje: {(found.Length == 0 ? "żadnej" : found)}. Potrzebny jest dokładnie jeden pełny artykuł PL i jeden EN. Dołącz brakującą wersję; program nie dopisuje urwanych tekstów.");
        }
        return articles;
    }
    private static void Read(JsonElement root, List<AiArticle> output, string? impliedLanguage = null)
    {
        if (root.ValueKind == JsonValueKind.Array) { foreach (var entry in root.EnumerateArray()) Read(entry, output); return; }
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Artykuł musi zawierać tytuł, intro i tekst.");
        if (Property(root, "articles", "artykuly", "artykuły") is { } list) { Read(list, output); return; }
        if (Property(root, "PL") is { } pl && Property(root, "EN") is { } en) { Read(pl, output, "PL"); Read(en, output, "EN"); return; }
        string Text(params string[] names)
        {
            var value = Property(root, names);
            if (value is { ValueKind: JsonValueKind.String }) return value.Value.GetString()!;
            if (value is { ValueKind: JsonValueKind.Array } && value.Value.EnumerateArray().All(p => p.ValueKind == JsonValueKind.String)) return string.Join("\n\n", value.Value.EnumerateArray().Select(p => p.GetString()));
            throw new InvalidOperationException($"W odpowiedzi brakuje pola {names[0]} albo ma ono niewłaściwy format. Nie musisz generować artykułu ponownie — poproś ChatGPT o dodanie brakującego pola.");
        }
        string[] List(params string[] names)
        {
            var value = Property(root, names);
            if (value == null || value.Value.ValueKind == JsonValueKind.Null) return [];
            if (value.Value.ValueKind == JsonValueKind.Array && value.Value.EnumerateArray().All(p => p.ValueKind == JsonValueKind.String)) return value.Value.EnumerateArray().Select(p => p.GetString()!).ToArray();
            if (value.Value.ValueKind == JsonValueKind.String) return names[0] == "sources" ? Regex.Matches(value.Value.GetString()!, @"https://[^\s<>]+").Select(m => m.Value).ToArray() : [value.Value.GetString()!];
            throw new InvalidOperationException($"Pole {names[0]} powinno być listą.");
        }
        var language = impliedLanguage ?? Text("language", "lang", "jezyk", "język");
        language = language.Trim().ToUpperInvariant() switch { "POLSKI" or "POLISH" or "PL" => "PL", "ENGLISH" or "ANGIELSKI" or "EN" => "EN", _ => language.Trim().ToUpperInvariant() };
        output.Add(new(Text("title", "tytul", "tytuł"), Text("intro", "wstep", "wstęp"), Text("body", "text", "content", "tresc", "treść").Replace("\\n", "\n"), language, List("sources", "zrodla", "źródła"), List("warnings", "uwagi")));
    }
    private static JsonElement? Property(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject()) if (names.Any(n => n.Equals(property.Name, StringComparison.OrdinalIgnoreCase))) return property.Value;
        return null;
    }
    private static List<string> ExtractDocuments(string response)
    {
        var result = new List<string>(); var start = -1; var depth = 0; var quoted = false; var escaped = false;
        for (var i = 0; i < response.Length; i++)
        {
            var c = response[i];
            if (start < 0) { if (c is '{' or '[') { start = i; depth = 1; } continue; }
            if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
            if (c == '"') quoted = true;
            else if (c is '{' or '[') depth++;
            else if (c is '}' or ']')
            {
                if (--depth == 0) { result.Add(response[start..(i + 1)]); start = -1; }
            }
        }
        if (start >= 0) throw new InvalidOperationException("Odpowiedź urywa się przed końcem artykułów lub JSON. Poproś ChatGPT o kontynuację od ostatniego zdania i dołącz ją do obecnego tekstu. Program nie zgaduje brakujących zakończeń.");
        return result;
    }
    private static string EscapeLineBreaks(string json)
    {
        json = Regex.Replace(json, @"(?<!\\)(https?)\\:(?=//)", "$1:", RegexOptions.IgnoreCase);
        var result = new StringBuilder(); var quoted = false; var escaped = false;
        foreach (var c in json)
        {
            if (quoted && c is '\r' or '\n' or '\t') { result.Append(c == '\r' ? "\\r" : c == '\n' ? "\\n" : "\\t"); escaped = false; continue; }
            result.Append(c);
            if (escaped) { escaped = false; continue; }
            if (quoted && c == '\\') escaped = true;
            else if (c == '"') quoted = !quoted;
        }
        return result.ToString();
    }
}
