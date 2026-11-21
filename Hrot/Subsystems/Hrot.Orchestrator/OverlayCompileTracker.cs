using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ T-6 (CE-3433, docs/DESIGN_Terrain_Authoring.md §18c-2) — the terrain-overlay compiles IN FLIGHT, by scenario name. A scenario save answers "saved" as soon as its <c>scenario.json</c> is written; the overlay compile (the expansion, and the navtile bake) runs AFTER, on a
/// low-priority thread of its own, and a LOAD of that scenario must wait for it: <see cref="StorageGatewayModule.PrefetchScenarioAsync"/> awaits <see cref="WaitAsync"/> before it reads the scenario folder, so the overlay exists before a node needs it. Two saves of one scenario
/// run one after the other (the second compiles what the first left). ⭐ Gives way while a Live runs: a compile does not START while <c>isLive</c> holds (it polls), as CE-4330 ⑤ holds a transfer to its give-way rate.
/// </summary>
public static class OverlayCompileTracker
{
    private static readonly ConcurrentDictionary<string, Task> InFlight = new(StringComparer.Ordinal);

    /// <summary>How long a start waits between looks at <c>isLive</c>.</summary>
    internal static TimeSpan LivePoll { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Runs <paramref name="compile"/> for <paramref name="scenarioName"/> after any compile of it already running, once no Live runs; returns at once.</summary>
    public static void Start(string scenarioName, Action compile, Func<bool>? isLive = null)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous = Task.CompletedTask;
        InFlight.AddOrUpdate(scenarioName, _ => tcs.Task, (_, old) => { previous = old; return tcs.Task; });
        var thread = new Thread(() =>
        {
            try
            {
                try { previous.Wait(); } catch (Exception) { }
                while (isLive != null && isLive()) Thread.Sleep(LivePoll);   // ⭐ give way: nothing starts while a Live runs
                compile();
            }
            catch (Exception) { /* CompileTerrainOverlay logs its own failures; the tracker only orders */ }
            finally { tcs.SetResult(); InFlight.TryUpdate(scenarioName, Task.CompletedTask, tcs.Task); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "overlay-compile:" + scenarioName };
        thread.Start();
    }

    /// <summary>Completes when no compile of <paramref name="scenarioName"/> is in flight (immediately when none is).</summary>
    public static Task WaitAsync(string scenarioName) => InFlight.TryGetValue(scenarioName, out var t) ? t : Task.CompletedTask;

    /// <summary>True while a compile of <paramref name="scenarioName"/> is running or queued.</summary>
    public static bool IsCompiling(string scenarioName) => InFlight.TryGetValue(scenarioName, out var t) && !t.IsCompleted;
}
