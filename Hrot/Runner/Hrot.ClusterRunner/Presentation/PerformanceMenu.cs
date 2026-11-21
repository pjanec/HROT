using System.Globalization;
using Fdp.Core.Jobs;
using Fdp.Presentation.WindowManager;
using Hrot.ClusterRunner.Configuration;

namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// ⭐⭐ U-2 (CE-4531, R-4224) — <c>View ▸ Settings ▸ Performance</c>: the threaded editor, the parallel simulation and the worker threads, saved
/// in the user folder (<see cref="PerformanceSettings.FileName"/>) and read by EVERY HROT process at its next start
/// (<see cref="PerformanceStartup"/>). 🔒 User, 2026-11-13: "switchable ON/OFF easily … in the View -> Settings …, savable to user folder".
/// A choice applies on restart; the status line says when the saved choice differs from what runs.
/// </summary>
internal static class PerformanceMenu
{
    /// <summary>The worker-thread choices (0 = auto: cores − 2).</summary>
    internal static readonly int[] ThreadChoices = { 0, 1, 2, 4, 6, 8, 12, 16 };

    private static PerformanceSettings? _saved;   // the file, read once and kept in step with every save (no file read per frame)

    /// <summary>Tests: forget the cached file.</summary>
    internal static void Forget() => _saved = null;

    internal static PerformanceSettings Saved => _saved ??= PerformanceSettings.Load(PerformanceStartup.UserPath);

    internal static void Save(PerformanceSettings settings)
    {
        _saved = settings;
        var path = PerformanceStartup.UserPath;
        if (path != null) settings.Save(path);
    }

    /// <summary>The status line: what runs, and whether a restart would change it.</summary>
    internal static string Status()
    {
        var running = PerformanceStartup.Running;
        var next = PerformanceSettings.Resolve(Saved, System.Environment.GetEnvironmentVariable, PerformanceStartup.SerialMode, PerformanceStartup.UiHost);
        string now = PerformanceSettings.Describe(in running).Replace("[Performance] ", "Running: ");
        return next == running ? now : now + "  —  restart to apply the saved choice";
    }

    public static void Register(WindowManager wm)
    {
        string root = GlobalMenuRegistry.SettingsMenuPath + "/Performance";
        MenuCommandAdapter.RegisterAction(wm.GlobalMenu, wm.ShellCommands, "settings.performance.status", root + "/Status", () => { },
            enabled: () => false, dynamicLabel: Status);
        wm.GlobalMenu.RegisterSeparator(root + "/sep_status");
        MenuCommandAdapter.RegisterToggle(wm.GlobalMenu, wm.ShellCommands, "settings.performance.threaded",
            root + "/Threaded editor (applies on restart)",
            () => Saved.ThreadedEditor, v => Save(Saved with { ThreadedEditor = v }));
        MenuCommandAdapter.RegisterToggle(wm.GlobalMenu, wm.ShellCommands, "settings.performance.parallel",
            root + "/Parallel simulation (applies on restart)",
            () => Saved.ParallelSimulation, v => Save(Saved with { ParallelSimulation = v }));
        foreach (int n in ThreadChoices)
        {
            int choice = n;
            string label = choice == 0 ? "Auto (cores - 2)" : choice.ToString(CultureInfo.InvariantCulture);
            MenuCommandAdapter.RegisterToggle(wm.GlobalMenu, wm.ShellCommands, "settings.performance.threads." + choice.ToString(CultureInfo.InvariantCulture),
                root + "/Worker threads (applies on restart)/" + label,
                () => Saved.WorkerThreads == choice, _ => Save(Saved with { WorkerThreads = choice }));
        }
    }
}
