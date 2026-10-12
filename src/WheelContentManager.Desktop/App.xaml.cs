using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WheelContentManager.Infrastructure;
namespace WheelContentManager.Desktop;
public partial class App : Application
{
    private IHost? host;
    private MainViewModel? viewModel;
    private Mutex? instance;
    private EventWaitHandle? showWindow;
    private System.Windows.Threading.DispatcherTimer? activation;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            instance = new Mutex(true, "Local\\WT-Blog-Generator-" + user, out var first);
            showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WT-Blog-Generator-Open-" + user);
            if (!first) { if (!e.Args.Contains("--background")) showWindow.Set(); Shutdown(); return; }
            host = Bootstrap.Create(e.Args);
            OperationSession.RequestCancellation(host.Services.GetRequiredService<AppPaths>());
            await Bootstrap.InitializeAsync(host.Services);
            var settingsService = host.Services.GetRequiredService<SettingsService>(); var settings = await settingsService.LoadAsync();
            settings.UseChatGpt = true; settings.BackgroundPreparationEnabled = false; settings.ScheduleEnabled = false; settings.StartWithWindows = false; settings.SetupCompleted = true;
            await settingsService.SaveAsync(settings);
            WindowsScheduler.ApplyAutoStart(false, Environment.ProcessPath!);
            var scheduler = host.Services.GetRequiredService<WindowsScheduler>();
            try { await scheduler.ApplyBackgroundAsync(settings); await scheduler.ApplyAsync(settings); } catch { /* no scheduled work is required by this app */ }
            if (e.Args.Contains("--background")) { Shutdown(); return; }
            viewModel = new MainViewModel(host.Services); await viewModel.InitializeAsync();
            MainWindow = new MainWindow(viewModel); MainWindow.Show(); ShutdownMode = ShutdownMode.OnMainWindowClose;
            activation = new() { Interval = TimeSpan.FromMilliseconds(500) };
            activation.Tick += (_, _) => { if (showWindow.WaitOne(0)) { MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); } }; activation.Start();
            await viewModel.InitialCheckAsync();
        }
        catch (Exception ex) { MessageBox.Show("Nie udało się uruchomić programu: " + ex.GetType().Name + ". Sprawdź instalację i dostęp do AppData.", "WT - Blog Generator", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { activation?.Stop(); viewModel?.StopUpdates(); showWindow?.Dispose(); instance?.Dispose(); host?.Dispose(); base.OnExit(e); }
}
