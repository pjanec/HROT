using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Gameplay;

/// <summary>The Player's deploy states (D-7, S2): <c>Dead</c> is the server's slot saying the Player's man died (a countdown to the deploy screen, or for good).</summary>
public enum DeployFlowState { Waiting, Observing, Choosing, Requesting, TakingControl, Playing, Dead }

/// <summary>One playable unit as the server offers it (<see cref="DeployOfferTopic"/>). <see cref="HolderStation"/> 0 = free.</summary>
public readonly record struct DeployOffer(int UnitNetId, ForceId Side, string SlotName, int HolderStation, byte Kind = 0, int Count = 0, int Max = 0, IReadOnlyList<string>? Kits = null)
{
    /// <summary>⭐ S3 — a spawn AREA the Player may choose, not a unit or a role.</summary>
    public bool IsArea => Kind == (byte)DeployOfferKind.Area;

    /// <summary>⭐ S2 — a role (a man is spawned) rather than a placed unit.</summary>
    public bool IsRole => Kind == (byte)DeployOfferKind.Role;
}

/// <summary>⭐ S2 — the Player's own slot as the slot reader last read it (false when it has none); the flow reads it through this instead of a second reader of the topic.</summary>
public delegate bool SlotLookup(int station, out PlayerSlotTopic slot);

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
public sealed class DeployFlowSystem : IEcsModuleSystem, IDeployFlow
{
    /// <summary>D-7 — every wait times out after this many milliseconds.</summary>
    public const long TimeoutMs = 10_000;
    public const string WaitingForScenario = "Waiting for the server to load a scenario";
    public const string NoAnswer = "the server did not answer";
    public const string KilledObserving = "killed — observing";
    private const long ServerCheckMs = 1_000;

    [Fdp.Core.WorldDerived("static per-world registry of the flow system: filled at construction")]
    private static readonly ConditionalWeakTable<EntityRepository, DeployFlowSystem> PerWorld = new();
    [Fdp.Core.WorldDerived("static per-world list of EVERY flow composed on the world (a node may host several stations): filled at construction")]
    private static readonly ConditionalWeakTable<EntityRepository, List<DeployFlowSystem>> AllPerWorld = new();

    /// <summary>The flow composed on <paramref name="world"/> as its OWN (the screen's and the HTTP route's lookup); null when this node has none. ⭐ CE-11027 — a flow made with <c>register: false</c> (a gateway seat) never becomes it.</summary>
    public static DeployFlowSystem? Of(EntityRepository? world) => world is not null && PerWorld.TryGetValue(world, out var f) ? f : null;

    /// <summary>⭐ CE-11027 S12-2 — the flow of <paramref name="station"/> on <paramref name="world"/> (any of the node's stations, registered or not); null when there is none.</summary>
    public static DeployFlowSystem? Of(EntityRepository? world, int station)
    {
        if (world is null || !AllPerWorld.TryGetValue(world, out var list)) return null;
        for (int i = 0; i < list.Count; i++) if (list[i]._station == station) return list[i];
        return null;
    }

    private readonly IGameplayWire _wire;
    private readonly EntityRepository _world;
    private readonly NetworkEntityMap _map;
    private readonly int _station;
    [Fdp.Core.FrameScratch("offer samples taken this frame: cleared before and after each read")]
    private readonly List<GameplaySample<DeployOfferTopic>> _takenOffers = new();
    [Fdp.Core.FrameScratch("status samples taken this frame: cleared before and after each read")]
    private readonly List<GameplaySample<DeployStatusTopic>> _takenStatus = new();
    [Fdp.Core.NotState("injected DeployFlowEnvironment dependency")]
    private readonly DeployFlowEnvironment _env;
    private readonly Func<long> _now;
    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private readonly Dictionary<int, DeployOffer> _offers = new();
    [Fdp.Core.WorldDerived("sorted snapshot of _offers rebuilt when _offersDirty")]
    private List<DeployOffer> _offerList = new();
    [Fdp.Core.WorldDerived("dirty flag for the sorted offers snapshot")]
    private bool _offersDirty;
    [Fdp.Core.WorldDerived("polled whether a deploy server exists, refreshed every ServerCheckMs")]
    private bool _serverExists;
    [Fdp.Core.WorldDerived("wall-clock throttle for the server existence poll")]
    private long _nextServerCheck;
    [Fdp.Core.SessionState("this station's request id: never reused, an answer to an old one must not match")]
    private string _requestId = "";
    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private int _pendingUnit;
    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private bool _pendingObserve;
    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private long _deadline;
    [Fdp.Core.SessionState("local station choice to stay observer: client session flow state, not world")]
    private bool _explicitObserver;

