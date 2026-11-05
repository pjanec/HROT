using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Time;
using Hrot.Common;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐⭐ Player Join §9 (D-1…D-9, S1-a…e) — THE deploy server: validates a Player station's <see cref="DeployRequestTopic"/>, reserves the unit for it
/// (<see cref="PlayerSlotTopic"/>), answers on <see cref="DeployStatusTopic"/> and offers every playable unit on <see cref="DeployOfferTopic"/>. It never writes a
/// unit it does not own (D-8) and never possesses for the Player — the station asks the brain, <see cref="StationClaims"/> decides.
/// <para>⭐ Gate: <c>[NodeFeature("gameplay.deploy-server")]</c> — the scheduler skips the system while the feature is off, so a node with the pack composed and
/// the feature off creates no gameplay reader/writer (<see cref="GameplayWire"/> is lazy). ON: one server per cluster — it stands down (disposing its slots and
/// offers, creating nothing new) unless it is the lowest node id with the feature on (<see cref="DeployServerElection"/>, re-read about once a second).</para>
/// <para>Slot life: Choosing (observer) / TakingControl (reserved until <c>now + 30 s</c> of SIM time) → Playing (the unit is <c>PossessedBy</c> the station) →
/// disposed (a TakingControl that lapsed, or the station's node departed — <c>NodeDeparted</c>). ⛔ No LINQ and no allocation on a frame that carries no
/// request (R-2084): strings are made when a sample arrives; the offers (≈ 1 Hz) reuse a name cache.</para>
/// </summary>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.EveryFrame)]
[NodeFeature(GameplayFeatures.DeployServerId)]
public sealed class DeployProcessorSystem : IEcsModuleSystem
{
    /// <summary>S1-d — a reservation not taken within this many seconds of sim time lapses.</summary>
    public const double ReservationSeconds = 30.0;
    private const long PeriodMs = 1000;

    private sealed class Slot
    {
        public int Station; public string Name = ""; public ForceId Side; public PlayerSlotState State; public int UnitNetId; public double Until;
    }

    private sealed class Offered { public FixedString32 NameKey; public string Name = ""; public bool Seen; }

    private readonly GameplayWire _wire;
    private readonly NetworkEntityMap _map;
    private readonly int _local;
    private readonly DeployServerElection? _election;
    private readonly Func<long> _nowMs;
    private readonly List<Slot> _slots = new();
    private readonly Dictionary<int, Offered> _offered = new();
    private readonly List<int> _scratch = new();
    private bool _isServer;
    private long _nextElectionMs, _nextOfferMs;
    private bool _offersDirty;

    /// <param name="wire">This node's gameplay wire.</param>
    /// <param name="map">The node's network entity map (UnitNetId → entity).</param>
    /// <param name="localNode">This node's id.</param>
    /// <param name="election">Who serves; null ⇒ this node always does (a single-server test or a node with no cluster cache).</param>
    /// <param name="nowMs">Wall milliseconds, for the 1 s cadences (tests inject).</param>
    public DeployProcessorSystem(GameplayWire wire, NetworkEntityMap map, int localNode, DeployServerElection? election = null, Func<long>? nowMs = null)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _local = localNode;
        _election = election;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>True while this node is THE server (the last election).</summary>
    public bool IsServer => _isServer;

    /// <summary>How many player slots this server holds.</summary>
    public int SlotCount => _slots.Count;

