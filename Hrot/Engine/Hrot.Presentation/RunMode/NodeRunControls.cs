using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Adapters;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐⭐ <c>CE-1445</c> (R-1044, <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.8) — <b>what DIFFERS between ECS hosts' run controls</b>, the only per-host input of <see cref="NodeRunControls.For(HrotNodeContext, NodeRunProfile, Func{double}?)"/>:
/// the host kind (⇒ gate policy, <see cref="HostGatePolicies.For"/>), its scenario session (an operator: editor, CGF), how it reads the unsaved-check answer, and an EXPLICIT suppression of the
/// toolbar time control (the player issues no ticks — Q54/D4). ⛔ A role parameterises or explicitly suppresses; it never simply lacks.
/// </summary>
/// <param name="Kind">The host's kind — its gate policy is the host's composed <c>Policy</c>, else <c>HostGatePolicies.For(Kind)</c> (the IG composes the Player's).</param>
/// <param name="Control">The host's scenario session; <see langword="null"/> for a host with none (SimHost, IG).</param>
/// <param name="UnsavedReads">How this host reads the unsaved-check round's answer; <see langword="null"/> ⇒ it observes nothing.</param>
/// <param name="SuppressTimeControl">The host's role has no toolbar time control (the composer does not register one over its transport).</param>
/// <param name="Policy">⭐ CE-4149 (DESIGN_Player_Join §7a B1) — the gate policy the host COMPOSES (the Player host passes <see cref="HostGatePolicy.Player"/>); <see langword="null"/> ⇒ the table's default for <paramref name="Kind"/>.</param>
public sealed record NodeRunProfile(RunHostKind Kind, IRunScenarioControl? Control = null, UnsavedCheckReads? UnsavedReads = null, bool SuppressTimeControl = false, HostGatePolicy? Policy = null)
{
    /// <summary>What this host's controls wait on — the policy it composed, else the table's default for <see cref="Kind"/>.</summary>
    public HostGatePolicy GatePolicy => Policy ?? HostGatePolicies.For(Kind);

    /// <summary>A Player (a host that waits only on ITS OWN readiness): no Cancel on its loading screen — it does not own the load.</summary>
    public bool IsPlayer => GatePolicy == HostGatePolicy.Player;
}

/// <summary>
/// ⭐⭐⭐ <c>CE-1445</c> — <b>the ONE construction of an ECS host's run controls</b>: the <see cref="ClusterTimeTransportAdapter"/> over the node's bus and cluster view, with its <see cref="RunModeChoice"/> attached.
/// Replaces the hand-written <c>new ClusterTimeTransportAdapter</c> + <c>AttachRunChoice</c> copies of CGF, SimHost and the editor, and is what the IG (a Player) calls so it gets the same gate and loading screen by construction.
/// </summary>
public static class NodeRunControls
{
    /// <summary>
    /// The configured adapter for <paramref name="node"/>: bus = <c>node.EventBus</c>, view = <c>node.ClusterSlave.View</c>, sim time = <paramref name="simTime"/> or the kernel's,
    /// gate policy from <paramref name="profile"/>, cancel = the node's slave (⛔ none for a Player). Its <see cref="ClusterTimeTransportAdapter.RunChoice"/> is attached.
    /// </summary>
    public static ClusterTimeTransportAdapter For(HrotNodeContext node, NodeRunProfile profile, Func<double>? simTime = null)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        // ⭐⭐ CE-3446 — a threaded node: the UI's controls read the orchestration MIRROR and the UI's cluster view, the time from A's clock, and the
        //   cancel goes to the sim as a job — the same for every staged host (DESIGN_Threaded_Simulation_Presentation §15)
        if (node.Presentation is { } staged)
            return For(staged.UiEventBus, () => staged.UiClusterView,
                simTime ?? (() => staged.World.HasSingletonUnmanaged<Fdp.Core.GlobalTime>() ? staged.World.GetSingletonUnmanaged<Fdp.Core.GlobalTime>().TotalTime : 0.0),
                reason => staged.Post("cluster: cancel load", () => node.ClusterSlave.CancelLoad(reason)), profile);
        return ForSimSide(node, profile, simTime);
    }

    /// <summary>
    /// ⭐ CE-3446 — the NODE's controls (the sim thread's on a threaded node: its HTTP routes, Run to …): the orchestration bus itself, the slave's view,
    /// the kernel's time. A host's UI uses <see cref="For(HrotNodeContext, NodeRunProfile, Func{double}?)"/>, which is this on an unstaged node.
    /// </summary>
    public static ClusterTimeTransportAdapter ForSimSide(HrotNodeContext node, NodeRunProfile profile, Func<double>? simTime = null)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        return For(node.EventBus, () => node.ClusterSlave.View, simTime ?? (() => node.Kernel.CurrentTime.TotalTime),
            reason => node.ClusterSlave.CancelLoad(reason), profile);   // ⛔ the core drops it for a Player
    }

    /// <summary>
    /// The same construction over the three things a node supplies, for a caller that holds them without a <see cref="HrotNodeContext"/> (a rail, a host whose context is not built yet).
    /// ⭐ The node overload is this with its own values — ONE place builds the adapter and attaches the choice.
    /// </summary>
    public static ClusterTimeTransportAdapter For(FdpEventBus bus, Func<ClusterView?> view, Func<double>? simTime, Action<string?>? cancelLoad, NodeRunProfile profile)
    {
        var adapter = new ClusterTimeTransportAdapter(bus, view, simTime);
        adapter.AttachRunChoice(profile.Control, unsavedReads: profile.UnsavedReads, gatePolicy: profile.GatePolicy, cancelLoad: profile.IsPlayer ? null : cancelLoad);
        if (adapter.RunChoice is { } choice) choice.ArmedPresets = ArmedPresets.Shared;   // ⭐ CE-3393 / D11 — every host remembers the armed preset in the user folder, by construction
        return adapter;
    }
}
