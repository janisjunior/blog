using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
namespace WheelContentManager.Infrastructure;
public sealed record MailSettingsPreset(string SmtpHost, int SmtpPort, string SmtpSecurity, string SmtpUser, string MailFrom, string MailTo,
    bool WeeklyPromptMailEnabled = true, bool WeeklyPromptsEnabled = true, bool StartWithWindows = true)
{
    public static async Task<MailSettingsPreset> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var input = File.OpenRead(path);
        if (input.Length > 65536) throw new InvalidOperationException("Plik ustawień poczty jest zbyt duży.");
        MailSettingsPreset? preset;
        try { preset = await JsonSerializer.DeserializeAsync<MailSettingsPreset>(input, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }, ct); }
        catch (JsonException) { throw new InvalidOperationException("Niepoprawny plik ustawień poczty. Plik nie może zawierać hasła ani dodatkowych ustawień programu."); }
        if (preset == null || string.IsNullOrWhiteSpace(preset.SmtpHost) || Uri.CheckHostName(preset.SmtpHost) != UriHostNameType.Dns ||
            preset.SmtpPort is < 1 or > 65535 || preset.SmtpSecurity is not ("StartTls" or "SslOnConnect") ||
            string.IsNullOrWhiteSpace(preset.SmtpUser) || string.IsNullOrWhiteSpace(preset.MailFrom) || string.IsNullOrWhiteSpace(preset.MailTo) ||
            !MailboxAddress.TryParse(preset.MailFrom, out _) || !MailboxAddress.TryParse(preset.MailTo, out _))
            throw new InvalidOperationException("Plik nie zawiera poprawnego SMTP, TLS, loginu i adresów poczty.");
        return preset;
    }
}
