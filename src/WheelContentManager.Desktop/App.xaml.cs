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
    private System.Windows.Threading.DispatcherTimer? weeklyTimer;
    private System.Windows.Forms.NotifyIcon? tray;
    private CancellationTokenSource? background;
    private bool polling, exiting;
    private DateTimeOffset retryAfter;
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
            await Bootstrap.InitializeAsync(host.Services);
            var settingsService = host.Services.GetRequiredService<SettingsService>(); var settings = await settingsService.LoadAsync();
            settings.UseChatGpt = true; settings.BackgroundPreparationEnabled = false; settings.ScheduleEnabled = false; settings.SetupCompleted = true;
            if (!settings.PromptAutomationConfigured)
            {
                settings.PromptAutomationConfigured = true; settings.WeeklyPromptsEnabled = true; settings.StartWithWindows = true; settings.WeeklyPromptEnabledAt = DateTimeOffset.UtcNow;
                OperationSession.Resume(host.Services.GetRequiredService<AppPaths>());
            }
            await settingsService.SaveAsync(settings);
            WindowsScheduler.ApplyAutoStart(settings.StartWithWindows, Environment.ProcessPath!);
            var scheduler = host.Services.GetRequiredService<WindowsScheduler>();
            try { await scheduler.ApplyBackgroundAsync(settings); await scheduler.ApplyAsync(settings); } catch { /* no scheduled work is required by this app */ }
            viewModel = new MainViewModel(host.Services); await viewModel.InitializeAsync();
            MainWindow = new MainWindow(viewModel);
            MainWindow.Closing += (_, closing) => { if (!exiting) { closing.Cancel = true; MainWindow.Hide(); } };
            if (!e.Args.Contains("--background")) MainWindow.Show();
            viewModel.CancelBackgroundRequested += () => background?.Cancel();
            viewModel.ResumeBackgroundRequested += () => retryAfter = DateTimeOffset.MinValue;
            tray = new() { Text = "WT - Blog Generator — paczki tygodniowe", Icon = System.Drawing.SystemIcons.Application, Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Otwórz program", null, (_, _) => ShowProgram());
            menu.Items.Add("Wznów przygotowanie", null, async (_, _) => { OperationSession.Resume(host.Services.GetRequiredService<AppPaths>()); retryAfter = DateTimeOffset.MinValue; await PollWeeklyAsync(); });
            menu.Items.Add("Wstrzymaj i zakończ", null, (_, _) => { OperationSession.RequestCancellation(host.Services.GetRequiredService<AppPaths>()); exiting = true; Shutdown(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowProgram();
            try { await scheduler.ApplyPromptScheduleAsync(settings.WeeklyPromptsEnabled); }
            catch (Exception ex) { viewModel.WeeklyStatus = "Harmonogram Windows niedostępny: " + ex.GetType().Name + ". Przygotowanie działa, gdy program jest włączony."; }
            activation = new() { Interval = TimeSpan.FromMilliseconds(500) };
            activation.Tick += (_, _) =>
            {
                if (showWindow.WaitOne(0)) ShowProgram();
                var state = OperationSession.Read(host.Services.GetRequiredService<AppPaths>());
                viewModel.BackgroundBusy = polling || state != null;
                if (state != null && !viewModel.Busy) viewModel.WeeklyStatus = state.Progress;
            }; activation.Start();
            weeklyTimer = new() { Interval = TimeSpan.FromSeconds(30) }; weeklyTimer.Tick += async (_, _) => await PollWeeklyAsync(); weeklyTimer.Start();
            await viewModel.LoadWeeklyAsync(); await PollWeeklyAsync();
        }
        catch (Exception ex) { MessageBox.Show("Nie udało się uruchomić programu: " + ex.GetType().Name + ". Sprawdź instalację i dostęp do AppData.", "WT - Blog Generator", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    private void ShowProgram() { MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); }
    private async Task PollWeeklyAsync()
    {
        if (polling || host == null || viewModel == null || viewModel.Busy || exiting || DateTimeOffset.UtcNow < retryAfter) return;
        polling = true; background = new(); viewModel.BackgroundBusy = true;
        try
        {
            var progress = new Progress<string>(message => viewModel.WeeklyStatus = message);
            viewModel.WeeklyStatus = await Task.Run(() => host.Services.GetRequiredService<WeeklyPromptService>().TickAsync(DateTimeOffset.UtcNow, progress, background.Token));
            await viewModel.LoadWeeklyAsync();
        }
        catch (OperationBusyException) { viewModel.WeeklyStatus = "Trwa inna operacja; przygotowanie zostanie wznowione automatycznie."; }
        catch (OperationCanceledException) { viewModel.WeeklyStatus = "Przygotowanie wstrzymane — kliknij Wznów."; }
        catch (Exception e) { retryAfter = DateTimeOffset.UtcNow.AddMinutes(15); viewModel.WeeklyStatus = (e is InvalidOperationException ? e.Message : "Błąd przygotowania: " + e.GetType().Name) + " Ponowienie za 15 minut lub po kliknięciu Wznów."; }
        finally { background.Dispose(); background = null; polling = false; viewModel.BackgroundBusy = false; }
    }
    protected override void OnExit(ExitEventArgs e) { exiting = true; weeklyTimer?.Stop(); activation?.Stop(); background?.Cancel(); tray?.Dispose(); viewModel?.StopUpdates(); showWindow?.Dispose(); instance?.Dispose(); host?.Dispose(); base.OnExit(e); }
}
