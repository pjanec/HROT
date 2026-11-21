using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ <c>CE-1025</c> (R-1031) — what a scenario load would break, as HTTP names it (<c>stoppedRun</c>). 📄 docs/DESIGN_Cluster_Load_Phase.md §9.1.
/// ⭐⭐ <c>CE-1438</c> (R-3066 audit #7) — <b>no definition of its own any more</b>: "a run is running" is <see cref="RunModeRunGuard.Of"/>, read off the facade's
/// <see cref="TransportMode"/> (the cluster state through <see cref="ClusterReplayControls.ModeOf"/>), the SAME one the run choice's <i>Stop &amp; Load</i> asks with.
/// The interactive warning moved into <see cref="RunModeChoice"/> (every host with a choice warns by construction).
/// </summary>
public static class ScenarioLoadGuard
{
    /// <summary>"a live run" / "a dry run" when one is running, else null — the one definition, applied to the cluster state.</summary>
    public static string? RunningRun(bool inPreview, ClusterState state) => RunModeRunGuard.Of(ClusterReplayControls.ModeOf(state), inPreview);
}
