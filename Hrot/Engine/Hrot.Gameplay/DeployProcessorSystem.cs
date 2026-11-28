using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
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
public sealed class DeployProcessorSystem : IEcsModuleSystem, Fdp.Interfaces.IReplayRestoreGated
{
    /// <summary>S1-d — a reservation not taken within this many seconds of sim time lapses.</summary>
    public const double ReservationSeconds = 30.0;
    private const long PeriodMs = 1000;

    /// <summary>⭐ S2 — a role's man made by the one creation path: (tkb type, where, the components) ⇒ his network id, pre-allocated (CE-4266; 0 = nothing can spawn on this node).</summary>
    public delegate int RoleSpawner(long personTkbType, SimTransform at, IReadOnlyList<object> components);

    /// <summary>S2-e — a role's request is answered only once the man EXISTS; this many milliseconds without him ⇒ refused "spawn timed out".</summary>
    public const long SpawnTimeoutMs = 10_000;

    public const string SpawnTimedOut = "spawn timed out";

    private sealed class Slot
    {
        public int Station; public string Name = ""; public ForceId Side; public PlayerSlotState State; public int UnitNetId; public double Until; public double RespawnAt;
    }

    private sealed class Offered
    {
        public FixedString32 NameKey; public string Name = ""; public bool Seen; public bool KeyedName;
        public FixedString64 NameKey64;                                  // an area's name (EntityInfo)
        public FixedString32 K0, K1, K2, K3; public List<string> Kits = new();   // a role's kit labels, rebuilt only when a name changed
    }

    /// <summary>A role request waiting for its man to exist (S2-e).</summary>
    private sealed class PendingSpawn
    {
        public int Station; public string RequestId = ""; public int UnitNetId; public int RoleNetId; public ForceId Side; public string RoleName = ""; public long DeadlineMs;
    }

    private readonly IGameplayWire _wire;
    private readonly NetworkEntityMap _map;
    private readonly int _local;
    [Fdp.Core.NotState("injected server election dependency")]
    private readonly DeployServerElection? _election;
    private readonly Func<long> _nowMs;
    [Fdp.Core.SessionState("the deploy server's join ledger per station: every unit id is checked against the live world before use; cleaned when a station leaves")]
    private readonly List<Slot> _slots = new();
    [Fdp.Core.SessionState("the deploy server's join ledger per station: every unit id is checked against the live world before use; cleaned when a station leaves")]
    private readonly Dictionary<int, Offered> _offered = new();
    [Fdp.Core.FrameScratch("key list: cleared at 422/508 before each use")]
    private readonly List<int> _scratch = new();
    [Fdp.Core.SessionState("the deploy server's join ledger per station: every unit id is checked against the live world before use; cleaned when a station leaves")]
    private readonly List<PendingSpawn> _pending = new();
    [Fdp.Core.SessionState("the deploy server's join ledger per station: every unit id is checked against the live world before use; cleaned when a station leaves")]
    [Fdp.Core.FrameScratch("the requests taken this frame: cleared before and after each read")]
    private readonly List<GameplaySample<DeployRequestTopic>> _taken = new();
    [Fdp.Core.FrameScratch("stations departing this frame: cleared before each ReadDepartures")]
    private readonly List<int> _departing = new();   // CE-11027 — scratch for ReadDepartures (a departure is rare; reused, never shrunk)
    [Fdp.Core.SessionState("the deploy server's join ledger: the stations whose status this server wrote; cleaned when a station leaves and at a world load")]
    private readonly HashSet<int> _answered = new();   // the stations whose DeployStatusTopic this server wrote (deleted when they leave)
    [Fdp.Core.SessionState("the deploy server's join ledger per station: every unit id is checked against the live world before use; cleaned when a station leaves")]
    private readonly List<(int Role, int Unit)> _spawned = new();   // the men THIS server made, per role (the count a role's Max is checked against)
    private readonly RoleSpawner? _spawner;
    [Fdp.Core.NotState("Random injected, or seeded from the run seed at the first spawn after a world load (SeededFromRun); picks a spawn point")]
    private Random _rng;
    [Fdp.Core.NotState("true when a test or host injected the Random: it is never re-seeded")]
    private readonly bool _rngInjected;
    [Fdp.Core.WorldDerived("true until the spawn choice was seeded for the current world: set at construction and at every world load")]
    private bool _reseed = true;
    [Fdp.Core.SessionState("a monotonic spawn counter: never reset, a reused number would collide")]
    private readonly Dictionary<int, int> _spawnCounter = new();
    [Fdp.Core.FrameScratch("person types list: cleared at 280 before each use")]
    private readonly List<long> _persons = new();   // scratch: the person TKB types present on a side
    private ISimClock? _clock;
    [Fdp.Core.SessionState("result of the deploy server election: cluster role of this node")]
    private bool _isServer;
    [Fdp.Core.WorldDerived("wall-clock throttle for the next election check")]
    private long _nextElectionMs, _nextOfferMs;
    [Fdp.Core.WorldDerived("dirty flag asking for an offers re-publish")]
    private bool _offersDirty;
    [Fdp.Core.WorldDerived("the world epoch last seen: a world replacement (a load, a restore) forgets every table")]
    private int _worldEpoch;
    [Fdp.Core.WorldDerived("true while the last frame was gated off by a replay restore")]
    private bool _wasGated;

