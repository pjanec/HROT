using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ <c>CE-1025</c> (R-1031) — what a scenario load would break: the run that is running now, if any, and the one line the interactive
/// load warns with. Pure. 📄 docs/DESIGN_Cluster_Load_Phase.md §9.1.
/// </summary>
public static class ScenarioLoadGuard
{
    /// <summary>"a live run" / "a dry run" when one is running, else null. ⚠ A PAUSED dry run counts: it still holds a restore point.
    /// ⭐ The editor's dry run wins the name: it plays inside a live-loaded cluster (<c>OperatingLive</c>), measured live 2026-10-23.</summary>
    public static string? RunningRun(bool inPreview, ClusterState state) => inPreview ? DryRun : state switch
    {
        ClusterState.LoadingLive or ClusterState.OperatingLive => LiveRun,
        ClusterState.LoadingPreview or ClusterState.OperatingPreview => DryRun,
        _ => null,
    };

    public const string LiveRun = "a live run";
    public const string DryRun = "a dry run";

    /// <summary>The warning shown before an interactive load over <paramref name="run"/>. Only a dry run has a restore point to forget.</summary>
    public static string Prompt(string run, string scenario) => run == DryRun
        ? $"Loading '{scenario}' stops the dry run and forgets its restore point:\nStop will not bring the current world back."
        : $"Loading '{scenario}' stops the live run.";

    public const string Title = "Load Scenario";
    public const string ConfirmLabel = "Stop & Load";
}
