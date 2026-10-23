using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ <c>CE-1025</c> (R-1031) — what a scenario load would break: the run that is running now, if any, and the one line the interactive
/// load warns with. Pure. 📄 docs/DESIGN_Cluster_Load_Phase.md §9.1.
/// </summary>
public static class ScenarioLoadGuard
{
    /// <summary>"a live run" / "a dry run" when one is running, else null. ⚠ A PAUSED dry run counts: it still holds a restore point.</summary>
    public static string? RunningRun(bool inPreview, ClusterState state) => state switch
    {
        ClusterState.LoadingLive or ClusterState.OperatingLive => "a live run",
        ClusterState.LoadingPreview or ClusterState.OperatingPreview => "a dry run",
        _ => inPreview ? "a dry run" : null,
    };

    /// <summary>The warning shown before an interactive load over <paramref name="run"/>.</summary>
    public static string Prompt(string run, string scenario)
        => $"Loading '{scenario}' stops {run} and forgets its restore point:\nStop will not bring the current world back.";

    public const string Title = "Load Scenario";
    public const string ConfirmLabel = "Stop & Load";
}