    /// <param name="register">⭐ CE-11027 S12-2 — true (the default): this flow is the world's own (<see cref="Of(EntityRepository?)"/>); false: a further station of the node (a gateway seat) — found by <see cref="Of(EntityRepository?, int)"/> only, it never replaces the world's own.</param>
    public DeployFlowSystem(IGameplayWire wire, EntityRepository world, NetworkEntityMap map, int station, DeployFlowEnvironment env, bool register = true)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _station = station;
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _now = env.NowMs ?? (() => Environment.TickCount64);
        if (register) PerWorld.AddOrUpdate(world, this);
        AllPerWorld.GetOrCreateValue(world).Add(this);
    }

    /// <summary>⭐ CE-11027 S12-2 — a seat that ended: the flow is forgotten by <see cref="Of(EntityRepository?, int)"/> (and by <see cref="Of(EntityRepository?)"/> when it was the world's own).</summary>
    public void Unregister()
    {
        if (AllPerWorld.TryGetValue(_world, out var list)) list.Remove(this);
        if (PerWorld.TryGetValue(_world, out var own) && ReferenceEquals(own, this)) PerWorld.Remove(_world);
    }

    /// <summary>⭐ CE-11027 S12-2 — the side of this station's last request (<see cref="ForceId.Neutral"/> until it asked): what a host (the gateway seat) reports as the seat's side.</summary>
    [field: Fdp.Core.SessionState("this station's join flow: the side of its last request")]
    public ForceId Side { get; private set; } = ForceId.Neutral;

    /// <summary>⭐ S2 — where the flow reads its own slot (set by the pack to the slot reader); null ⇒ no slot-driven transitions (a test of the S1 flow).</summary>
    public SlotLookup? Slots { get; set; }

    private Fdp.Toolkit.Time.ISimClock? _clock;
    [Fdp.Core.WorldDerived("sim time copied from the clock each frame (189) for display")]
    private double _simNow, _respawnAt;
    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private bool _slotSeenPlaying;

    [field: Fdp.Core.SessionState("deploy flow state machine (Waiting/Choosing/Requesting/Playing) of this station session")]
    public DeployFlowState State { get; private set; } = DeployFlowState.Waiting;

    /// <summary>⭐ S2 — when a Dead slot returns to Choosing (sim s); 0 = never (the Player observes for good); meaningless outside Dead.</summary>
    public double RespawnAtSim => State == DeployFlowState.Dead ? _respawnAt : 0.0;

    /// <summary>Why the flow is Waiting (empty when the gate is closed — the loading screen speaks then), or the last refusal / timeout while Choosing.</summary>
    [Fdp.Core.WorldDerived("display text for the flow reason")]
    private string _reason = "";

    public string Reason
    {
        get
        {
            if (State != DeployFlowState.Dead) return _reason;
            if (_respawnAt <= 0.0) return KilledObserving;
            int secs = (int)Math.Ceiling(Math.Max(0.0, _respawnAt - _simNow));
            return secs == _lastSecs ? _deadText : (_deadText = $"killed — respawn in {_lastSecs = secs} s");
        }
        private set => _reason = value;
    }

    [Fdp.Core.WorldDerived("seconds cache for the death message text")]
    private int _lastSecs = -1;
    [Fdp.Core.WorldDerived("cached death message text")]
    private string _deadText = "";

    /// <summary>The last refusal ("WrongSide", "HeldByAnotherStation (held by station 7)", …) or timeout text; cleared by the next request.</summary>
    [field: Fdp.Core.WorldDerived("last refusal text kept for the UI")]
    public string LastRefusal { get; private set; } = "";

    /// <summary>The last <see cref="DeployStatusTopic"/> answer's unit; 0 none.</summary>
    public int UnitNetId => _pendingUnit;

    /// <summary>The id of the request in flight (empty when none) — what the server's <see cref="DeployStatusTopic"/> answers.</summary>
    public string RequestId => _requestId;

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
    /// <param name="kit">⭐ S3 — which kit of the role (0 = its own type); ignored by a placed unit.</param>
    /// <param name="areaNetId">⭐ S3 — the spawn area chosen (an area offer's id); 0 = any area of the side.</param>
    public bool Request(ForceId side, int unitNetId, byte kit = 0, int areaNetId = 0)
    {
        if (State != DeployFlowState.Choosing) return false;
        return Send(side, unitNetId, observe: false, kit, areaNetId);
    }

    /// <summary>Ask to observe on <paramref name="side"/> (D-13): the server makes a Choosing slot of it. From Choosing, or Observing with a server.</summary>
    public bool Observe(ForceId side)
    {
        if (State != DeployFlowState.Choosing && !(State == DeployFlowState.Observing && _serverExists)) return false;
        _explicitObserver = true;
        return Send(side, 0, observe: true);
    }

    /// <summary>
    /// ⭐ CE-11027 S12-3 (Q114 E) — this station LEAVES the deploy (a seat that ended, a Player that quit): the server frees its slot and answer at once and every unit it holds is released. The flow goes back to Choosing
    /// (Observing when no server exists). Not from Waiting (nothing was joined). Returns false when it did nothing.
    /// </summary>
    public bool Leave()
    {
        if (State == DeployFlowState.Waiting) return false;
        _wire.RequestWriter?.Write(new DeployRequestTopic { RequestId = Guid.NewGuid().ToString("N"), StationNodeId = _station, Leave = true });
        Reset();
        State = _serverExists ? DeployFlowState.Choosing : DeployFlowState.Observing;
        return true;
    }

    /// <summary>The Player stops observing and goes back to choosing (the screen's "Choose a unit" after "keep observing").</summary>
    public bool StopObserving()
    {
        if (State != DeployFlowState.Observing || !_serverExists) return false;
        _explicitObserver = false;
        State = DeployFlowState.Choosing;
        return true;
    }

    /// <summary>The kit and spawn area of the LAST request sent (S3) — what the Player's screen / HTTP asked for; the rails read them.</summary>
    [field: Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    public byte RequestedKit { get; private set; }

    [field: Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    public int RequestedAreaNetId { get; private set; }

    private bool Send(ForceId side, int unit, bool observe, byte kit = 0, int areaNetId = 0)
    {
        RequestedKit = kit; RequestedAreaNetId = areaNetId; Side = side;
        var writer = _wire.RequestWriter;
        _requestId = Guid.NewGuid().ToString("N");
        _pendingUnit = unit; _pendingObserve = observe; LastRefusal = "";
        _deadline = _now() + TimeoutMs;
        State = DeployFlowState.Requesting;
        writer?.Write(new DeployRequestTopic { RequestId = _requestId, StationNodeId = _station, Side = (byte)side, UnitNetId = unit, Observe = observe, Kit = kit, AreaNetId = areaNetId });
        return true;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        long now = _now();
        _clock ??= Fdp.Toolkit.Time.SimClock.Of(view);   // one boxed clock (R-2084)
        _simNow = _clock.TotalTime;
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
        ReadOwnSlot();
        switch (State)
        {
            case DeployFlowState.Requesting: AwaitStatus(now); break;
            case DeployFlowState.TakingControl: AwaitPossession(now); break;
        }
    }

    private void Reset()
    {
        _slotSeenPlaying = false;
        _pendingUnit = 0; _pendingObserve = false; _requestId = ""; _explicitObserver = false;
    }

    private void ReadOffers()
    {
        var reader = _wire.OfferReader;
        if (reader is null) return;
        _takenOffers.Clear();
        if (reader.TakeInto(_takenOffers) == 0) return;
        for (int i = 0; i < _takenOffers.Count; i++)
        {
            var s = _takenOffers[i];
            if (s.Alive)
            {
                var o = s.Data;
                _offers[o.UnitNetId] = new DeployOffer(o.UnitNetId, (ForceId)o.Side, o.SlotName ?? "", o.HolderStation, o.Kind, o.Count, o.Max, o.Kits is { Count: > 0 } k ? k.ToArray() : null);   // ⭐ S3 — no kits ⇒ null, so equality stays value-like
                _offersDirty = true;
            }
            else if (_offers.Remove(s.Data.UnitNetId)) _offersDirty = true;   // the key rides in Data
        }
        _takenOffers.Clear();
    }

    /// <summary>
    /// ⭐ S2 — the SERVER's slot drives the Player's life: Playing → Dead (the man died; RespawnAtSim says when) and Playing → Choosing (he was released), Dead → Choosing at the respawn.
    /// A stale Choosing (unit 0) is ignored until the slot has shown Playing once; a Dead slot counts when it names this life's unit.
    /// </summary>
    private void ReadOwnSlot()
    {
        if (Slots is null || !Slots(_station, out var slot)) return;
        switch (State)
        {
            case DeployFlowState.Playing:
                if (slot.State == (byte)PlayerSlotState.Playing) _slotSeenPlaying = true;
                else if (slot.State == (byte)PlayerSlotState.Dead && slot.UnitNetId == _pendingUnit) {   // the Dead slot names THIS life's man, so a fast death (Playing never read) still counts
                     _respawnAt = slot.RespawnAtSim; _lastSecs = -1; State = DeployFlowState.Dead; }
                else if (_slotSeenPlaying && slot.State == (byte)PlayerSlotState.Choosing) { _pendingUnit = 0; State = DeployFlowState.Choosing; }
                break;
            case DeployFlowState.Dead:
                _respawnAt = slot.State == (byte)PlayerSlotState.Dead ? slot.RespawnAtSim : _respawnAt;
                if (slot.State == (byte)PlayerSlotState.Choosing) { _pendingUnit = 0; _respawnAt = 0; State = DeployFlowState.Choosing; }
                break;
        }
    }

    private void AwaitStatus(long now)
    {
        var reader = _wire.StatusReader;
        if (reader is not null)
        {
            _takenStatus.Clear();
            reader.TakeInto(_takenStatus);
            for (int i = 0; i < _takenStatus.Count; i++)
            {
                if (!_takenStatus[i].Alive) continue;
                var st = _takenStatus[i].Data;
                if (st.StationNodeId != _station || st.RequestId != _requestId) continue;
                _takenStatus.Clear();
                if (!st.Accepted)
                {
                    BackToChoosing(st.HolderStation != 0 ? $"{st.Reason} (held by station {st.HolderStation})" : st.Reason ?? "");
                    return;
                }
                if (_pendingObserve) { State = DeployFlowState.Observing; return; }
                // ⭐ the unit to take is the one the SERVER names: for a placed unit it is the one asked for; for a ROLE it is the man just spawned for it
                //   (📐 measured live 2026-11-05: possessing the asked id — the role's own entity — timed out every time).
                if (st.UnitNetId != 0) _pendingUnit = st.UnitNetId;
                _deadline = now + TimeoutMs;
                State = DeployFlowState.TakingControl;
                TryPossess(now);
                return;
            }
            _takenStatus.Clear();
        }
        if (now >= _deadline) BackToChoosing(NoAnswer);
    }

    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private bool _possessSent;
    [Fdp.Core.WorldDerived("last possess refusal text for display/diagnostics")]
    private string _possessRefusal = "";
    [Fdp.Core.SessionState("this station's join flow with the deploy server: the unit it asked for is checked live (IsAlive) before use")]
    private long _nextPossessTry;

    /// <summary>
    /// Asks this node's station for the unit. ⭐ A LOCAL refusal is retried every 500 ms until the deadline: a man the server JUST spawned is often not on
    /// this node yet (its replica waits for its first pose, CE-4086), so "unknown here" now is not "no". The refusal text is reported only at the deadline.
    /// </summary>
    private void TryPossess(long now)
    {
        var (ok, detail) = _env.Possess(_pendingUnit);
        _possessSent = ok;
        _possessRefusal = ok ? "" : detail;
        _nextPossessTry = now + PossessRetryMs;
    }

    private const long PossessRetryMs = 500;

    private void AwaitPossession(long now)
    {
        if (_map.TryGetEntity(_pendingUnit, out var unit) && _world.IsAlive(unit) && PossessionSystem.HolderOf(_world, unit) == _station)
        {
            State = DeployFlowState.Playing;
            _slotSeenPlaying = false;
            return;
        }
        if (now >= _deadline) { BackToChoosing(_possessSent ? "taking control timed out" : _possessRefusal); return; }
        if (!_possessSent && now >= _nextPossessTry) TryPossess(now);
    }

    /// <summary>A refusal or a timeout: back to Choosing with the reason — and no unit (the live run showed the refused unit still reported as the slot's).</summary>
    private void BackToChoosing(string reason)
    {
        LastRefusal = reason;
        _pendingUnit = 0;
        State = DeployFlowState.Choosing;
    }
}
