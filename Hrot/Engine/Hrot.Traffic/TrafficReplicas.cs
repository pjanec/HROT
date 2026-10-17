using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using VehicleHandle = Sim.Core.VehicleHandle;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4038 / R-4041 / R-4043 (the vicinity half) — turns the SUMO cars near HROT into real ENTITIES: a car within
/// <see cref="Radius"/> of any HROT body becomes a replica (so raycasts, EQS, sensors and blasts see it), stays one until it is past
/// <see cref="Radius"/> + <see cref="Hysteresis"/>, and is destroyed then or when SUMO retires the car. Cars further out are drawn
/// but are not entities.
/// </summary>
/// <remarks>
/// <para>⭐ A replica is an ordinary networked entity — a network id from the cluster's allocator (R-4029), owned by the node that
/// hosts SUMO (R-4038), Neutral — on the <see cref="ReplicationRoute.SumoSharp"/> route, so NED never publishes it (EM-4): every
/// other node gets the car from SUMO's own wire and dead-reckons it itself (R-4020). It carries
/// <see cref="ScenarioIgnoreTag"/>: the car is SUMO's, never the scenario's.</para>
/// <para>⭐ An ANCHOR is an HROT body — <c>SimTransform</c> + <c>PhysicsCollider</c> on the default route — so a map graphic (an area,
/// a route) never pulls cars in, and a replica never anchors another.</para>
/// <para>⭐ Its collider is the car's own footprint (the circle the raycast broad phase and the radius search use), 1.5 m tall.</para>
/// </remarks>
public sealed class TrafficReplicas
{
    /// <summary>R-4041 — the vicinity radius.</summary>
    public const float Radius = 300f;

    /// <summary>R-4041 — a replica is kept until this far past <see cref="Radius"/>.</summary>
    public const float Hysteresis = 50f;

    private const float CarHeight = 1.5f;

    private readonly EntityRepository _world;
    private readonly INetworkIdAllocator? _ids;
    private readonly int _localNodeId;
    private readonly Dictionary<VehicleHandle, Entity> _replicas = new();
    private readonly List<Vector2> _anchors = new();
    private readonly HashSet<VehicleHandle> _seen = new();
    private readonly List<VehicleHandle> _drop = new();
    private long _localIds = -1;

    /// <param name="ids">The cluster's id allocator; null (a test world) ⇒ local negative ids.</param>
    public TrafficReplicas(EntityRepository world, INetworkIdAllocator? ids, int localNodeId)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ids = ids;
        _localNodeId = localNodeId;
    }

    /// <summary>The live replicas, by SUMO car.</summary>
    public IReadOnlyDictionary<VehicleHandle, Entity> Replicas => _replicas;

    /// <summary>Is this car a replica now (so a car layer skips drawing it: the entity layer draws it)?</summary>
    public bool IsReplica(VehicleHandle handle) => _replicas.ContainsKey(handle);

    /// <summary>Applies one frame: creates, poses and destroys replicas against <paramref name="cars"/>.</summary>
    public void Update(IReadOnlyList<TrafficCarPose> cars)
    {
        CollectAnchors();
        _seen.Clear();
        float enter2 = Radius * Radius, leave2 = (Radius + Hysteresis) * (Radius + Hysteresis);

        foreach (var car in cars)
        {
            _seen.Add(car.Handle);
            float d2 = NearestAnchor2(new Vector2(car.X, car.Y));
            bool have = _replicas.TryGetValue(car.Handle, out var e) && _world.IsAlive(e);
            if (have)
            {
                if (d2 > leave2) { Destroy(car.Handle, e); continue; }
                Pose(e, car);
            }
            else if (d2 <= enter2)
            {
                _replicas[car.Handle] = Create(car);
            }
        }

        _drop.Clear();
        foreach (var kv in _replicas)
            if (!_seen.Contains(kv.Key) || !_world.IsAlive(kv.Value)) _drop.Add(kv.Key);
        foreach (var h in _drop) Destroy(h, _replicas[h]);
    }

    /// <summary>Destroys every replica (the session ended, the net or the demand went away).</summary>
    public void Clear()
    {
        foreach (var kv in _replicas)
            if (_world.IsAlive(kv.Value)) _world.DestroyEntity(kv.Value);
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
        long netId = _ids?.AllocateId() ?? _localIds--;
        _world.AddComponent(e, new NetworkIdentity(netId));
        _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: _localNodeId, localNodeId: _localNodeId));
        _world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.TrafficCar });
        _world.AddComponent(e, new ReplicationRoute(ReplicationRoute.SumoSharp));
        _world.AddComponent(e, new TrafficCar { VehicleIndex = car.Handle.Index, Generation = car.Handle.Generation });
        _world.AddComponent(e, new ScenarioIgnoreTag());
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64("SUMO " + car.Id), ForceId = ForceId.Neutral });
        _world.AddComponent(e, new PhysicsCollider
        {
            Radius = 0.5f * MathF.Max(car.Length, car.Width),
            Height = CarHeight,
            CollisionLayer = Fdp.Toolkit.Physics.PhysicsConstants.EntityCollisionLayer,
        });
        _world.AddComponent(e, Transform(car));
        return e;
    }

    private void Pose(Entity e, in TrafficCarPose car) => _world.SetComponent(e, Transform(car));

    private static SimTransform Transform(in TrafficCarPose car)
        => new() { Position = new Vector3(car.X, car.Y, car.Z), Rotation = SimMath.FromYaw(car.YawRad) };

    private void Destroy(VehicleHandle handle, Entity e)
    {
        if (_world.IsAlive(e)) _world.DestroyEntity(e);
        _replicas.Remove(handle);
    }
}
