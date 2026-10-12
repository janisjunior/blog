using System.Text.Json;
using WheelContentManager.Infrastructure;
using Xunit;
namespace WheelContentManager.Tests;
public class MailSettingsPresetTests
{
    [Fact] public async Task LoadsCompletePresetWithoutPasswordAndKeepsAutomationFlags()
    {
        var file = Path.GetTempFileName();
        try
        {
            var expected = new MailSettingsPreset("smtp.fixture.example", 587, "StartTls", "sender@fixture.example", "sender@fixture.example", "recipient@fixture.example");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(expected));
            Assert.Equal(expected, await MailSettingsPreset.LoadAsync(file));
        }
        finally { File.Delete(file); }
    }
    [Theory][InlineData("Password", "secret")][InlineData("SmtpSecurity", "None")][InlineData("SmtpHost", "https://smtp.fixture.example")]
    public async Task RejectsPasswordsUnknownFieldsAndInvalidConnectionSettings(string key, string value)
    {
        var file = Path.GetTempFileName();
        try
        {
            var preset = new Dictionary<string, object> { ["SmtpHost"] = "smtp.fixture.example", ["SmtpPort"] = 587, ["SmtpSecurity"] = "StartTls", ["SmtpUser"] = "sender@fixture.example", ["MailFrom"] = "sender@fixture.example", ["MailTo"] = "recipient@fixture.example" };
            preset[key] = value; await File.WriteAllTextAsync(file, JsonSerializer.Serialize(preset));
            await Assert.ThrowsAsync<InvalidOperationException>(() => MailSettingsPreset.LoadAsync(file));
        }
        finally { File.Delete(file); }
    }
}