    /// <summary>⭐ CE-11027 S12-3 — set by the node's record / replay controller (<see cref="Fdp.Interfaces.IReplayRestoreGated"/>): true while this node restores a recording. The server then publishes no offer and no slot, reads no request.</summary>
    public Func<bool>? IsReplayRestoring { get; set; }

    /// <param name="wire">This node's gameplay wire.</param>
    /// <param name="map">The node's network entity map (UnitNetId → entity).</param>
    /// <param name="localNode">This node's id.</param>
    /// <param name="election">Who serves; null ⇒ this node always does (a single-server test or a node with no cluster cache).</param>
    /// <param name="nowMs">Wall milliseconds, for the 1 s cadences (tests inject).</param>
    /// <param name="spawner">⭐ S2 — how a role's man is made (the pack passes the node's <c>EntityCreation</c>); null ⇒ role requests are refused "spawning unavailable".</param>
    /// <param name="rng">The spawn point's randomness (tests inject it); null ⇒ ⭐ CE-11027 S12-4 (Q114 J) seeded from the run seed (<see cref="SeededFromRun"/>) at the first spawn of each world, so one scenario replays its spawn points.</param>
    public DeployProcessorSystem(IGameplayWire wire, NetworkEntityMap map, int localNode, DeployServerElection? election = null, Func<long>? nowMs = null,
                                 RoleSpawner? spawner = null, Random? rng = null)
    {
        _spawner = spawner;
        _rng = rng ?? new Random(0);   // replaced at the first spawn by the world's seed (SeededFromRun)
        _rngInjected = rng is not null;
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
        if (IsReplayRestoring?.Invoke() == true)
        {
            // ⭐ CE-11027 S12-3 (Q114 H) — a replay restores the recording's world: nothing is offered or slotted against it, and the requests of a live Player are not kept for after.
            _wasGated = true;
            var gated = _wire.Requests;
            if (gated is not null) { _taken.Clear(); gated.TakeInto(_taken); _taken.Clear(); }
            return;
        }
        if (_wasGated) { _wasGated = false; _offersDirty = true; }   // the replay ended: offer again
        if (WorldEpoch.Moved(repo, ref _worldEpoch)) ForgetWorld(repo);   // ⭐ CE-11027 S12-3 (Q114 H) — a load: the tables belong to the world that ended

        double simNow = (_clock ??= SimClock.Of(repo)).TotalTime;   // ⭐ ONE boxed clock, kept: SimClock.Of returns the interface, so asking per frame would box a struct (24 B) every frame (R-2084)
        ReadRequests(repo, simNow);
        ReadDepartures(repo);
        ResolveSpawns(repo, now);
        Progress(repo, simNow);
        if (_offersDirty || now >= _nextOfferMs) { _nextOfferMs = now + PeriodMs; _offersDirty = false; PublishOffers(repo); }
    }

    // ── requests ────────────────────────────────────────────────────────────────────────────

