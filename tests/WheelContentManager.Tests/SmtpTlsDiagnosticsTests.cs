using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Infrastructure;
using Xunit;
namespace WheelContentManager.Tests;
public class SmtpTlsDiagnosticsTests
{
    [Fact] public async Task InvalidCertificateIsExplainedAndRejectedBeforeAuthentication()
    {
        await using var env = await TestEnvironment.CreateAsync();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=mail.fixture.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("mail.fixture.example"); request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Schannel cannot use the ephemeral key created by CreateSelfSigned for server authentication.
        // Import a test-only PFX with the default persisted key storage; Dispose removes its key.
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var commands = new List<string>();
        var server = Task.Run(async () =>
        {
            try
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 fixture ESMTP"); commands.Add((await reader.ReadLineAsync(timeout.Token))!);
                await writer.WriteAsync("250-fixture\r\n250 STARTTLS\r\n"); await writer.FlushAsync(timeout.Token);
                commands.Add((await reader.ReadLineAsync(timeout.Token))!); await writer.WriteLineAsync("220 Ready for TLS");
                using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = serverCertificate, EnabledSslProtocols = SslProtocols.Tls12 }, timeout.Token);
                using var encryptedReader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                var command = await encryptedReader.ReadLineAsync(timeout.Token); if (command != null) commands.Add(command);
            }
            catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException or SocketException) { }
        });
        try
        {
            var settings = env.Services.GetRequiredService<SettingsService>(); var s = await settings.LoadAsync();
            s.SmtpHost = "localhost"; s.SmtpPort = ((IPEndPoint)listener.LocalEndpoint).Port; s.SmtpSecurity = "StartTls";
            s.SmtpUser = s.MailFrom = s.MailTo = "sender@fixture.example"; await settings.SaveAsync(s);
            env.Services.GetRequiredService<ISecretStore>().Save("smtp", "synthetic-secret-must-not-be-sent");
            var error = await Assert.ThrowsAsync<PromptMailNotSentException>(() => env.Services.GetRequiredService<NotificationService>().TestAsync(timeout.Token));
            Assert.Contains("mail.fixture.example", error.Message); Assert.Contains("RemoteCertificate", error.Message);
            Assert.DoesNotContain("synthetic-secret", error.Message);
            Assert.DoesNotContain("test-only-key", error.Message);
            await server; Assert.DoesNotContain(commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commands, command => command == "STARTTLS");
        }
        finally { timeout.Cancel(); listener.Stop(); await server; }
    }
}
