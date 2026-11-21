using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.Threading;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ CE-3446 — what the deploy screen, the spawn-area layer and the HTTP route read of a node's deploy flow, and the verbs they call: the flow
/// itself (<see cref="DeployFlowSystem"/>, on the node's world) or, on a threaded node's presentation world, its <see cref="DeployFlowView"/>.
/// </summary>
public interface IDeployFlow
{
    DeployFlowState State { get; }
    string Reason { get; }
    string LastRefusal { get; }
    int UnitNetId { get; }
    string RequestId { get; }
    bool ServerExists { get; }
    int Station { get; }
    IReadOnlyList<DeployOffer> Offers { get; }
    double RespawnAtSim { get; }
    byte RequestedKit { get; }
    int RequestedAreaNetId { get; }
    bool Request(ForceId side, int unitNetId, byte kit = 0, int areaNetId = 0);
    bool Observe(ForceId side);
    bool StopObserving();
}

/// <summary>⭐ CE-3446 — the deploy flow a world's readers use: the flow on the node's world, its view on a threaded node's presentation world.</summary>
public static class DeployFlows
{
    private static readonly ConditionalWeakTable<EntityRepository, DeployFlowView> Views = new();

    /// <summary>The flow (or its view) on <paramref name="world"/>; null when the node has none.</summary>
    public static IDeployFlow? Of(EntityRepository? world)
        => world is null ? null : (IDeployFlow?)DeployFlowSystem.Of(world) ?? (Views.TryGetValue(world, out var v) ? v : null);

    /// <summary>
    /// A staged node: the UI's view of <paramref name="flow"/> on the presentation world, fed by a readout with every tick (the shared
    /// presentation's channel). Called by the capability that composes the flow, so every staged host gets it by construction.
    /// </summary>
    public static DeployFlowView Stage(DeployFlowSystem flow, Hrot.Common.Infrastructure.StagedPresentation presentation)
    {
        var view = new DeployFlowView(presentation.World);
        Views.AddOrUpdate(presentation.World, view);
        presentation.AddReadout<DeployFlowReadout>(last => DeployFlowReadout.ReadIfChanged(last, flow), view.Apply);
        return view;
    }
}

/// <summary>⭐ CE-3446 — one tick's flow as the UI reads it (immutable; a new one only when something changed).</summary>
public sealed record DeployFlowReadout(
    DeployFlowState State, string Reason, string LastRefusal, int UnitNetId, string RequestId, bool ServerExists, int Station,
    IReadOnlyList<DeployOffer> Offers, double RespawnAtSim, byte RequestedKit, int RequestedAreaNetId)
{
    /// <summary>Sim thread: the flow's state now, or null when it equals <paramref name="last"/> (0 B then).</summary>
    public static DeployFlowReadout? ReadIfChanged(DeployFlowReadout? last, DeployFlowSystem f)
    {
        var offers = f.Offers;   // the flow rebuilds its list only when an offer changed, so reference equality is change detection
        if (last is not null && last.State == f.State && ReferenceEquals(last.Reason, f.Reason) && ReferenceEquals(last.LastRefusal, f.LastRefusal)
            && last.UnitNetId == f.UnitNetId && ReferenceEquals(last.RequestId, f.RequestId) && last.ServerExists == f.ServerExists
            && last.Station == f.Station && ReferenceEquals(last.Offers, offers) && last.RespawnAtSim == f.RespawnAtSim
            && last.RequestedKit == f.RequestedKit && last.RequestedAreaNetId == f.RequestedAreaNetId)
            return null;
        return new DeployFlowReadout(f.State, f.Reason, f.LastRefusal, f.UnitNetId, f.RequestId, f.ServerExists, f.Station,
            offers, f.RespawnAtSim, f.RequestedKit, f.RequestedAreaNetId);
    }
}

/// <summary>
/// ⭐ CE-3446 — the deploy flow as the UI thread of a threaded node sees it: reads from the last readout (applied in order with the pull), verbs
/// posted to the sim thread as jobs on the flow itself. A verb answers what the flow would answer from the state the UI shows (its precondition).
/// </summary>
public sealed class DeployFlowView : IDeployFlow
{
    private static readonly DeployFlowReadout Initial = new(DeployFlowState.Waiting, "", "", 0, "", false, 0, Array.Empty<DeployOffer>(), 0.0, 0, 0);
    private readonly EntityRepository _presentation;
    private DeployFlowReadout _r = Initial;

    public DeployFlowView(EntityRepository presentationWorld) => _presentation = presentationWorld ?? throw new ArgumentNullException(nameof(presentationWorld));

    /// <summary>UI thread: the readout the sim published.</summary>
    public void Apply(DeployFlowReadout readout) => _r = readout ?? throw new ArgumentNullException(nameof(readout));

    public DeployFlowState State => _r.State;
    public string Reason => _r.Reason;
    public string LastRefusal => _r.LastRefusal;
    public int UnitNetId => _r.UnitNetId;
    public string RequestId => _r.RequestId;
    public bool ServerExists => _r.ServerExists;
    public int Station => _r.Station;
    public IReadOnlyList<DeployOffer> Offers => _r.Offers;
    public double RespawnAtSim => _r.RespawnAtSim;
    public byte RequestedKit => _r.RequestedKit;
    public int RequestedAreaNetId => _r.RequestedAreaNetId;

    public bool Request(ForceId side, int unitNetId, byte kit = 0, int areaNetId = 0)
    {
        if (State != DeployFlowState.Choosing) return false;
        SimJobs.Run(_presentation, s => DeployFlowSystem.Of(s)?.Request(side, unitNetId, kit, areaNetId), "deploy: request");
        return true;
    }

    public bool Observe(ForceId side)
    {
        if (State != DeployFlowState.Choosing && !(State == DeployFlowState.Observing && ServerExists)) return false;
        SimJobs.Run(_presentation, s => DeployFlowSystem.Of(s)?.Observe(side), "deploy: observe");
        return true;
    }

    public bool StopObserving()
    {
        if (State != DeployFlowState.Observing || !ServerExists) return false;
        SimJobs.Run(_presentation, s => DeployFlowSystem.Of(s)?.StopObserving(), "deploy: stop observing");
        return true;
    }
}
