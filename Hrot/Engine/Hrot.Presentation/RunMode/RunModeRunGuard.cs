using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-1438</c> (R-3066 audit #6, #7; AQ108 §2.11) — <b>"a run is running" has ONE definition</b>, derived from the facade's
/// <see cref="TransportMode"/>, and the one line a load or an open warns with. ⭐ Used by <see cref="RunModeChoice"/> (so every host with a
/// choice warns <i>Stop &amp; Load</i> by construction) and by HTTP (<c>DebugApiService</c> reads it to name the run its load stopped).
/// <para>⚠ A <see cref="TransportMode.Review"/> (DAR) and a <see cref="TransportMode.Replay"/> (AAR) are NOT a run to stop here: that is unchanged from
/// <c>CE-1025</c> — a load over them never warned and HTTP's <c>stoppedRun</c> stayed <see langword="null"/>.</para>
/// </summary>
public static class RunModeRunGuard
{
    /// <summary>The live run's name in a warning / in HTTP's <c>stoppedRun</c>.</summary>
    public const string LiveRun = "a live run";
    /// <summary>The dry run's name.</summary>
    public const string DryRun = "a dry run";
    /// <summary>The confirming button of a load / open over a run.</summary>
    public const string StopAndLoad = "Stop & Load";
    /// <summary>The title of the load / open confirmation.</summary>
    public const string Title = "Load Scenario";

    /// <summary>
    /// "a live run" / "a dry run" when one is running in <paramref name="mode"/>, else <see langword="null"/>. ⚠ A PAUSED dry run counts: it still holds
    /// a restore point. <paramref name="inDryRun"/> — a host that knows it is in a dry run whatever the mode says (a live run can read as "in preview", CE-4054) — wins the name.
    /// </summary>
    public static string? Of(TransportMode mode, bool inDryRun = false) => inDryRun ? DryRun : mode switch
    {
        TransportMode.Live    => LiveRun,
        TransportMode.Preview => DryRun,
        _ => null,
    };

    /// <summary>The warning before opening <paramref name="scenario"/> over <paramref name="run"/>. Only a dry run has a restore point to forget.</summary>
    public static string LoadPrompt(string run, string scenario) => run == DryRun
        ? $"Loading '{scenario}' stops the dry run and forgets its restore point:\nStop will not bring the current world back."
        : $"Loading '{scenario}' stops the live run.";

    /// <summary>The warning before opening a recording over <paramref name="run"/>.</summary>
    public static string RecordingPrompt(string run) => run == DryRun
        ? "Opening a recording stops the dry run and forgets its restore point:\nStop will not bring the current world back."
        : "Opening a recording stops the live run.";
}