    private void ReadRequests(EntityRepository repo, double simNow)
    {
        var reader = _wire.Requests;
        if (reader is null) return;
        _taken.Clear();
        if (reader.TakeInto(_taken) == 0) return;
        for (int i = 0; i < _taken.Count; i++)
            if (_taken[i].Alive) Handle(repo, _taken[i].Data, simNow);   // a request that ended (its writer left) is nothing to answer
        _taken.Clear();
    }

    private void Handle(EntityRepository repo, in DeployRequestTopic r, double simNow)
    {
        if (r.Leave) { Leave(repo, r.StationNodeId); return; }
        var side = (ForceId)r.Side;
        if (r.Observe)
        {
            SetSlot(repo, r.StationNodeId, "", side, PlayerSlotState.Choosing, 0, 0);
            Answer(r, accepted: true, "", 0, 0);
            return;
        }
        if (r.UnitNetId < 0 || (_map.TryGetEntity(r.UnitNetId, out var roleEntity) && repo.IsAlive(roleEntity)
                                && repo.IsComponentTypeRegistered<DeployRole>() && repo.HasComponent<DeployRole>(roleEntity)))
        {
            HandleRole(repo, r, side);
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
    {
        _answered.Add(r.StationNodeId);   // ⭐ the station's answer is durable (TransientLocal): deleted when the station leaves
        WriteAnswer(r, accepted, reason, holder, unitNetId);
    }

    private void WriteAnswer(in DeployRequestTopic r, bool accepted, string reason, int holder, int unitNetId)
        => _wire.Status?.Write(new DeployStatusTopic
        {
            StationNodeId = r.StationNodeId, RequestId = r.RequestId ?? "", Accepted = accepted, Reason = reason, UnitNetId = unitNetId, HolderStation = holder,
        });

    // ── roles (S2) ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ S2-a/c/e — a request that names a ROLE: the role's side must match, its <c>MaxCount</c> not be reached, a free point must exist in a spawn area of the side — then ONE man is asked for through the
    /// creation path and the answer waits for him (a <see cref="PendingSpawn"/>). A role is a Deploy Role entity (its network id) or, where the side declares none, an implicit one: <c>-(person TKB type)</c>.
    /// </summary>
    private void HandleRole(EntityRepository repo, in DeployRequestTopic r, ForceId side)
    {
        long personType; string roleName; int max; ForceId roleSide;
        if (r.UnitNetId < 0)
        {
            if (r.Kit != 0) { Answer(r, false, "NoSuchKit", 0, r.UnitNetId); return; }   // an implicit role carries ONE kit: its type
            personType = -(long)r.UnitNetId; max = 0; roleSide = side;
            if (SideDeclaresRoles(repo, side) || !PersonTypesOn(repo, side).Contains(personType)) { Answer(r, false, "NoSuchUnit", 0, r.UnitNetId); return; }
            roleName = TypeName(repo, personType);
        }
        else
        {
            _map.TryGetEntity(r.UnitNetId, out var roleEntity);
            var role = repo.GetComponentRO<DeployRole>(roleEntity);
            personType = role.KitTkb(r.Kit); max = role.MaxCount; roleSide = role.Side; roleName = role.Name.ToString();   // ⭐ S3-a — kit 0 = the role's own type, 1…3 its extra kits
            if (r.Kit > 3 || (r.Kit > 0 && personType == 0)) { Answer(r, false, "NoSuchKit", 0, r.UnitNetId); return; }
            if (personType == 0) { Answer(r, false, "the role has no person type", 0, r.UnitNetId); return; }
        }
        if (roleSide != side) { Answer(r, false, "WrongSide", 0, r.UnitNetId); return; }
        if (max > 0 && AliveOf(repo, r.UnitNetId) + PendingOf(r.UnitNetId, r.StationNodeId) >= max) { Answer(r, false, "RoleFull", 0, r.UnitNetId); return; }
        if (_spawner is null) { Answer(r, false, "spawning unavailable", 0, r.UnitNetId); return; }
        Entity? chosen = null;   // ⭐ S3-c — the Player's area, if it named one
        if (r.AreaNetId != 0)
        {
            if (!_map.TryGetEntity(r.AreaNetId, out var areaEntity)) { Answer(r, false, SpawnPoints.NoSuchArea, 0, r.UnitNetId); return; }
            chosen = areaEntity;
        }
        if (_reseed && !_rngInjected) { _rng = SeededFromRun(repo); _reseed = false; }
        if (!SpawnPoints.Pick(repo, side, _rng, out var pos, out float yaw, out string why, chosen)) { Answer(r, false, why, 0, r.UnitNetId); return; }

        _spawnCounter.TryGetValue(r.UnitNetId, out int n);
        _spawnCounter[r.UnitNetId] = ++n;
        float rad = yaw * (MathF.PI / 180f);
        var at = new SimTransform { Position = pos, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rad) };
        var comps = new object[] { new EntityInfo { Name = new FixedString64($"{roleName} {n}"), ForceId = side } };
        int unitNetId = _spawner(personType, at, comps);   // ⭐ CE-4266 — the man's network id, fixed by the request (pre-allocated)
        if (unitNetId == 0) { Answer(r, false, "spawning unavailable", 0, r.UnitNetId); return; }
        for (int i = _pending.Count - 1; i >= 0; i--) if (_pending[i].Station == r.StationNodeId) _pending.RemoveAt(i);   // a new request replaces the station's old one
        _pending.Add(new PendingSpawn { Station = r.StationNodeId, RequestId = r.RequestId ?? "", UnitNetId = unitNetId, RoleNetId = r.UnitNetId, Side = side, RoleName = roleName, DeadlineMs = _nowMs() + SpawnTimeoutMs });
    }

    /// <summary>Each tick while a spawn is pending: the man exists (his pre-allocated network id is in the map, alive) ⇒ the slot is TakingControl on him and the Player is told; 10 s ⇒ refused.</summary>
    private void ResolveSpawns(EntityRepository repo, long nowMs)
    {
        if (_pending.Count == 0) return;
        double simNow = (_clock ??= SimClock.Of(repo)).TotalTime;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            int unitNet = _map.TryGetEntity(p.UnitNetId, out var man) && repo.IsAlive(man) ? p.UnitNetId : 0;
            var request = new DeployRequestTopic { RequestId = p.RequestId, StationNodeId = p.Station, UnitNetId = p.RoleNetId };
            if (unitNet != 0)
            {
                _pending.RemoveAt(i);
                _spawned.Add((p.RoleNetId, unitNet));
                SetSlot(repo, p.Station, p.RoleName, p.Side, PlayerSlotState.TakingControl, unitNet, simNow + ReservationSeconds);
                Answer(request, true, "", 0, unitNet);
                _offersDirty = true;
            }
            else if (nowMs >= p.DeadlineMs)
            {
                _pending.RemoveAt(i);
                Answer(request, false, SpawnTimedOut, 0, p.RoleNetId);
            }
        }
    }

