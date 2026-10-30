using System;
using Fdp.Toolkit.Orchestration;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐⭐ <c>CE-1442</c> (U1; <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.7) — <b>the ONE run gate</b>: whether this host's controls may act now, and if not, why. Built in ONE place (<c>ClusterTimeTransportAdapter.Gate</c>, from the node's
/// <see cref="ClusterView"/> and the host's <see cref="HostGatePolicy"/>) and read through <see cref="RunModeChoice.Gate"/> by every control — the toolbar, the mode menu, the scenario door, the loading screen, the error section,
/// the start surface and the pickers. ⛔ No control reads <c>ClusterView.CanOperate</c> itself: <c>GateOpen(policy)</c> is not <c>CanOperate</c> (an operator also waits for its OWN load).
/// </summary>
/// <param name="Open">The controls may act.</param>
/// <param name="Reason">Why not (<see langword="null"/> when open): the error, else <c>loading: …</c>, else the view's <c>NotOperableReason</c>.</param>
/// <param name="Loading">The gate is closed because a load is in progress (this node's or the cluster's); never while in error.</param>
/// <param name="Progress">What the load is doing now (the cluster's <c>LoadProgress</c>, else this node's step); empty when not loading.</param>
/// <param name="InError">The cluster is in its ERROR state (R-3073): only Close and Exit work.</param>
/// <param name="ErrorReason">The error text (empty when not in error).</param>
/// <param name="HasInventory">The master's lists (scenarios, recordings) have arrived — a picker shows <see cref="WaitingForInventoryText"/> until then.</param>
public sealed record RunGate(bool Open, string? Reason, bool Loading, string Progress, bool InError, string ErrorReason, bool HasInventory)
{
    /// <summary>What a picker shows while the cluster's list has not arrived (never an empty list that reads as "none").</summary>
    public const string WaitingForInventoryText = "waiting for the cluster's list…";

    /// <summary>The gate of a host with no cluster view (a bare rig): nothing waits.</summary>
    public static RunGate AlwaysOpen { get; } = new(true, null, false, string.Empty, false, string.Empty, true);

    /// <summary>The one derivation, from the node's view and the host's policy. <see langword="null"/> view ⇒ <see cref="AlwaysOpen"/>.</summary>
    public static RunGate From(ClusterView? view, HostGatePolicy policy)
    {
        if (view is null) return AlwaysOpen;
        var open = view.GateOpen(policy);
        var inError = view.IsInError;
        var progress = view.LoadProgress.Length > 0 ? view.LoadProgress : view.LocalStep;
        var loading = !open && !inError && (view.IsClusterLoading || view.IsLocalLoading);
        string? reason = null;
        if (!open)
            reason = inError ? view.ErrorReason
                   : loading ? (progress.Length > 0 ? $"loading: {progress}" : "loading")
                   : view.NotOperableReason ?? "this node is not ready yet";
        return new RunGate(open, reason, loading, loading ? progress : string.Empty, inError, view.ErrorReason, view.HasInventory);
    }
}
