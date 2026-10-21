using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using VehicleHandle = Sim.Core.VehicleHandle;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4038 / R-4041 / R-4043 (the vicinity half), §12 N2/N5/T1/T2 — turns the SUMO cars near HROT into real ENTITIES: a car within
/// <see cref="Radius"/> of any HROT body becomes a replica (so raycasts, EQS, sensors and blasts see it), stays one until it is past
/// <see cref="Radius"/> + <see cref="Hysteresis"/>, and is destroyed then or when SUMO retires the car. Cars further out are drawn but
/// are not entities.
/// </summary>
/// <remarks>
/// <para>⭐ Every node applies the SAME rule to the SAME wire and gets the SAME entity: its network id is the car's SUMO vehicle id,
/// allocated by the traffic node when SUMO spawned it (N2); its owner is the node that SENT it (N5) — so the traffic node's own replica
/// is owned and every other node's is a replica of it; its TKB type rides the spawn record (T1; the inert "SUMO car" type only when the
/// sender mapped none); its colour, when the sender gave one, is <see cref="EntityAppearance"/> (T2 — otherwise the map's per-car paint,
/// seeded by the network id, is the same on every node). It is registered in the node's <see cref="NetworkEntityMap"/>, so a hit
/// addressed to its network id finds it.</para>
/// <para>⭐ It is on the <see cref="ReplicationRoute.SumoSharp"/> route, so NED never publishes it (EM-4), and carries
/// <see cref="ScenarioIgnoreTag"/>: the car is SUMO's, never the scenario's.</para>
/// <para>⭐ An ANCHOR is an HROT body — <c>SimTransform</c> + <c>PhysicsCollider</c> on the default route — so a map graphic never pulls
/// cars in, and a replica never anchors another.</para>
/// </remarks>
public sealed class TrafficReplicas
{
    /// <summary>R-4041 — the vicinity radius.</summary>
    public const float Radius = 300f;

    /// <summary>R-4041 — a replica is kept until this far past <see cref="Radius"/>.</summary>
    public const float Hysteresis = 50f;

    private const float CarHeight = 1.5f;
    private const float PedRadius = 0.3f, PedHeight = 1.75f;

    /// <summary>⭐ AQ93 §15 K3/K7 — a replica's hit points. 📌 Measured 2026-10-17 on the cluster: without <c>Health</c> a burst never hit a
    /// SUMO agent (<c>AreaEffectSystem</c> damages only entities that have one), so no hit ever reached the traffic node. The value only
    /// gates "alive": the hit converts the agent, and OUR entity takes it with its own TKB health.</summary>
    public const float ReplicaHealth = 100f;

    /// <summary>⭐ AQ93 §15 K3 / §15.8 (CE-4031) — a SUMO pedestrian's type, by his id (<see cref="TrafficPanicSystem.TypeOf"/>): the far
    /// figure, the replica and the civilian a conversion makes are ONE person — same type, and the map seeds his clothes and face by the
    /// same id. ⛔ Before: the replica was always the adult man, so he changed sex and age the moment he was converted.</summary>
    public static long PedestrianTypeOf(long networkId) => TrafficPanicSystem.TypeOf(networkId);

    /// <summary>⭐ AQ93 §12 T1 — a SUMO car's type: the one its sender mapped, else the generic traffic car. The replica and the map's far
    /// car (§15.8) both read it.</summary>
    public static long CarTypeOf(in TrafficCarPose car) => car.TkbType != 0 ? car.TkbType : TkbEntityTypes.TrafficCar;

    private readonly EntityRepository _world;
    private readonly NetworkEntityMap? _entityMap;
    private readonly int _localNodeId;
    private readonly Dictionary<(int Owner, VehicleHandle Handle), Entity> _replicas = new();
    private readonly List<Vector2> _anchors = new();
    private readonly HashSet<(int, VehicleHandle)> _seen = new();
    private readonly List<(int, VehicleHandle)> _drop = new();
    private readonly Dictionary<(int Owner, long NetworkId), Entity> _peds = new();
    private readonly HashSet<(int, long)> _pedSeen = new();
    private readonly List<(int, long)> _pedDrop = new();
    private readonly HashSet<long> _released = new();
    private readonly HashSet<long> _seenIds = new();

