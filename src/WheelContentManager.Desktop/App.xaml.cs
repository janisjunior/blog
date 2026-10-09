using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WheelContentManager.Infrastructure;
namespace WheelContentManager.Desktop;
public partial class App : Application
{
    private IHost? host;
    private MainViewModel? viewModel;
    private System.Windows.Forms.NotifyIcon? tray;
    private Mutex? instance;
    private EventWaitHandle? showWindow;
    private System.Windows.Threading.DispatcherTimer? activationTimer;
    private bool exiting;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        instance = new Mutex(true, "Local\\WT-Blog-Generator-" + identity, out var first);
        showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WT-Blog-Generator-Open-" + identity);
        if (!first) { if (!e.Args.Contains("--background")) showWindow.Set(); Shutdown(); return; }
        try
        {
            host = Bootstrap.Create(e.Args); await Bootstrap.InitializeAsync(host.Services);
            var vm = new MainViewModel(host.Services); viewModel = vm; await vm.InitializeAsync();
            MainWindow = new MainWindow(vm);
            MainWindow.Closing += (_, closing) => { if (!exiting) { closing.Cancel = true; MainWindow.Hide(); } };
            tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "WT - Blog Generator", Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Otwórz program", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
            menu.Items.Add("Anuluj operację i wstrzymaj przygotowanie", null, (_, _) => Dispatcher.Invoke(() => vm.CancelCommand.Execute(null)));
            menu.Items.Add("Zakończ i wstrzymaj przygotowanie", null, (_, _) => Dispatcher.Invoke(() => { vm.CancelCommand.Execute(null); exiting = true; Shutdown(); }));
            tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
            vm.PreparedSetCompleted += () => tray.ShowBalloonTip(10000, "Artykuły gotowe", "Przygotowano zestaw 3 artykułów PL + EN. Otwórz WT - Blog Generator, aby je przeczytać.", System.Windows.Forms.ToolTipIcon.Info);
            activationTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
            activationTimer.Tick += (_, _) => { if (showWindow.WaitOne(0)) ShowMainWindow(); }; activationTimer.Start();
            if (!e.Args.Contains("--background") || !vm.Settings.SetupCompleted) ShowMainWindow();
            if (!vm.Settings.SetupCompleted) new SetupWizard(vm) { Owner = MainWindow }.ShowDialog();
            else await vm.ConfigureBackgroundAsync();
        }
        catch (Exception ex) { MessageBox.Show("Nie udało się uruchomić aplikacji: " + ex.GetType().Name + ". Sprawdź prawa zapisu do AppData i instalację Windows.", "WT - Blog Generator", MessageBoxButton.OK, MessageBoxImage.Error); exiting = true; Shutdown(1); }
    }
    private void ShowMainWindow() { MainWindow.Show(); if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); }
    protected override void OnExit(ExitEventArgs e)
    {
        activationTimer?.Stop(); viewModel?.StopUpdates(); tray?.Dispose(); showWindow?.Dispose(); instance?.Dispose(); host?.Dispose(); base.OnExit(e);
    }
}
