using System.Runtime.CompilerServices;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Gameplay;

/// <summary>The Player's deploy states (D-7). S2 adds <c>Dead</c>.</summary>
public enum DeployFlowState { Waiting, Observing, Choosing, Requesting, TakingControl, Playing }

/// <summary>One playable unit as the server offers it (<see cref="DeployOfferTopic"/>). <see cref="HolderStation"/> 0 = free.</summary>
public readonly record struct DeployOffer(int UnitNetId, ForceId Side, string SlotName, int HolderStation);

/// <summary>What the flow asks of its node — each a delegate so the pack builds them from the node context and a rail supplies its own (no 10 s sleeps).</summary>
/// <param name="GateOpen"><c>ClusterView.GateOpen(HostGatePolicy.Player)</c>.</param>
/// <param name="OperatingLive">The cluster state the master announced is <c>OperatingLive</c>.</param>
/// <param name="ServerExists"><see cref="DeployServerElection.AnyServer"/> (null ⇒ never a server).</param>
/// <param name="Possess">This node's <c>EntityOrders.Possess(unit)</c>: accepted?, and the refusal text.</param>
/// <param name="NowMs">Wall milliseconds.</param>
public sealed record DeployFlowEnvironment(Func<bool> GateOpen, Func<bool> OperatingLive, Func<bool> ServerExists,
                                           Func<int, (bool Ok, string Detail)> Possess, Func<long>? NowMs = null);

/// <summary>
/// ⭐⭐ Player Join §9.2 / D-4, D-6, D-7, R-4103 — the Player's deploy STATE MACHINE (all the logic; <c>DeployScreen</c> only draws it and calls
/// <see cref="Request"/> / <see cref="Observe"/>). Waiting (gate closed, or open but the cluster not Live — "Waiting for the server to load a scenario") →
/// Observing (no server, R-4103) / Choosing (offers read from <see cref="DeployOfferTopic"/>) → Requesting (a <see cref="DeployRequestTopic"/>, answered on
/// <see cref="DeployStatusTopic"/>) → TakingControl (the EXISTING <c>EntityOrders.Possess</c>) → Playing (read from the Player's OWN world: the unit is
/// <c>PossessedBy</c> this station). Every wait is 10 s. A server that disappears sends any state but Playing back to Observing.
/// <para>⛔ The wire is touched only when needed: nothing while Waiting or with no server; the offer/status readers on the first Choosing; the request writer on the first request.</para>
/// </summary>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.EveryFrame)]
public sealed class DeployFlowSystem : IEcsModuleSystem
{
    /// <summary>D-7 — every wait times out after this many milliseconds.</summary>
    public const long TimeoutMs = 10_000;
    public const string WaitingForScenario = "Waiting for the server to load a scenario";
    public const string NoAnswer = "the server did not answer";
    private const long ServerCheckMs = 1_000;

    private static readonly ConditionalWeakTable<EntityRepository, DeployFlowSystem> PerWorld = new();

    /// <summary>The flow composed on <paramref name="world"/> (the screen's and the HTTP route's lookup); null when this node has none.</summary>
    public static DeployFlowSystem? Of(EntityRepository? world) => world is not null && PerWorld.TryGetValue(world, out var f) ? f : null;

    private readonly GameplayWire _wire;
    private readonly EntityRepository _world;
    private readonly NetworkEntityMap _map;
    private readonly int _station;
    private readonly DeployFlowEnvironment _env;
    private readonly Func<long> _now;
    private readonly Dictionary<int, DeployOffer> _offers = new();
    private List<DeployOffer> _offerList = new();
    private bool _offersDirty;
    private bool _serverExists;
    private long _nextServerCheck;
    private string _requestId = "";
    private int _pendingUnit;
    private bool _pendingObserve;
    private long _deadline;
    private bool _explicitObserver;

