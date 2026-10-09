using System.Text.Json;

namespace WheelContentManager.Infrastructure;

public sealed class OperationBusyException() : InvalidOperationException("Trwa operacja w tle. Możesz sprawdzić postęp lub ją anulować.");
public sealed record OperationState(string Id, string Label, string Progress, DateTimeOffset Started);

// The file lock remains authoritative; metadata and cancellation are scoped to its unique owner.
public sealed class OperationSession : IDisposable
{
    private readonly OperationLock gate;
    private readonly AppPaths paths;
    private readonly CancellationTokenSource cancellation;
    private readonly Timer timer;
    private readonly object sync = new();
    private OperationState state;
    private bool disposed;
    public CancellationToken Token => cancellation.Token;
    private static string StateFile(AppPaths p) => Path.Combine(p.Root, "operation-state.json");
    private static string CancelFile(AppPaths p) => Path.Combine(p.Root, "operation-cancel.txt");
    private static string PauseFile(AppPaths p) => Path.Combine(p.Root, "background.paused");
    private OperationSession(AppPaths paths, string label, CancellationToken ct)
    {
        this.paths = paths; gate = OperationLock.Acquire(Path.Combine(paths.Root, "operation.lock"));
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        state = new(Guid.NewGuid().ToString("N"), label, label, DateTimeOffset.UtcNow);
        try { WriteState(); }
        catch { cancellation.Dispose(); gate.Dispose(); throw; }
        timer = new Timer(_ => PollCancellation(), null, 0, 250);
    }
    public static OperationSession Start(AppPaths paths, string label, CancellationToken ct) => new(paths, label, ct);
    public static bool Paused(AppPaths paths) => File.Exists(PauseFile(paths));
    public static void Resume(AppPaths paths) => File.Delete(PauseFile(paths));
    public static bool IsRunning(AppPaths paths)
    {
        try { using var check = OperationLock.Acquire(Path.Combine(paths.Root, "operation.lock")); return false; }
        catch (OperationBusyException) { return true; }
    }
    public static OperationState? Read(AppPaths paths)
    {
        if (!IsRunning(paths)) return null;
        try { using var stream = new FileStream(StateFile(paths), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return JsonSerializer.Deserialize<OperationState>(stream); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }
    public static bool RequestCancellation(AppPaths paths, bool pauseBackground = true)
    {
        if (pauseBackground) File.WriteAllText(PauseFile(paths), "Przygotowanie w tle wstrzymano przez użytkownika.");
        var running = Read(paths);
        if (running == null) return false;
        File.WriteAllText(CancelFile(paths), running.Id); return true;
    }
    public IProgress<string> Progress(IProgress<string>? target) => new ImmediateProgress(value =>
    {
        lock (sync)
        {
            if (disposed) return;
            state = state with { Progress = value }; WriteState(); target?.Report(value);
        }
    });
    private sealed class ImmediateProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
    private void WriteState()
    {
        var temporary = StateFile(paths) + "." + state.Id + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, StateFile(paths), true);
    }
    private void PollCancellation()
    {
        lock (sync)
        {
            if (disposed) return;
            try { if (File.Exists(CancelFile(paths)) && File.ReadAllText(CancelFile(paths)) == state.Id) cancellation.Cancel(); }
            catch (IOException) { /* retry a concurrently written request */ }
        }
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return; disposed = true; timer.Dispose();
            try { File.Delete(StateFile(paths)); File.Delete(CancelFile(paths)); }
            finally { gate.Dispose(); cancellation.Dispose(); }
        }
    }
}