    /// <summary>The slot of <paramref name="station"/>; false when it has none.</summary>
    public bool TryGetSlot(int station, out PlayerSlotState state, out int unitNetId)
    {
        foreach (var s in _slots)
            if (s.Station == station) { state = s.State; unitNetId = s.UnitNetId; return true; }
        state = PlayerSlotState.Choosing; unitNetId = 0;
        return false;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        long now = _nowMs();
        if (now >= _nextElectionMs)
        {
            _nextElectionMs = now + PeriodMs;
            bool serves = _election?.IsServer(_local) ?? true;
            if (_isServer && !serves) StandDown(repo);
            if (!_isServer && serves) _offersDirty = true;
            _isServer = serves;
        }
        if (!_isServer) return;

        double simNow = SimClock.Of(repo).TotalTime;
        ReadRequests(repo, simNow);
        ReadDepartures(repo);
        Progress(repo, simNow);
        if (_offersDirty || now >= _nextOfferMs) { _nextOfferMs = now + PeriodMs; _offersDirty = false; PublishOffers(repo); }
    }

    // ── requests ────────────────────────────────────────────────────────────────────────────

    private void ReadRequests(EntityRepository repo, double simNow)
    {
        var reader = _wire.Requests;
        if (reader is null) return;
        using var loan = reader.Take();
        foreach (var s in loan)
            if (s.IsValid) Handle(repo, s.Data, simNow);
    }

    private void Handle(EntityRepository repo, in DeployRequestTopic r, double simNow)
    {
        var side = (ForceId)r.Side;
        if (r.Observe)
        {
            SetSlot(repo, r.StationNodeId, "", side, PlayerSlotState.Choosing, 0, 0);
            Answer(r, accepted: true, "", 0, 0);
            return;
        }
        if (!_map.TryGetEntity(r.UnitNetId, out var unit) || !repo.IsAlive(unit)) { Answer(r, false, "NoSuchUnit", 0, r.UnitNetId); return; }
        if (!repo.IsComponentTypeRegistered<Playable>() || !repo.HasComponent<Playable>(unit)) { Answer(r, false, "NotPlayable", 0, r.UnitNetId); return; }
        var playable = repo.GetComponentRO<Playable>(unit);
        if (playable.Side != side) { Answer(r, false, "WrongSide", 0, r.UnitNetId); return; }
        if (!PossessionSystem.Alive(repo, unit)) { Answer(r, false, "Dead", 0, r.UnitNetId); return; }
        var refusal = StationClaims.ForPossess(repo, unit, r.StationNodeId, out int holder);
        if (refusal != ClaimRefusal.None) { Answer(r, false, refusal.ToString(), holder, r.UnitNetId); return; }

        SetSlot(repo, r.StationNodeId, playable.SlotName.ToString(), side, PlayerSlotState.TakingControl, r.UnitNetId, simNow + ReservationSeconds);
        Answer(r, true, "", 0, r.UnitNetId);
        _offersDirty = true;   // the unit now shows its reserving station
    }

    private void Answer(in DeployRequestTopic r, bool accepted, string reason, int holder, int unitNetId)
        => _wire.Status?.Write(new DeployStatusTopic
        {
            StationNodeId = r.StationNodeId, RequestId = r.RequestId ?? "", Accepted = accepted, Reason = reason, UnitNetId = unitNetId, HolderStation = holder,
        });

    // ── slots ───────────────────────────────────────────────────────────────────────────────

    private void SetSlot(EntityRepository repo, int station, string name, ForceId side, PlayerSlotState state, int unitNetId, double until)
    {
        Slot? slot = null;
        foreach (var s in _slots) if (s.Station == station) { slot = s; break; }
        if (slot is null) { slot = new Slot { Station = station }; _slots.Add(slot); }
        slot.Name = name; slot.Side = side; slot.State = state; slot.UnitNetId = unitNetId; slot.Until = until;
        // The reservation is made HERE as well as by the slot reader (which will say the same a frame later): a second request for the unit in the SAME frame
        // must already see it reserved — the DDS round trip would let two stations both be accepted.
        StationReservations.Of(repo).Set(station, side, unitNetId);
        WriteSlot(slot);
    }

    private void WriteSlot(Slot s)
        => _wire.SlotWriter?.Write(new PlayerSlotTopic
        {
            StationNodeId = s.Station, Name = s.Name, Side = (byte)s.Side, State = (byte)s.State, UnitNetId = s.UnitNetId, ReservedUntilSim = s.Until,
        });

