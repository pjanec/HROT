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

    private readonly EntityRepository _world;
    private readonly NetworkEntityMap? _entityMap;
    private readonly int _localNodeId;
    private readonly Dictionary<(int Owner, VehicleHandle Handle), Entity> _replicas = new();
    private readonly List<Vector2> _anchors = new();
    private readonly HashSet<(int, VehicleHandle)> _seen = new();
    private readonly List<(int, VehicleHandle)> _drop = new();

    /// <param name="entityMap">The node's network entity map (null in a world with no network layer, e.g. a rail).</param>
    public TrafficReplicas(EntityRepository world, NetworkEntityMap? entityMap, int localNodeId)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _entityMap = entityMap;
        _localNodeId = localNodeId;
    }

    /// <summary>The live replicas, by (sender, SUMO car).</summary>
    public IReadOnlyDictionary<(int Owner, VehicleHandle Handle), Entity> Replicas => _replicas;

    /// <summary>Is this car a replica now (so a car layer skips drawing it: the entity layer draws it)?</summary>
    public bool IsReplica(TrafficCarPose car) => _replicas.ContainsKey((car.Owner, car.Handle));

    /// <summary>Applies one frame: creates, poses and destroys replicas against <paramref name="cars"/>.</summary>
    public void Update(IReadOnlyList<TrafficCarPose> cars)
    {
        CollectAnchors();
        _seen.Clear();
        float enter2 = Radius * Radius, leave2 = (Radius + Hysteresis) * (Radius + Hysteresis);

        foreach (var car in cars)
        {
            var key = (car.Owner, car.Handle);
            _seen.Add(key);
            float d2 = NearestAnchor2(new Vector2(car.X, car.Y));
            bool have = _replicas.TryGetValue(key, out var e) && _world.IsAlive(e);
            if (have)
            {
                if (d2 > leave2) { Destroy(key, e); continue; }
                Pose(e, car);
            }
            else if (d2 <= enter2 && car.NetworkId != 0)
            {
                _replicas[key] = Create(car);
            }
        }

        _drop.Clear();
        foreach (var kv in _replicas)
            if (!_seen.Contains(kv.Key) || !_world.IsAlive(kv.Value)) _drop.Add(kv.Key);
        foreach (var k in _drop) Destroy(k, _replicas[k]);
    }

    /// <summary>Destroys every replica (the wire went away, the net or the demand changed).</summary>
    public void Clear()
    {
        foreach (var kv in _replicas.ToArray()) Destroy(kv.Key, kv.Value);
        _replicas.Clear();
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
        _world.AddComponent(e, new TkbIdentity { TkbType = car.TkbType != 0 ? car.TkbType : TkbEntityTypes.TrafficCar });
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