    /// <param name="entityMap">The node's network entity map (null in a world with no network layer, e.g. a rail).</param>
    public TrafficReplicas(EntityRepository world, NetworkEntityMap? entityMap, int localNodeId)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _entityMap = entityMap;
        _localNodeId = localNodeId;
    }

    /// <summary>The live replicas, by (sender, SUMO car).</summary>
    public IReadOnlyDictionary<(int Owner, VehicleHandle Handle), Entity> Replicas => _replicas;

    /// <summary>⭐ AQ93 §15 K3 — the live pedestrian replicas, by (sender, network id).</summary>
    public IReadOnlyDictionary<(int Owner, long NetworkId), Entity> Pedestrians => _peds;

    /// <summary>Is this pedestrian a replica now (the entity layer draws it)?</summary>
    public bool IsPedReplica(TrafficPedPose ped) => _peds.ContainsKey((ped.Owner, ped.NetworkId));

    /// <summary>⭐ AQ93 §15 K5/K7 — the pedestrian or car <paramref name="networkId"/> is becoming OUR entity: its replica goes now (so the
    /// id is free when ours is registered) and is not re-made while SUMO's wire still shows it.</summary>
    public void Release(long networkId)
    {
        _released.Add(networkId);
        foreach (var kv in _peds.ToArray())
            if (kv.Key.NetworkId == networkId) DestroyPed(kv.Key, kv.Value);
        // the car replica: resolved through the node's map (it is registered there at Create), never by reading ids inline
        if (_entityMap != null && _entityMap.TryGetEntity(networkId, out var car))
            foreach (var kv in _replicas.ToArray())
                if (kv.Value == car) Destroy(kv.Key, kv.Value);
    }

    /// <summary>Is this car a replica now (so a car layer skips drawing it: the entity layer draws it)?</summary>
    public bool IsReplica(TrafficCarPose car) => _replicas.ContainsKey((car.Owner, car.Handle));

    /// <summary>⭐ AQ93 §14 G6 — a SUMO shadow of one of our handed-over vehicles (<c>hrot:&lt;networkId&gt;</c>): never a replica, never drawn
    /// as a car — the body it drives is the entity.</summary>
    public static bool IsShadow(TrafficCarPose car) => car.Id is { } id && id.StartsWith(TrafficShadowFollowSystem.ShadowPrefix, StringComparison.Ordinal);

    /// <summary>Is this car drawn by the entity layer (a replica, or a shadow whose body is the entity)?</summary>
    public bool IsEntity(TrafficCarPose car) => IsShadow(car) || IsReplica(car);

    /// <summary>Applies one frame: creates, poses and destroys replicas against <paramref name="cars"/> and
    /// <paramref name="pedestrians"/>.</summary>
    public void Update(IReadOnlyList<TrafficCarPose> cars, IReadOnlyList<TrafficPedPose>? pedestrians = null)
    {
        CollectAnchors();
        _seen.Clear();
        _seenIds.Clear();
        float enter2 = Radius * Radius, leave2 = (Radius + Hysteresis) * (Radius + Hysteresis);

        foreach (var car in cars)
        {
            var key = (car.Owner, car.Handle);
            _seen.Add(key);
            _seenIds.Add(car.NetworkId);
            if (_released.Contains(car.NetworkId)) continue;   // ⭐ K7 — becoming our car: not re-made
            float d2 = NearestAnchor2(new Vector2(car.X, car.Y));
            bool have = _replicas.TryGetValue(key, out var e) && _world.IsAlive(e);
            if (have)
            {
                if (d2 > leave2) { Destroy(key, e); continue; }
                Pose(e, car);
            }
            else if (d2 <= enter2 && car.NetworkId != 0 && !IsShadow(car))   // ⭐ §14 G6 — a shadow car's body IS the entity
            {
                _replicas[key] = Create(car);
            }
        }

        _drop.Clear();
        foreach (var kv in _replicas)
            if (!_seen.Contains(kv.Key) || !_world.IsAlive(kv.Value)) _drop.Add(kv.Key);
        foreach (var k in _drop) Destroy(k, _replicas[k]);
        UpdatePedestrians(pedestrians ?? Array.Empty<TrafficPedPose>(), enter2, leave2);
    }

    // ⭐ §15 K3 — the SAME vicinity rule for pedestrians (R-4041): an entity within Radius of one of our bodies, kept to Radius + Hysteresis.
    private void UpdatePedestrians(IReadOnlyList<TrafficPedPose> peds, float enter2, float leave2)
    {
        _pedSeen.Clear();
        foreach (var ped in peds)
        {
            var key = (ped.Owner, ped.NetworkId);
            _pedSeen.Add(key);
            _seenIds.Add(ped.NetworkId);
            if (ped.NetworkId == 0 || _released.Contains(ped.NetworkId)) continue;
            float d2 = NearestAnchor2(new Vector2(ped.X, ped.Y));
            bool have = _peds.TryGetValue(key, out var e) && _world.IsAlive(e);
            if (have)
            {
                if (d2 > leave2) { DestroyPed(key, e); continue; }
                _world.SetComponent(e, Transform(ped));
            }
            else if (d2 <= enter2 && (_entityMap is null || !_entityMap.TryGetEntity(ped.NetworkId, out _)))   // ⛔ never over a live id
            {
                _peds[key] = CreatePed(ped);
            }
        }
        _pedDrop.Clear();
        foreach (var kv in _peds)
            if (!_pedSeen.Contains(kv.Key) || !_world.IsAlive(kv.Value)) _pedDrop.Add(kv.Key);
        foreach (var k in _pedDrop) DestroyPed(k, _peds[k]);
        _released.RemoveWhere(id => !_seenIds.Contains(id));   // SUMO no longer shows it: the release is done
    }

    private Entity CreatePed(in TrafficPedPose ped)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(ped.NetworkId));
        _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: ped.Owner, localNodeId: _localNodeId));
        _world.AddComponent(e, new TkbIdentity { TkbType = PedestrianTypeOf(ped.NetworkId) });
        _world.AddComponent(e, new ReplicationRoute(ReplicationRoute.SumoSharp));
        _world.AddComponent(e, new TrafficCar { VehicleIndex = 0, Kind = TrafficCar.KindPedestrian });
        _world.AddComponent(e, new ScenarioIgnoreTag());
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64("SUMO ped " + ped.Name), ForceId = ForceId.Neutral });
        _world.AddComponent(e, new PhysicsCollider
        {
            Radius = PedRadius,
            Height = PedHeight,
            CollisionLayer = Fdp.Toolkit.Physics.PhysicsConstants.EntityCollisionLayer,
        });
        _world.AddComponent(e, Transform(ped));
        AddHealth(e);
        _entityMap?.Register(ped.NetworkId, e);
        return e;
    }

    private void AddHealth(Entity e)
    {
        if (_world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>())
            _world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = ReplicaHealth, Max = ReplicaHealth });
    }

    private static SimTransform Transform(in TrafficPedPose ped)
        => new() { Position = new Vector3(ped.X, ped.Y, ped.Z), Rotation = SimMath.FromYaw(ped.YawRad) };

    private void DestroyPed((int, long) key, Entity e)
    {
        if (_world.IsAlive(e))
        {
            if (_entityMap is not null && _world.HasComponent<NetworkIdentity>(e)
                && _entityMap.TryGetEntity(key.Item2, out var mapped) && mapped == e)
            {
                uint frame = _world.HasSingleton<GlobalTime>() ? (uint)_world.GetSingleton<GlobalTime>().FrameNumber : 0u;
                _entityMap.Unregister(key.Item2, frame);
            }
            _world.DestroyEntity(e);
        }
        _peds.Remove(key);
    }

    /// <summary>Destroys every replica (the wire went away, the net or the demand changed).</summary>
    public void Clear()
    {
        foreach (var kv in _replicas.ToArray()) Destroy(kv.Key, kv.Value);
        _replicas.Clear();
        foreach (var kv in _peds.ToArray()) DestroyPed(kv.Key, kv.Value);
        _peds.Clear();
        _released.Clear();
    }

    private void CollectAnchors()
    {
        _anchors.Clear();
        var q = _world.Query().With<SimTransform>().With<PhysicsCollider>().Without<ReplicationRoute>().Build();
        foreach (var a in q)
        {
            var p = _world.GetComponentRO<SimTransform>(a).Position;
            _anchors.Add(new Vector2(p.X, p.Y));
        }
    }

    private float NearestAnchor2(Vector2 p)
    {
        float best = float.PositiveInfinity;
        foreach (var a in _anchors)
            best = MathF.Min(best, Vector2.DistanceSquared(a, p));
        return best;
    }

    private Entity Create(in TrafficCarPose car)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(car.NetworkId));
        _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: car.Owner, localNodeId: _localNodeId));
        _world.AddComponent(e, new TkbIdentity { TkbType = CarTypeOf(car) });
        _world.AddComponent(e, new ReplicationRoute(ReplicationRoute.SumoSharp));
        _world.AddComponent(e, new TrafficCar { VehicleIndex = car.Handle.Index, Generation = car.Handle.Generation });
        _world.AddComponent(e, new ScenarioIgnoreTag());
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64("SUMO " + car.Id), ForceId = ForceId.Neutral });
        if (car.Color != 0 && _world.IsComponentTypeRegistered<EntityAppearance>())
            _world.AddComponent(e, EntityAppearance.Of((byte)(car.Color >> 16), (byte)(car.Color >> 8), (byte)car.Color));
        _world.AddComponent(e, new PhysicsCollider
        {
            Radius = 0.5f * MathF.Max(car.Length, car.Width),
            Height = CarHeight,
            CollisionLayer = Fdp.Toolkit.Physics.PhysicsConstants.EntityCollisionLayer,
        });
        _world.AddComponent(e, Transform(car));
        AddHealth(e);
        _entityMap?.Register(car.NetworkId, e);
        return e;
    }

    private void Pose(Entity e, in TrafficCarPose car) => _world.SetComponent(e, Transform(car));

    private static SimTransform Transform(in TrafficCarPose car)
        => new() { Position = new Vector3(car.X, car.Y, car.Z), Rotation = SimMath.FromYaw(car.YawRad) };

    private void Destroy((int, VehicleHandle) key, Entity e)
    {
        if (_world.IsAlive(e))
        {
            if (_entityMap is not null && _world.HasComponent<NetworkIdentity>(e))
            {
                uint frame = _world.HasSingleton<GlobalTime>() ? (uint)_world.GetSingleton<GlobalTime>().FrameNumber : 0u;
                _entityMap.Unregister(_world.GetComponentRO<NetworkIdentity>(e).Value, frame);
            }
            _world.DestroyEntity(e);
        }
        _replicas.Remove(key);
    }
}