    /// <summary>⭐ CE-11027 S12-3 (Q114 G) — the spawns of <paramref name="roleNetId"/> still waiting for their man (a station's own earlier request is replaced by its new one, so it does not count against it): a role with one place left must not be filled twice in one frame.</summary>
    private int PendingOf(int roleNetId, int exceptStation)
    {
        int n = 0;
        for (int i = 0; i < _pending.Count; i++)
            if (_pending[i].RoleNetId == roleNetId && _pending[i].Station != exceptStation) n++;
        return n;
    }

    /// <summary>How many of the men this server made for <paramref name="roleNetId"/> are alive now.</summary>
    private int AliveOf(EntityRepository repo, int roleNetId)
    {
        int n = 0;
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i].Role == roleNetId && _map.TryGetEntity(_spawned[i].Unit, out var e) && repo.IsAlive(e) && PossessionSystem.Alive(repo, e)) n++;
        return n;
    }

    private static bool SideDeclaresRoles(EntityRepository repo, ForceId side)
    {
        if (!repo.IsComponentTypeRegistered<DeployRole>()) return false;
        foreach (var e in repo.Query().With<DeployRole>().Build())
            if (repo.IsAlive(e) && repo.GetComponentRO<DeployRole>(e).Side == side) return true;
        return false;
    }

    /// <summary>The person TKB types alive on <paramref name="side"/> (the implicit roles) — "is a person" is <c>Locomotions.Of == Pedestrian</c>, the one test the engine has.</summary>
    private List<long> PersonTypesOn(EntityRepository repo, ForceId side)
    {
        _persons.Clear();
        if (!repo.IsComponentTypeRegistered<TkbIdentity>() || !repo.IsComponentTypeRegistered<EntityInfo>()) return _persons;
        foreach (var e in repo.Query().With<TkbIdentity>().With<EntityInfo>().Build())
        {
            if (!repo.IsAlive(e) || repo.GetComponentRO<EntityInfo>(e).ForceId != side) continue;
            if (Fdp.Toolkit.Motion.Locomotions.Of(repo, e) != Fdp.Toolkit.Tkb.Domain.LocomotionClass.Pedestrian) continue;
            long t = repo.GetComponentRO<TkbIdentity>(e).TkbType.Value;
            if (!_persons.Contains(t)) _persons.Add(t);
        }
        return _persons;
    }

    private static string TypeName(EntityRepository repo, long tkbType)
        => repo.HasSingletonManaged<ITkbDatabase>() && repo.GetSingletonManaged<ITkbDatabase>() is { } db
           && db.TryGetByType((TkbTypeId)tkbType, out var t) && !string.IsNullOrWhiteSpace(t.Name) ? t.Name : $"Type {tkbType}";

    // ── slots ───────────────────────────────────────────────────────────────────────────────

    private void SetSlot(EntityRepository repo, int station, string name, ForceId side, PlayerSlotState state, int unitNetId, double until, double respawnAt = 0.0)
    {
        Slot? slot = null;
        foreach (var s in _slots) if (s.Station == station) { slot = s; break; }
        if (slot is null) { slot = new Slot { Station = station }; _slots.Add(slot); }
        slot.Name = name; slot.Side = side; slot.State = state; slot.UnitNetId = unitNetId; slot.Until = until; slot.RespawnAt = respawnAt;
        // The reservation is made HERE as well as by the slot reader (which will say the same a frame later): a second request for the unit in the SAME frame
        // must already see it reserved — the DDS round trip would let two stations both be accepted.
        StationReservations.Of(repo).Set(station, side, state is PlayerSlotState.Choosing or PlayerSlotState.Dead ? 0 : unitNetId);
        WriteSlot(slot);
    }

    private void WriteSlot(Slot s)
        => _wire.SlotWriter?.Write(new PlayerSlotTopic
        {
            StationNodeId = s.Station, Name = s.Name, Side = (byte)s.Side, State = (byte)s.State, UnitNetId = s.UnitNetId, ReservedUntilSim = s.Until, RespawnAtSim = s.RespawnAt,
        });

    private void DisposeSlotAt(EntityRepository repo, int index)
    {
        int station = _slots[index].Station;
        _slots.RemoveAt(index);
        StationReservations.Of(repo).Remove(station);
        _wire.SlotWriter?.DisposeInstance(new PlayerSlotTopic { StationNodeId = station });
        _offersDirty = true;
    }

    /// <summary>
    /// The slot life on the SERVER (§10.2): TakingControl → Playing (the unit is <c>PossessedBy</c> the station) or lapse; Playing → Dead (the unit died — or is gone) with <c>RespawnAtSim = now + delay</c>;
    /// Playing → Choosing (the station RELEASED it — closes §9.4's open gap); Dead → Choosing at <c>RespawnAtSim</c> (a delay of 0 stays Dead: the Player observes for good).
    /// </summary>
    private void Progress(EntityRepository repo, double simNow)
    {
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            var s = _slots[i];
            switch (s.State)
            {
                case PlayerSlotState.TakingControl:
                    if (_map.TryGetEntity(s.UnitNetId, out var unit) && repo.IsAlive(unit) && PossessionSystem.HolderOf(repo, unit) == s.Station)
                    {
                        s.State = PlayerSlotState.Playing; s.Until = 0;
                        WriteSlot(s);
                        _offersDirty = true;
                    }
                    else if (simNow > s.Until) DisposeSlotAt(repo, i);   // the reservation lapsed untaken
                    break;
                case PlayerSlotState.Playing:
                    if (!_map.TryGetEntity(s.UnitNetId, out var man) || !repo.IsAlive(man) || !PossessionSystem.Alive(repo, man))
                    {
                        float delay = RespawnDelay(repo);
                        SetSlot(repo, s.Station, s.Name, s.Side, PlayerSlotState.Dead, s.UnitNetId, 0, respawnAt: delay > 0f ? simNow + delay : 0.0);
                        _offersDirty = true;
                    }
                    else if (PossessionSystem.HolderOf(repo, man) != s.Station)
                    {
                        SetSlot(repo, s.Station, "", s.Side, PlayerSlotState.Choosing, 0, 0);   // released: the unit is free again
                        _offersDirty = true;
                    }
                    break;
                case PlayerSlotState.Dead:
                    if (s.RespawnAt > 0.0 && simNow >= s.RespawnAt)
                    {
                        SetSlot(repo, s.Station, "", s.Side, PlayerSlotState.Choosing, 0, 0);
                        _offersDirty = true;
                    }
                    break;
            }
        }
    }

    /// <summary>⭐ S2-d — the respawn delay: <c>GameplaySettings</c> on the World Environment, else 10 s. (Read on a death only.)</summary>
    private static float RespawnDelay(EntityRepository repo)
    {
        if (repo.IsComponentTypeRegistered<GameplaySettings>())
            foreach (var e in repo.Query().With<GameplaySettings>().WithLifecycle(EntityLifecycle.All).Build())
                return repo.GetComponentRO<GameplaySettings>(e).RespawnDelaySeconds;
        return GameplaySettings.DefaultRespawnDelaySeconds;
    }

    private void ReadDepartures(EntityRepository repo)
    {
        if ((_slots.Count == 0 && _answered.Count == 0) || !repo.Bus.IsRegistered<NodeDeparted>()) return;
        foreach (ref readonly var gone in repo.ReadEvents<NodeDeparted>())
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
                if (((Fdp.Core.StationId)_slots[i].Station).Node == gone.NodeId) DisposeSlotAt(repo, i);   // ⭐ CE-11027 A′ — every station on the node
            _departing.Clear();
            foreach (int a in _answered) if (((Fdp.Core.StationId)a).Node == gone.NodeId) _departing.Add(a);   // ⭐ CE-11027 A′ — the answer of every station on the node
            foreach (int a in _departing) DisposeAnswer(a);
        }
    }

    /// <summary>
    /// ⭐ CE-4270 ③ (found by the scripted full cycle, step 10) — the station left: its <c>DeployStatusTopic</c> (TransientLocal, keyed by
    /// station) is deleted too, slot or not (a refused station has an answer and no slot). 📐 Before, every departed station left its last
    /// answer ALIVE for every late joiner, one more per join.
    /// </summary>
    private void DisposeAnswer(int station)
    {
        if (_answered.Remove(station)) _wire.Status?.DisposeInstance(new DeployStatusTopic { StationNodeId = station });
    }

    /// <summary>
    /// ⭐ CE-11027 S12-3 (Q114 E) — a station left: its slot, its durable answer and its pending spawn go at once (as <see cref="ReadDepartures"/> does for a node), and <see cref="ReleaseStationEvent"/> is published on the world bus so
    /// the possession system releases every unit it holds. A station that joined nothing: a no-op, but the event is still sent (it may hold a unit from an earlier deploy).
    /// </summary>
    private void Leave(EntityRepository repo, int station)
    {
        for (int i = _slots.Count - 1; i >= 0; i--) if (_slots[i].Station == station) DisposeSlotAt(repo, i);
        DisposeAnswer(station);
        for (int i = _pending.Count - 1; i >= 0; i--) if (_pending[i].Station == station) _pending.RemoveAt(i);
        if (!repo.Bus.IsRegistered<ReleaseStationEvent>()) repo.Bus.Register<ReleaseStationEvent>();
        repo.Bus.Publish(new ReleaseStationEvent { StationNodeId = (Fdp.Core.StationId)station });
        _offersDirty = true;
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
                writer?.Write(new DeployOfferTopic { UnitNetId = netId, Side = (byte)playable.Side, SlotName = o.Name, HolderStation = holder, Kind = (byte)DeployOfferKind.Placed });
            }
        }
        PublishRoleOffers(repo, writer);
        PublishAreaOffers(repo, writer);
        _scratch.Clear();
        foreach (var kv in _offered) if (!kv.Value.Seen) _scratch.Add(kv.Key);
        foreach (int gone in _scratch)
        {
            _offered.Remove(gone);
            writer?.DisposeInstance(new DeployOfferTopic { UnitNetId = gone });
        }
    }

    /// <summary>⭐ S2-a — every Deploy Role entity is offered (Kind Role, its network id, Count alive / Max); a side that declares none gets one IMPLICIT role per person TKB type present on it (id <c>-(type)</c>, unlimited).</summary>
    private void PublishRoleOffers(EntityRepository repo, IGameplayOut<DeployOfferTopic>? writer)
    {
        var declared = new HashSet<ForceId>();
        if (repo.IsComponentTypeRegistered<DeployRole>() && repo.IsComponentTypeRegistered<NetworkIdentity>())
            foreach (var e in repo.Query().With<DeployRole>().Build())
            {
                if (!repo.IsAlive(e) || !repo.HasComponent<NetworkIdentity>(e)) continue;
                var role = repo.GetComponentRO<DeployRole>(e);
                declared.Add(role.Side);
                int netId = repo.GetComponentRO<NetworkIdentity>(e).Value;
                if (!_offered.TryGetValue(netId, out var o)) _offered[netId] = o = new Offered();
                if (!o.Seen && (o.Name.Length == 0 || !o.NameKey.Equals(role.Name))) { o.NameKey = role.Name; o.Name = role.Name.ToString(); }
                o.Seen = true;
                RefreshKits(o, role);
                writer?.Write(new DeployOfferTopic { UnitNetId = netId, Side = (byte)role.Side, SlotName = o.Name, HolderStation = 0, Kind = (byte)DeployOfferKind.Role, Count = AliveOf(repo, netId), Max = role.MaxCount, Kits = o.Kits });
            }
        foreach (var side in new[] { ForceId.Friend, ForceId.Hostile })
        {
            if (declared.Contains(side)) continue;
            var types = PersonTypesOn(repo, side);
            for (int i = 0; i < types.Count; i++)
            {
                int id = -(int)types[i];
                if (!_offered.TryGetValue(id, out var o)) { _offered[id] = o = new Offered { Name = TypeName(repo, types[i]), KeyedName = false }; o.Kits.Add(o.Name); }   // ⭐ S3 — an implicit role carries ONE kit: its type
                o.Seen = true;
                writer?.Write(new DeployOfferTopic { UnitNetId = id, Side = (byte)side, SlotName = o.Name, HolderStation = 0, Kind = (byte)DeployOfferKind.Role, Count = AliveOf(repo, id), Max = 0, Kits = o.Kits });
            }
        }
    }

    /// <summary>
    /// ⭐ S3-a — the role's kit labels by kit number, rebuilt only when a name changed: index 0 = the role's own name, 1…3 = <c>KitNName</c> (or "Kit n"), "" = an unset kit. Empty when no extra kit is set — a role
    /// with no kits offers none (the screen shows a plain row).
    /// </summary>
    private static void RefreshKits(Offered o, in DeployRole role)
    {
        bool any = role.Kit1Tkb != 0 || role.Kit2Tkb != 0 || role.Kit3Tkb != 0;
        if (o.Kits.Count > 0 == any && o.K1.Equals(role.Kit1Name) && o.K2.Equals(role.Kit2Name) && o.K3.Equals(role.Kit3Name) && o.K0.Equals(role.Name)) return;
        o.K0 = role.Name; o.K1 = role.Kit1Name; o.K2 = role.Kit2Name; o.K3 = role.Kit3Name;
        o.Kits = new List<string>(4);
        if (!any) return;
        o.Kits.Add(role.Name.ToString());
        o.Kits.Add(role.Kit1Tkb == 0 ? "" : KitLabel(role.Kit1Name, 1));
        o.Kits.Add(role.Kit2Tkb == 0 ? "" : KitLabel(role.Kit2Name, 2));
        o.Kits.Add(role.Kit3Tkb == 0 ? "" : KitLabel(role.Kit3Name, 3));
    }

    private static string KitLabel(FixedString32 name, int n) => name.IsEmpty ? $"Kit {n}" : name.ToString();

    /// <summary>⭐ S3 — every <see cref="SpawnArea"/> entity (of EVERY side) is offered as Kind Area: its network id, side and name, so the Player's screen can list the areas of the side it chose and its request can name one.</summary>
    private void PublishAreaOffers(EntityRepository repo, IGameplayOut<DeployOfferTopic>? writer)
    {
        if (!repo.IsComponentTypeRegistered<SpawnArea>() || !repo.IsComponentTypeRegistered<NetworkIdentity>()) return;
        foreach (var e in repo.Query().With<SpawnArea>().Build())
        {
            if (!repo.IsAlive(e) || !repo.HasComponent<NetworkIdentity>(e)) continue;
            int netId = repo.GetComponentRO<NetworkIdentity>(e).Value;
            var area = repo.GetComponentRO<SpawnArea>(e);
            var name = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(e) ? repo.GetComponentRO<EntityInfo>(e).Name : default;
            if (!_offered.TryGetValue(netId, out var o)) _offered[netId] = o = new Offered();
            if (!o.Seen && (o.Name.Length == 0 || !o.NameKey64.Equals(name))) { o.NameKey64 = name; o.Name = name.IsEmpty ? "Spawn area" : name.ToString(); }
            o.Seen = true;
            writer?.Write(new DeployOfferTopic { UnitNetId = netId, Side = (byte)area.Side, SlotName = o.Name, HolderStation = 0, Kind = (byte)DeployOfferKind.Area, Count = 0, Max = 0, Kits = o.Kits });
        }
    }

    /// <summary>The salt that keeps the spawn choice's draws apart from every other <see cref="Fdp.Toolkit.Determinism.SimRng"/> call site (the rule on <c>SimRng.FromSim</c>).</summary>
    private const int SpawnSalt = 0x5A17;

    /// <summary>
    /// ⭐ CE-11027 S12-4 (Q114 J) — the spawn choice's <see cref="Random"/> for <paramref name="world"/>: seeded from the run seed the way the other deterministic systems read it (the world's
    /// recorded <c>RunSeed</c> singleton, else <c>SimRng.RunSeed</c> — <c>StochasticCombat.cs:300</c>), mixed through <see cref="Fdp.Toolkit.Determinism.SimRng"/>: the same seed ⇒ the same spawn points;
    /// another seed ⇒ (usually) others. <c>SimRng.Deterministic = false</c> (the soak escape hatch) restores an unseeded one.
    /// </summary>
    public static Random SeededFromRun(EntityRepository world)
    {
        if (!Fdp.Toolkit.Determinism.SimRng.Deterministic) return new Random();
        uint seed = world.HasSingleton<Fdp.Toolkit.Aggregate.RunSeed>() ? world.GetSingleton<Fdp.Toolkit.Aggregate.RunSeed>().Value : Fdp.Toolkit.Determinism.SimRng.RunSeed;
        return new Random((int)Fdp.Toolkit.Determinism.SimRng.FromSim(0, SpawnSalt, 0f, seed).NextUInt());
    }

    // ── standing down ───────────────────────────────────────────────────────────────────────

    /// <summary>A lower-id server appeared: retract everything this one published, so the new server is the only voice (it re-offers the units itself).</summary>
    private void ForgetWorld(EntityRepository repo)
    {
        StandDown(repo);          // slots, answers and offers: gone from the wire
        _pending.Clear();         // a spawn in flight belongs to the world that ended
        _reseed = true;           // ⭐ S12-4 (Q114 J): the next world's spawn choice starts from ITS seed
        _spawned.Clear();         // so do the men this server made
        _offersDirty = true;
    }

    private void StandDown(EntityRepository repo)
    {
        for (int i = _slots.Count - 1; i >= 0; i--) DisposeSlotAt(repo, i);
        foreach (int station in _answered) _wire.Status?.DisposeInstance(new DeployStatusTopic { StationNodeId = station });
        _answered.Clear();
        foreach (int netId in _offered.Keys) _scratch.Add(netId);
        foreach (int netId in _scratch) _wire.Offers?.DisposeInstance(new DeployOfferTopic { UnitNetId = netId });
        _scratch.Clear();
        _offered.Clear();
    }
}
