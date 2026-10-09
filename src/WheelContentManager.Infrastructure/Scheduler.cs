using System.Diagnostics;
using System.Security;
using WheelContentManager.Core;

namespace WheelContentManager.Infrastructure;

public sealed class WindowsScheduler(AppPaths paths)
{
    public static string TaskXml(AppSettings s, string worker, string user)
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var retry = s.MaxRetries > 0 ? $"<RestartOnFailure><Interval>PT30M</Interval><Count>{s.MaxRetries}</Count></RestartOnFailure>" : "";
        var start = Normalization.NextRun(s, DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss");
        static string Esc(string t) => SecurityElement.Escape(t)!;
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>WT - Blog Generator — przygotowanie treści PL/EN, bez publikacji.</Description></RegistrationInfo>
          <Triggers><CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled><ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek><{days[(int)s.ScheduleDay]}/></DaysOfWeek></ScheduleByWeek></CalendarTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>{Esc(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT3H</ExecutionTimeLimit><Enabled>true</Enabled>{retry}</Settings>
          <Actions Context="Author"><Exec><Command>{Esc(worker)}</Command><Arguments>--run-weekly</Arguments><WorkingDirectory>{Esc(Path.GetDirectoryName(worker)!)}</WorkingDirectory></Exec></Actions>
        </Task>
        """;
    }
    public static string BackgroundTaskXml(AppSettings s, string worker, string user)
    {
        static string Esc(string value) => SecurityElement.Escape(value)!;
        var boundary = DateTime.Today.AddHours(6).ToString("yyyy-MM-ddTHH:mm:ss");
        var retry = s.MaxRetries > 0 ? $"<RestartOnFailure><Interval>PT30M</Interval><Count>{s.MaxRetries}</Count></RestartOnFailure>" : "";
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>WT - Blog Generator — utrzymanie 3 gotowych artykułów, bez publikacji.</Description></RegistrationInfo>
          <Triggers>
            <LogonTrigger><Enabled>true</Enabled><UserId>{Esc(user)}</UserId><Delay>PT1M</Delay></LogonTrigger>
            <CalendarTrigger><Repetition><Interval>PT2H</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>{boundary}</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>
          </Triggers>
          <Principals><Principal id="Author"><UserId>{Esc(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT3H</ExecutionTimeLimit><Enabled>true</Enabled>{retry}</Settings>
          <Actions Context="Author"><Exec><Command>{Esc(worker)}</Command><Arguments>--prepare-stock</Arguments><WorkingDirectory>{Esc(Path.GetDirectoryName(worker)!)}</WorkingDirectory></Exec></Actions>
        </Task>
        """;
    }
    public async Task ApplyBackgroundAsync(AppSettings s, bool startNow = false, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Przygotowanie w tle wymaga Harmonogramu Windows.");
        var name = "WheelContentManager-Prepare-" + Environment.UserName;
        if (!s.BackgroundPreparationEnabled)
        {
            if (await RunAsync(["/Query", "/TN", name], ct, true) == 0) await RunAsync(["/Change", "/TN", name, "/DISABLE"], ct);
            return;
        }
        if (!s.SetupCompleted || string.IsNullOrWhiteSpace(s.AiModel) || s.InputPricePerMillion <= 0 || s.OutputPricePerMillion <= 0) return;
        var worker = Path.Combine(AppContext.BaseDirectory, "Worker", "WheelContentManager.Worker.exe");
        if (!File.Exists(worker)) throw new InvalidOperationException("Brak Workera w paczce. Zainstaluj pełną paczkę WT - Blog Generator.");
        var xml = Path.Combine(paths.Root, "prepare-schedule.xml");
        await File.WriteAllTextAsync(xml, BackgroundTaskXml(s, worker, System.Security.Principal.WindowsIdentity.GetCurrent().Name), System.Text.Encoding.Unicode, ct);
        await RunAsync(["/Create", "/TN", name, "/XML", xml, "/F"], ct);
        if (startNow) await RunAsync(["/Run", "/TN", name], ct);
    }
    private static async Task<int> RunAsync(IEnumerable<string> args, CancellationToken ct, bool allowFailure = false)
    {
        var p = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) p.ArgumentList.Add(arg);
        using var process = Process.Start(p)!;
        var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct); await process.WaitForExitAsync(ct);
        await output; await error; if (process.ExitCode != 0 && !allowFailure) throw new InvalidOperationException($"Harmonogram Windows: błąd {process.ExitCode}. Sprawdź uprawnienia konta i dostępność Harmonogramu zadań.");
        return process.ExitCode;
    }
    public async Task ApplyAsync(AppSettings s, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Harmonogram zadań jest dostępny na Windows.");
        var name = "WheelContentManager-" + Environment.UserName;
        if (!s.ScheduleEnabled) { if (await RunAsync(["/Query", "/TN", name], ct, true) == 0) await RunAsync(["/Change", "/TN", name, "/DISABLE"], ct); return; }
        var worker = Path.Combine(AppContext.BaseDirectory, "Worker", "WheelContentManager.Worker.exe");
        if (!File.Exists(worker)) throw new InvalidOperationException("Brak Worker/WheelContentManager.Worker.exe w paczce aplikacji. Użyj pełnej paczki Windows.");
        var path = Path.Combine(paths.Root, "schedule.xml");
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        await File.WriteAllTextAsync(path, TaskXml(s, worker, user), System.Text.Encoding.Unicode, ct);
        await RunAsync(["/Create", "/TN", name, "/XML", path, "/F"], ct);
    }
}
