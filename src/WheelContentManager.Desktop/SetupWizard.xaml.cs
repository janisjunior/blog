using System.Windows;
using System.Windows.Controls;
namespace WheelContentManager.Desktop;
public partial class SetupWizard : Window
{
    private readonly MainViewModel vm;
    public SetupWizard(MainViewModel vm) { this.vm = vm; InitializeComponent(); DataContext = vm; }
    private void ApiKeyChanged(object sender, RoutedEventArgs e) => vm.ApiKey = ((PasswordBox)sender).Password;
    private void SmtpPasswordChanged(object sender, RoutedEventArgs e) => vm.SmtpPassword = ((PasswordBox)sender).Password;
    private void Later(object sender, RoutedEventArgs e) => Close();
    private void Back(object sender, RoutedEventArgs e) { Steps.SelectedIndex--; UpdateButtons(); }
    private async void Next(object sender, RoutedEventArgs e)
    {
        if (Steps.SelectedIndex < 3) { Steps.SelectedIndex++; UpdateButtons(); return; }
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        if (vm.Status.StartsWith("Ustawienia zapisane")) Close();
    }
    private void UpdateButtons() { StepLabel.Text = $"Krok {Steps.SelectedIndex + 1} z 4"; BackButton.IsEnabled = Steps.SelectedIndex > 0; NextButton.Content = Steps.SelectedIndex == 3 ? "Zapisz i otwórz aplikację" : "Dalej"; }
}
