using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using WheelContentManager.Infrastructure;
namespace WheelContentManager.Desktop;
public sealed class MailSettingsWindow : Window
{
    private readonly IServiceProvider services;
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly PasswordBox password = new();
    private readonly ComboBox security = new() { ItemsSource = new[] { "StartTls", "SslOnConnect" } };
    private readonly CheckBox mail = new() { Content = "Wysyłaj w każdy piątek o 12:00 (czas polski)" };
    private readonly CheckBox background = new() { Content = "Przygotowuj 3 paczki w tle — po jednej na markę" };
    private readonly CheckBox autoStart = new() { Content = "Uruchamiaj program po zalogowaniu do Windows" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly StackPanel history = new();
    public MailSettingsWindow(IServiceProvider services)
    {
        this.services = services; Title = "Poczta i paczki tygodniowe"; Width = 620; Height = 850; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(24) }; Content = new ScrollViewer { Content = panel };
        panel.Children.Add(new TextBlock { Text = "Trzy paczki w każdy piątek o 12:00", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Mail zawiera ZIP z gotowymi poleceniami EN → PL i linkami do galerii. Komputer musi działać, użytkownik być zalogowany i mieć internet; pominięty termin zostanie nadrobiony. Zamknięcie okna chowa program do zasobnika.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "LH.pl: odczytaj nazwę serwera SMTP i ustawienia szyfrowania w panelu swojej usługi lub konfiguracji poczty. Nazwa serwera może zależeć od konta hostingowego.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        var help = new Button { Content = "Pomoc konfiguracji poczty LH.pl", HorizontalAlignment = HorizontalAlignment.Left };
        help.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.lh.pl/pomoc/") { UseShellExecute = true }); panel.Children.Add(help);
        foreach (var (key, label) in new[] { ("host", "Serwer SMTP — podaje dostawca poczty"), ("port", "Port (zwykle 587 dla STARTTLS lub 465 dla SSL/TLS)"), ("from", "Adres nadawcy"), ("to", "Adres odbiorcy"), ("user", "Login SMTP (zwykle pełny adres poczty)") })
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 0) }); var field = new TextBox(); fields[key] = field; panel.Children.Add(field);
        }
        fields["user"].LostFocus += (_, _) =>
        {
            var address = fields["user"].Text.Trim();
            if (!MailboxAddress.TryParse(address, out _)) return;
            if (string.IsNullOrWhiteSpace(fields["from"].Text)) fields["from"].Text = address;
            if (string.IsNullOrWhiteSpace(fields["to"].Text)) fields["to"].Text = address;
        };
        panel.Children.Add(new TextBlock { Text = "Szyfrowanie połączenia" }); panel.Children.Add(security);
        panel.Children.Add(new TextBlock { Text = "Hasło SMTP / hasło aplikacji — puste pole zachowuje zapisane hasło", Margin = new(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap }); panel.Children.Add(password);
        panel.Children.Add(new TextBlock { Text = "Hasło zostaje zaszyfrowane przez Windows dla Twojego konta. Gmail i część innych dostawców wymaga hasła aplikacji. Microsoft 365 może wymagać włączenia SMTP AUTH przez administratora.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) });
        panel.Children.Add(background); panel.Children.Add(mail); panel.Children.Add(autoStart);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        var save = new Button { Content = "Zapisz ustawienia" }; save.Click += async (_, _) => await ExecuteAsync(SaveAsync); buttons.Children.Add(save);
        var test = new Button { Content = "Wyślij wiadomość testową" }; test.Click += async (_, _) => await ExecuteAsync(async () => { await SaveAsync(); await services.GetRequiredService<NotificationService>().TestAsync(); status.Text = "Wiadomość testowa wysłana. Sprawdź skrzynkę i spam."; }); buttons.Children.Add(test);
        panel.Children.Add(status); panel.Children.Add(history);
        Loaded += async (_, _) => await ExecuteAsync(LoadAsync);
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        IsEnabled = false;
        try { await action(); }
        catch (Exception e) { status.Text = e is InvalidOperationException ? e.Message : "Nie udało się wykonać operacji: " + e.GetType().Name; }
        finally { IsEnabled = true; }
    }
    private async Task LoadAsync()
    {
        var s = await services.GetRequiredService<SettingsService>().LoadAsync();
        fields["host"].Text = s.SmtpHost; fields["port"].Text = s.SmtpPort.ToString(); fields["from"].Text = s.MailFrom; fields["to"].Text = s.MailTo; fields["user"].Text = s.SmtpUser;
        security.SelectedItem = s.SmtpSecurity; mail.IsChecked = s.WeeklyPromptMailEnabled; background.IsChecked = s.WeeklyPromptsEnabled; autoStart.IsChecked = s.StartWithWindows;
        history.Children.Clear();
        foreach (var batch in (await services.GetRequiredService<WeeklyPromptService>().BatchesAsync()).Take(6))
        {
            history.Children.Add(new TextBlock { Text = batch.Key + " — " + batch.Status + " (" + batch.MailState + ")", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
            if (batch.MailState is not ("Unknown" or "Sending")) continue;
            var received = new Button { Content = "Ten mail dotarł — potwierdź odbiór" };
            received.Click += async (_, _) => await ExecuteAsync(async () => { await services.GetRequiredService<WeeklyPromptService>().ResolveMailAsync(batch.Key, true); await LoadAsync(); }); history.Children.Add(received);
            var retry = new Button { Content = "Mail nie dotarł — zleć ponowienie (możliwy duplikat)" };
            retry.Click += async (_, _) => await ExecuteAsync(async () => { await services.GetRequiredService<WeeklyPromptService>().ResolveMailAsync(batch.Key, false); await LoadAsync(); }); history.Children.Add(retry);
        }
    }
    private async Task SaveAsync()
    {
        var settings = services.GetRequiredService<SettingsService>(); var s = await settings.LoadAsync();
        s.SmtpHost = fields["host"].Text.Trim(); s.MailFrom = fields["from"].Text.Trim(); s.MailTo = fields["to"].Text.Trim(); s.SmtpUser = fields["user"].Text.Trim();
        if (!int.TryParse(fields["port"].Text, out var port)) throw new InvalidOperationException("Wpisz prawidłowy port SMTP.");
        s.SmtpPort = port; s.SmtpSecurity = security.SelectedItem?.ToString() ?? "StartTls";
        s.WeeklyPromptMailEnabled = mail.IsChecked == true; s.WeeklyPromptsEnabled = background.IsChecked == true; s.StartWithWindows = autoStart.IsChecked == true;
        if (s.WeeklyPromptMailEnabled && (string.IsNullOrWhiteSpace(s.SmtpHost) || !MailboxAddress.TryParse(s.MailFrom, out _) || !MailboxAddress.TryParse(s.MailTo, out _))) throw new InvalidOperationException("Do wysyłki uzupełnij SMTP i poprawne adresy poczty.");
        s.Validate(); services.GetRequiredService<ISecretStore>().Save("smtp", password.Password); password.Clear();
        if (s.WeeklyPromptMailEnabled && !string.IsNullOrWhiteSpace(s.SmtpUser) && string.IsNullOrWhiteSpace(services.GetRequiredService<ISecretStore>().Read("smtp"))) throw new InvalidOperationException("Wpisz hasło SMTP / hasło aplikacji.");
        await settings.SaveAsync(s); WindowsScheduler.ApplyAutoStart(s.StartWithWindows, Environment.ProcessPath!);
        await services.GetRequiredService<WindowsScheduler>().ApplyPromptScheduleAsync(s.WeeklyPromptsEnabled);
        if (s.WeeklyPromptsEnabled) OperationSession.Resume(services.GetRequiredService<AppPaths>());
        status.Text = "Zapisano. Wysyłka: piątek 12:00 czasu polskiego. Paczki będą przygotowywane wcześniej w tle.";
    }
}
