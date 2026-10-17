using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Traffic;
using Hrot.Core.Tkb;

namespace Hrot.Traffic;

/// <summary>⭐ AQ93 §15 K4 — a shot or a blast the serving node heard of: where (NET coordinates, for SUMO; and terrain-local, for the
/// civilians' fright) and how far it frightens pedestrians.</summary>
public readonly record struct TrafficIncident(double NetX, double NetY, double Radius, Vector3 At);

/// <summary>⭐ AQ93 §15 K5 — one pedestrian SUMO gave up (H14 S1): its name (= its network id, K2), its state when removed (NET
/// coordinates, heading in radians, 0 = +x, counter-clockwise) and the incident that frightened it (terrain-local).</summary>
public readonly record struct TrafficConversion(string Name, double X, double Y, double Z, double Heading, Vector3 FrightFrom);

/// <summary>⭐ AQ93 §15 K7 — one of SUMO's agents was HIT (it is a replica near our units): its name (= its network id), whether it is a
/// car, and where the harm came from (terrain-local), for the fright.</summary>
public readonly record struct TrafficHit(string Name, bool IsVehicle, Vector3 FrightFrom);

/// <summary>
/// ⭐ AQ93 §15 K4/K5/K6 (R-4031/R-4032, CV-12) — on the node that SERVES traffic, each frame: every <see cref="WeaponFireNotification"/>
/// (where the round left) and <see cref="DetonationNotification"/> (where it burst) is an incident for the stepping thread, which removes
/// the pedestrians within its radius from SUMO (<see cref="TrafficSession.Convert"/>, capped); every pedestrian it gave up becomes ONE
/// <see cref="EntityCreationRequest"/> — a civilian of the <see cref="CivilianTkbCatalog"/> family keeping the pedestrian's network id,
/// born terrified of the incident (the <c>Fear</c> / <c>FrightFrom</c> attributes, CE-4024), so his <c>CivilianHsm</c> panics and runs.
/// </summary>
/// <remarks>
/// <para>⭐ Owner 0: the system picks — the default processor (CGF in a cluster, the editor alone, R-4029); the traffic node, which is
/// optional (R-4027), never keeps a brain. ⭐ The request is TRANSIENT: a person SUMO walked is traffic, never the scenario's.</para>
/// <para>⭐ Its own replica goes first (<see cref="TrafficReplicas.Release"/>), so the id is free when the civilian is registered; every
/// other node drops its replica on the pedestrian's Despawn (H14 S2).</para>
/// <para>⭐ Both events reach every node (the shared pack, CE-1148) — on the traffic node they arrive by NED ingress.</para>
/// <para>⭐ K7 (5c) — a <see cref="DamageAssessedEvent"/> on a pedestrian replica gives that pedestrian up the same way (never capped: he is
/// hurt), and the hit is HELD until our civilian exists under the same network id, then re-published to him as a local hit, so the
/// existing damage path (<c>DamageAssessedEgressTranslator</c> → the brain's <c>HealthApplicationSystem</c>) applies it. ⏳ A hit car waits
/// for SumoSharp H15 (removing a car by name).</para>
/// </remarks>
[UpdateInPhase(SystemPhase.Input)]
public sealed class TrafficPanicSystem : IEcsModuleSystem
{
    /// <summary>Incidents of one frame closer than this (and no wider) are one incident: a burst of fire is one fright.</summary>
    public const float MergeMetres = 5f;

    private readonly TrafficRuntime _runtime;
    private readonly Func<TrafficReplicas?> _replicas;
    private readonly List<TrafficIncident> _frame = new();
    private readonly List<TrafficConversion> _conversions = new();
    private readonly Fdp.Toolkit.Replication.Services.NetworkEntityMap? _entityMap;
    private readonly Dictionary<long, (DamageAssessedEvent Hit, double Until)> _held = new();
    private readonly List<long> _done = new();

