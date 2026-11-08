using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ CE-3407 (AQ108 §0 D27, R-3083) — the CLUSTER half of "can I save the scenario": only in Edit, never while the cluster is in error.
/// <see cref="EditorScenarioSession.SaveDisabledReason"/> asks this first, then the variant gate (<c>ScenarioSaveGate</c>) — so the pen, the Save slot and
/// File › Save read one reason and cannot disagree.
/// </summary>
/// <remarks>
/// ⭐ A view that never followed a cluster state (a host with no orchestration, a rail's fresh view) refuses nothing — the variant gate still answers.
/// ⛔ Allocation-free on the steady path (every reason is a constant; the error's text is cached per reason string) — the toolbar reads it every frame.
/// In Live / DAR / AAR the Save SLOT takes a checkpoint instead (<c>ISaveInterception</c>); this reason says why the SCENARIO is not written.
/// </remarks>
public static class SaveRefusal
{
    public const string Preview = "a preview runs a copy of the edit world";
    public const string Live    = "a live run is not the scenario — Save takes a checkpoint";
    public const string Review  = "a review of the live run — Save takes a checkpoint";
    public const string Replay  = "a recording is open — Save takes a checkpoint";
    public const string Busy    = "the cluster is changing state";
    public const string NoScene = "no scenario is open for editing";

    private static string? _errorFor;
    private static string? _errorText;

    /// <summary>Why the cluster refuses a scenario save now, or null when it is in Edit and healthy (or <paramref name="view"/> follows no cluster).</summary>
    public static string? Of(ClusterView? view)
    {
        if (view is null || !view.HasFollowedState) return null;
        if (view.IsInError)
        {
            string r = view.ErrorReason;
            if (!ReferenceEquals(r, _errorFor)) { _errorFor = r; _errorText = "the cluster is in error: " + r; }
            return _errorText;
        }
        return view.State switch
        {
            ClusterState.OperatingEdit => null,
            ClusterState.OperatingPreview or ClusterState.LoadingPreview or ClusterState.UnloadingPreview => Preview,
            ClusterState.OperatingLive => Live,
            ClusterState.OperatingReview => Review,
            ClusterState.OperatingReplay => Replay,
            ClusterState.Idle => NoScene,
            _ => Busy,
        };
    }
}