    private void DisposeSlotAt(EntityRepository repo, int index)
    {
        int station = _slots[index].Station;
        _slots.RemoveAt(index);
        StationReservations.Of(repo).Remove(station);
        _wire.SlotWriter?.DisposeInstance(new PlayerSlotTopic { StationNodeId = station });
        _offersDirty = true;
    }

    private void Progress(EntityRepository repo, double simNow)
    {
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            var s = _slots[i];
            if (s.State != PlayerSlotState.TakingControl) continue;
            if (_map.TryGetEntity(s.UnitNetId, out var unit) && repo.IsAlive(unit) && PossessionSystem.HolderOf(repo, unit) == s.Station)
            {
                s.State = PlayerSlotState.Playing; s.Until = 0;
                WriteSlot(s);
                _offersDirty = true;
            }
            else if (simNow > s.Until) DisposeSlotAt(repo, i);   // the reservation lapsed untaken
        }
    }

    private void ReadDepartures(EntityRepository repo)
    {
        if (_slots.Count == 0 || !repo.Bus.IsRegistered<NodeDeparted>()) return;
        foreach (ref readonly var gone in repo.ReadEvents<NodeDeparted>())
            for (int i = _slots.Count - 1; i >= 0; i--)
                if (_slots[i].Station == gone.NodeId) DisposeSlotAt(repo, i);
    }

    // ── offers ──────────────────────────────────────────────────────────────────────────────

    private void PublishOffers(EntityRepository repo)
    {
        var writer = _wire.Offers;
        foreach (var o in _offered.Values) o.Seen = false;
        if (repo.IsComponentTypeRegistered<Playable>() && repo.IsComponentTypeRegistered<NetworkIdentity>())
        {
            foreach (var unit in repo.Query().With<Playable>().Build())
            {
                if (!repo.IsAlive(unit) || !repo.HasComponent<NetworkIdentity>(unit) || !PossessionSystem.Alive(repo, unit)) continue;
                int netId = repo.GetComponentRO<NetworkIdentity>(unit).Value;
                var playable = repo.GetComponentRO<Playable>(unit);
                if (!_offered.TryGetValue(netId, out var o)) _offered[netId] = o = new Offered { NameKey = default };
                if (!o.Seen && (o.Name.Length == 0 || !o.NameKey.Equals(playable.SlotName))) { o.NameKey = playable.SlotName; o.Name = playable.SlotName.ToString(); }
                o.Seen = true;
                int holder = PossessionSystem.HolderOf(repo, unit) ?? 0;
                if (holder == 0 && StationReservations.TryGet(repo, out var res) && res.TryReservedFor(netId, out int reservedFor)) holder = reservedFor;
                writer?.Write(new DeployOfferTopic { UnitNetId = netId, Side = (byte)playable.Side, SlotName = o.Name, HolderStation = holder });
            }
        }
        _scratch.Clear();
        foreach (var kv in _offered) if (!kv.Value.Seen) _scratch.Add(kv.Key);
        foreach (int gone in _scratch)
        {
            _offered.Remove(gone);
            writer?.DisposeInstance(new DeployOfferTopic { UnitNetId = gone });
        }
    }

    // ── standing down ───────────────────────────────────────────────────────────────────────

    /// <summary>A lower-id server appeared: retract everything this one published, so the new server is the only voice (it re-offers the units itself).</summary>
    private void StandDown(EntityRepository repo)
    {
        for (int i = _slots.Count - 1; i >= 0; i--) DisposeSlotAt(repo, i);
        foreach (int netId in _offered.Keys) _scratch.Add(netId);
        foreach (int netId in _scratch) _wire.Offers?.DisposeInstance(new DeployOfferTopic { UnitNetId = netId });
        _scratch.Clear();
        _offered.Clear();
    }
}