    /// <summary>How long a hit is held for the civilian it converts to (cluster seconds): a creation request round trip, with margin.</summary>
    public const double HoldSeconds = 10.0;

    /// <param name="entityMap">The node's network entity map: a hit is re-published to the civilian it finds there (null in a world with no
    /// network layer, e.g. a rail — then a held hit is never delivered).</param>
    public TrafficPanicSystem(TrafficRuntime runtime, Func<TrafficReplicas?> replicas,
                              Fdp.Toolkit.Replication.Services.NetworkEntityMap? entityMap = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _replicas = replicas ?? throw new ArgumentNullException(nameof(replicas));
        _entityMap = entityMap;
    }

    /// <summary>Hits waiting for their civilian (by network id) — rails and diagnostics.</summary>
    public IReadOnlyCollection<long> HeldHits => _held.Keys;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;
        var net = TrafficNetCache.Of(world);
        if (net is null || !TryDemand(world, out var demand)) return;

        _frame.Clear();
        if (world.Bus.IsRegistered<WeaponFireNotification>())
            foreach (var shot in world.Bus.Read<WeaponFireNotification>())
                if (Origin(world, shot) is { } at) Add(net.Placement, at, demand.ShotRadius);
        if (world.Bus.IsRegistered<DetonationNotification>())
            foreach (var blast in world.Bus.Read<DetonationNotification>())
                Add(net.Placement, new Vector3(blast.HitX, blast.HitY, blast.HitZ), demand.BlastRadius);
        double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0.0;
        if (_runtime.Current is not null)   // no SUMO running ⇒ no pedestrian to frighten (and nothing queued for later)
        {
            foreach (var i in _frame) _runtime.ReportIncident(i);
            if (world.Bus.IsRegistered<DamageAssessedEvent>() && world.IsComponentTypeRegistered<TrafficCar>())
                foreach (var hit in world.Bus.Read<DamageAssessedEvent>()) Hit(world, hit, now);
        }
        Deliver(world, now);