    public DeployFlowSystem(GameplayWire wire, EntityRepository world, NetworkEntityMap map, int station, DeployFlowEnvironment env)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _station = station;
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _now = env.NowMs ?? (() => Environment.TickCount64);
        PerWorld.AddOrUpdate(world, this);
    }

    public DeployFlowState State { get; private set; } = DeployFlowState.Waiting;

    /// <summary>Why the flow is Waiting (empty when the gate is closed — the loading screen speaks then), or the last refusal / timeout while Choosing.</summary>
    public string Reason { get; private set; } = "";

    /// <summary>The last refusal ("WrongSide", "HeldByAnotherStation (held by station 7)", …) or timeout text; cleared by the next request.</summary>
    public string LastRefusal { get; private set; } = "";

    /// <summary>The last <see cref="DeployStatusTopic"/> answer's unit; 0 none.</summary>
    public int UnitNetId => _pendingUnit;

    /// <summary>True while some node serves Deploy (R-4103); sampled about once a second.</summary>
    public bool ServerExists => _serverExists;

    /// <summary>This node's station id (a request's <c>StationNodeId</c>).</summary>
    public int Station => _station;

    /// <summary>The offers read so far, grouped however the screen likes (a snapshot list, rebuilt only when an offer changed).</summary>
    public IReadOnlyList<DeployOffer> Offers
    {
        get
        {
            if (_offersDirty) { _offerList = new List<DeployOffer>(_offers.Values); _offerList.Sort((a, b) => a.UnitNetId.CompareTo(b.UnitNetId)); _offersDirty = false; }
            return _offerList;
        }
    }

    /// <summary>Ask the server for <paramref name="unitNetId"/> on <paramref name="side"/>. Only from Choosing; false otherwise.</summary>
    public bool Request(ForceId side, int unitNetId)
    {
        if (State != DeployFlowState.Choosing) return false;
        return Send(side, unitNetId, observe: false);
    }

    /// <summary>Ask to observe on <paramref name="side"/> (D-13): the server makes a Choosing slot of it. From Choosing, or Observing with a server.</summary>
    public bool Observe(ForceId side)
    {
        if (State != DeployFlowState.Choosing && !(State == DeployFlowState.Observing && _serverExists)) return false;
        _explicitObserver = true;
        return Send(side, 0, observe: true);
    }

    /// <summary>The Player stops observing and goes back to choosing (the screen's "Choose a unit" after "keep observing").</summary>
    public bool StopObserving()
    {
        if (State != DeployFlowState.Observing || !_serverExists) return false;
        _explicitObserver = false;
        State = DeployFlowState.Choosing;
        return true;
    }

    private bool Send(ForceId side, int unit, bool observe)
    {
        var writer = _wire.RequestWriter;
        _requestId = Guid.NewGuid().ToString("N");
        _pendingUnit = unit; _pendingObserve = observe; LastRefusal = "";
        _deadline = _now() + TimeoutMs;
        State = DeployFlowState.Requesting;
        writer?.Write(new DeployRequestTopic { RequestId = _requestId, StationNodeId = _station, Side = (byte)side, UnitNetId = unit, Observe = observe });
        return true;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        long now = _now();
        bool open = _env.GateOpen();
        bool live = open && _env.OperatingLive();
        if (!live)
        {
            if (State != DeployFlowState.Waiting) Reset();
            State = DeployFlowState.Waiting;
            Reason = open ? WaitingForScenario : "";
            _serverExists = false;
            return;
        }

        if (now >= _nextServerCheck) { _nextServerCheck = now + ServerCheckMs; _serverExists = _env.ServerExists(); }

        if (State == DeployFlowState.Waiting)
        {
            Reason = "";
            State = _serverExists && !_explicitObserver ? DeployFlowState.Choosing : DeployFlowState.Observing;
        }

        if (!_serverExists)
        {
            if (State != DeployFlowState.Playing && State != DeployFlowState.Observing) { Reset(); State = DeployFlowState.Observing; }
            return;
        }
        if (State == DeployFlowState.Observing && !_explicitObserver) State = DeployFlowState.Choosing;   // a server appeared

        // ⭐ With a server and a live run the Player's ends are made as it enters Choosing — so the volatile request writer has matched the server's reader by the time the Player clicks.
        _ = _wire.RequestWriter; _ = _wire.StatusReader;
        ReadOffers();
        switch (State)
        {
            case DeployFlowState.Requesting: AwaitStatus(now); break;
            case DeployFlowState.TakingControl: AwaitPossession(now); break;
        }
    }

    private void Reset()
    {
        _pendingUnit = 0; _pendingObserve = false; _requestId = ""; _explicitObserver = false;
    }

    private void ReadOffers()
    {
        var reader = _wire.OfferReader;
        if (reader is null) return;
        using var loan = reader.Take();
        foreach (var s in loan)
        {
            if (s.IsValid)
            {
                var o = s.Data;
                _offers[o.UnitNetId] = new DeployOffer(o.UnitNetId, (ForceId)o.Side, o.SlotName ?? "", o.HolderStation);
                _offersDirty = true;
            }
            else if (s.Info.InstanceState != DdsInstanceState.Alive)
            {
                if (_offers.Remove(DdsTypeSupport.FromNative<DeployOfferTopic>(s.NativePtr).UnitNetId)) _offersDirty = true;
            }
        }
    }

    private void AwaitStatus(long now)
    {
        var reader = _wire.StatusReader;
        if (reader is not null)
        {
            using var loan = reader.Take();
            foreach (var s in loan)
            {
                if (!s.IsValid) continue;
                var st = s.Data;
                if (st.StationNodeId != _station || st.RequestId != _requestId) continue;
                if (!st.Accepted)
                {
                    LastRefusal = st.HolderStation != 0 ? $"{st.Reason} (held by station {st.HolderStation})" : st.Reason ?? "";
                    State = DeployFlowState.Choosing;
                    return;
                }
                if (_pendingObserve) { State = DeployFlowState.Observing; return; }
                var (ok, detail) = _env.Possess(_pendingUnit);
                if (!ok) { LastRefusal = detail; State = DeployFlowState.Choosing; return; }
                _deadline = now + TimeoutMs;
                State = DeployFlowState.TakingControl;
                return;
            }
        }
        if (now >= _deadline) { LastRefusal = NoAnswer; State = DeployFlowState.Choosing; }
    }

    private void AwaitPossession(long now)
    {
        if (_map.TryGetEntity(_pendingUnit, out var unit) && _world.IsAlive(unit) && PossessionSystem.HolderOf(_world, unit) == _station)
        {
            State = DeployFlowState.Playing;
            return;
        }
        if (now >= _deadline) { LastRefusal = "taking control timed out"; State = DeployFlowState.Choosing; }
    }
}
