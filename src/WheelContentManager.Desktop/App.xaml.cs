using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WheelContentManager.Infrastructure;
namespace WheelContentManager.Desktop;
public partial class App : Application
{
    private IHost? host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            host = Bootstrap.Create(e.Args); await Bootstrap.InitializeAsync(host.Services);
            var vm = new MainViewModel(host.Services); await vm.InitializeAsync();
            MainWindow = new MainWindow(vm); MainWindow.Show(); ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (!vm.Settings.SetupCompleted) new SetupWizard(vm) { Owner = MainWindow }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show("Nie udało się uruchomić aplikacji: " + ex.GetType().Name + ". Sprawdź prawa zapisu do AppData i instalację Windows.", "Wheel Content Manager", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { host?.Dispose(); base.OnExit(e); }
}
