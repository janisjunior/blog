using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using WheelContentManager.Core;
namespace WheelContentManager.Infrastructure;
public static class SmtpTlsDiagnostics
{
    public static string Describe(AppSettings settings, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        var text = $"Nie ustanowiono bezpiecznego TLS z {settings.SmtpHost}:{settings.SmtpPort} ({settings.SmtpSecurity}).\nWeryfikacja certyfikatu: {errors}.";
        if (certificate != null)
        {
            using var cert = new X509Certificate2(certificate);
            text += "\nNazwa DNS w certyfikacie: " + cert.GetNameInfo(X509NameType.DnsName, false) + "\nWystawca: " + cert.Issuer +
                "\nWażność (UTC): " + cert.NotBefore.ToUniversalTime().ToString("yyyy-MM-dd") + " — " + cert.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd");
        }
        if (chain != null) foreach (var status in chain.ChainStatus) text += "\nŁańcuch: " + status.Status + " — " + status.StatusInformation.Trim();
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)) text += "\nCertyfikat nie pasuje do wpisanego adresu SMTP. Sprawdź oficjalną nazwę serwera w panelu LH.pl.";
        else if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)) text += "\nSprawdź datę komputera i zaufanie do wystawcy; dostawca poczty powinien potwierdzić prawidłowy łańcuch certyfikatu.";
        return text + "\nLogowanie nie zostało rozpoczęte. Sprawdzanie certyfikatu pozostaje włączone.";
    }
}
