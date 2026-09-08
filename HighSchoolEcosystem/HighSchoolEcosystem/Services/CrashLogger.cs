using System.Diagnostics;

namespace HighSchoolEcosystem.Services;

/// <summary>
/// Last-resort logging for exceptions that escape normal handling — chiefly
/// `async void` event handlers (page lifecycle overrides, MAUI click handlers),
/// where a fault has nowhere to propagate.
///
/// This writes to the debug output and a capped on-device log file only. It is
/// NOT a substitute for a crash-reporting service: nothing here reaches you once
/// the app is on someone else's phone. Wire up Crashlytics / Sentry / App Center
/// before a real rollout — see PRODUCTION.md.
/// </summary>
public static class CrashLogger
{
    private const long MaxLogBytes = 256 * 1024;

    private static readonly object FileLock = new();
    private static int _registered;

    public static void Register()
    {
        // Guarded because CreateMauiApp can run more than once in some hosts;
        // double-subscribing would duplicate every entry.
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("UnobservedTaskException", e.Exception);
            // Marks the fault handled. By default .NET wouldn't tear the process
            // down for this anyway, but it keeps the behaviour explicit and stops
            // it escalating if ThrowUnobservedTaskExceptions is ever turned on.
            e.SetObserved();
        };
    }

    public static void Log(string source, Exception? exception)
    {
        var message = $"[{DateTimeOffset.UtcNow:O}] {source}: {exception}";
        Debug.WriteLine(message);

        try
        {
            // AppDataDirectory is per-app private storage on every target.
            var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
            lock (FileLock)
            {
                // Truncate rather than grow without bound — this file is never
                // uploaded or rotated, so an error loop could otherwise fill the
                // user's storage.
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                {
                    File.Delete(path);
                }
                File.AppendAllText(path, message + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never itself throw — we may already be unwinding.
        }
    }
}