        _conversions.Clear();
        _runtime.TakeConversions(_conversions);
        foreach (var c in _conversions) Request(world, net.Placement, c);
    }

    // ⭐ K7 — a hit on a SUMO replica: give him up, and hold the hit for the civilian he becomes.
    private void Hit(EntityRepository world, in DamageAssessedEvent hit, double now)
    {
        var e = hit.HitEntity;
        if (!world.IsAlive(e) || !world.HasComponent<TrafficCar>(e) || !world.HasComponent<NetworkIdentity>(e)) return;
        long id = world.GetComponentRO<NetworkIdentity>(e).Value;
        if (_held.ContainsKey(id)) return;   // a burst on one man is one conversion; his first hit is the one he is born with
        bool car = world.GetComponentRO<TrafficCar>(e).Kind == TrafficCar.KindCar;
        if (car) return;   // ⏳ SumoSharp H15 — a car cannot be removed by name yet
        var from = world.IsAlive(hit.Source) && world.HasComponent<SimTransform>(hit.Source)
            ? world.GetComponentRO<SimTransform>(hit.Source).Position
            : world.GetComponentRO<SimTransform>(e).Position;
        _runtime.ReportHit(new TrafficHit(id.ToString(CultureInfo.InvariantCulture), car, from));
        _held[id] = (hit, now + HoldSeconds);
    }

    // The held hits whose civilian exists now: re-published to him, as a hit of THIS node, so the egress carries it to his brain owner.
    private void Deliver(EntityRepository world, double now)
    {
        if (_held.Count == 0) return;
        _done.Clear();
        foreach (var (id, held) in _held)
        {
            if (now > held.Until) { _done.Add(id); continue; }
            if (_entityMap is null || !_entityMap.TryGetEntity(id, out var civilian) || !world.IsAlive(civilian)
                || world.HasComponent<TrafficCar>(civilian)) continue;   // still the replica, or not born yet
            var hit = held.Hit;
            hit.HitEntity = civilian;
            hit.IsRemote = false;
            world.Bus.Publish(hit);
            _done.Add(id);
        }
        foreach (var id in _done) _held.Remove(id);
    }

    private void Add(SumoNetPlacement placement, Vector3 at, float radius)
    {
        if (radius <= 0f) return;
        foreach (var i in _frame)
            if (i.Radius >= radius && Vector2.Distance(new(i.At.X, i.At.Y), new(at.X, at.Y)) < MergeMetres) return;
        var (nx, ny) = placement.ToNet(at.X, at.Y);
        _frame.Add(new TrafficIncident(nx, ny, radius, at));
    }

    // Where the round left — the shot's own field, or the shooter where an older sender left it empty.
    private static Vector3? Origin(EntityRepository world, in WeaponFireNotification shot)
    {
        if (shot.Origin != Vector3.Zero) return shot.Origin;
        if (world.IsAlive(shot.Shooter) && world.HasComponent<SimTransform>(shot.Shooter))
            return world.GetComponentRO<SimTransform>(shot.Shooter).Position;
        return null;
    }

    private void Request(EntityRepository world, SumoNetPlacement placement, in TrafficConversion c)
    {
        if (!long.TryParse(c.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var netId) || netId <= 0) return;
        _replicas()?.Release(netId);
        var (x, y) = placement.ToLocal(c.X, c.Y);
        var (ax, ay) = placement.ToLocal(c.X + Math.Cos(c.Heading), c.Y + Math.Sin(c.Heading));   // the heading through the placement
        double yaw = Math.Atan2(ay - y, ax - x);
        var at = new Vector3((float)x, (float)y, (float)c.Z);
        world.Bus.PublishManaged(new EntityCreationRequest
        {
            RequestId = Guid.NewGuid(),
            OwnerAppInstanceId = 0,                                       // the system picks: the brain node (R-4029)
            TkbType = TypeOf(netId),
            PreAllocatedNetworkId = netId,                                // K2: the pedestrian's id IS the civilian's
            InitType = Fdp.Toolkit.Replication.ReliableInitType.None,     // a panicking man waits for nobody
            IsTransient = true,                                           // traffic, never the scenario's
            InitialComponents = new List<object> { new SimTransform { Position = at, Rotation = SimMath.FromYaw((float)yaw) } },
            InitialAttributesJson = FrightJson(yaw, c.FrightFrom),
        });
    }

    /// <summary>The fright the civilian is born with (CE-4024) and his heading, as creation attributes (compass degrees for the heading).</summary>
    public static string FrightJson(double yawRad, Vector3 from)
        => string.Create(CultureInfo.InvariantCulture,
            $"{{\"Heading\":{90.0 - yawRad * 180.0 / Math.PI:0.###},\"Fear\":1,\"FrightFrom\":{{\"X\":{from.X:0.###},\"Y\":{from.Y:0.###},\"Z\":{from.Z:0.###}}}}}");

    /// <summary>⭐ K5 — the civilian type for a pedestrian (SUMO's carry no age or sex): four in ten men, four in ten women, one child, one
    /// elderly, by the id — the same on every node.</summary>
    public static int TypeOf(long networkId) => (int)(networkId % 10) switch
    {
        < 4 => CivilianTkbCatalog.TkbCivilianAdultMale,
        < 8 => CivilianTkbCatalog.TkbCivilianAdultFemale,
        8 => CivilianTkbCatalog.TkbCivilianChild,
        _ => CivilianTkbCatalog.TkbCivilianElderly,
    };

    private static bool TryDemand(EntityRepository world, out TrafficDemand demand)
    {
        demand = default;
        if (!world.IsComponentTypeRegistered<TrafficDemand>()) return false;
        foreach (var e in world.Query().With<TrafficDemand>().Build()) { demand = world.GetComponentRO<TrafficDemand>(e); return demand.TargetPedestrians > 0; }
        return false;
    }
}
