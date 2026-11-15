using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using VehicleParams = CarKinem.Core.VehicleParams;

namespace Hrot.Traffic;

/// <summary>How SUMO is told about one of our bodies (AQ93 §13 F3).</summary>
public enum TrafficBodyKind : byte
{
    /// <summary>A lane obstacle on every vehicle lane its footprint overlaps (a disc when no lane is near).</summary>
    Vehicle = 0,
    /// <summary>A crowd disc.</summary>
    Person = 1,
}

/// <summary>⭐ AQ93 §13 — one of OUR bodies as SUMO sees it: NET coordinates (metres), velocity (m/s), footprint (metres).</summary>
public readonly record struct TrafficBody(int NetworkId, TrafficBodyKind Kind, double X, double Y, double Vx, double Vy, float Length, float Width);

/// <summary>
/// ⭐ AQ93 §13 (E4, R-4027) — each frame on a SERVING node's main thread: gathers our bodies and hands them to the stepping thread
/// (<see cref="TrafficRuntime.SetBodies"/>), which tells SUMO before its next step.
/// </summary>
/// <remarks>
/// <para>⭐ A body is what the vicinity replicas anchor on (<see cref="TrafficReplicas"/>, R-4041): <c>SimTransform</c> +
/// <c>PhysicsCollider</c>, not a SUMO car (<c>ReplicationRoute</c>) — on the dedicated traffic node these are NED replicas of our
/// units (R-4057), in the editor they are local. Its class is <see cref="Locomotions.Of"/>: a ground vehicle class ⇒ a vehicle, a
/// pedestrian ⇒ a person, the air, the sea and an unknown class ⇒ not told (§13 F2).</para>
/// <para>⭐ The footprint is <see cref="EntityFootprint.Dimensions"/> — the one size rule the map draws by.</para>
/// </remarks>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class TrafficBodyFeedSystem : IEcsModuleSystem
{
    private int _toldVehicles = -1, _toldPersons = -1;

    /// <summary>A person's disc radius when its footprint names no width.</summary>
    public const float PersonRadius = 0.4f;

    private readonly TrafficRuntime _runtime;
    private readonly List<TrafficBody> _scratch = new();

    public TrafficBodyFeedSystem(TrafficRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;
        var net = TrafficNetCache.Of(world);
        var bodies = net is null ? Array.Empty<TrafficBody>() : Gather(world, net.Placement, _scratch);
        _runtime.SetBodies(bodies);
        // ⭐ CE-4025 — what SUMO is told, logged when its make-up changes (never per frame): the one way to see it on a remote node
        int vehicles = 0, persons = 0;
        foreach (var b in bodies) { if (b.Kind == TrafficBodyKind.Vehicle) vehicles++; else persons++; }
        if (vehicles != _toldVehicles || persons != _toldPersons)
        {
            _toldVehicles = vehicles; _toldPersons = persons;
            var sb = new System.Text.StringBuilder();
            foreach (var b in bodies)
                if (b.Kind == TrafficBodyKind.Vehicle)
                    sb.Append(System.FormattableString.Invariant($" [{b.NetworkId} net ({b.X:0.0},{b.Y:0.0}) {b.Length:0.0}x{b.Width:0.0} v ({b.Vx:0.00},{b.Vy:0.00})]"));
            Fdp.Core.Logging.FdpLog<TrafficBodyFeedSystem>.Info("[Traffic] SUMO is told {0} of our vehicles and {1} of our people{2}", vehicles, persons, sb.ToString());
        }
    }

    /// <summary>The bodies of <paramref name="world"/> in net coordinates, a new array (the stepping thread keeps it).</summary>
    public static TrafficBody[] Gather(EntityRepository world, SumoNetPlacement placement, List<TrafficBody>? scratch = null)
    {
        var into = scratch ?? new List<TrafficBody>();
        into.Clear();
        if (!world.IsComponentTypeRegistered<SimTransform>()) return Array.Empty<TrafficBody>();
        if (!world.IsComponentTypeRegistered<PhysicsCollider>()) { AddObstacles(world, placement, into); return into.ToArray(); }
        bool routed = world.IsComponentTypeRegistered<ReplicationRoute>();
        bool velocity = world.IsComponentTypeRegistered<SimVelocity>();
        bool identity = world.IsComponentTypeRegistered<NetworkIdentity>();
        bool sized = world.IsComponentTypeRegistered<VehicleParams>();   // ⚠ a traffic node may not register it: the default footprint
        foreach (var e in world.Query().With<SimTransform>().With<PhysicsCollider>().Build())
        {
            if (routed && world.HasComponent<ReplicationRoute>(e)) continue;   // a SUMO car's replica: SUMO already has it
            if (KindOf(Locomotions.Of(world, e)) is not { } kind) continue;
            if (HandedToSumo(world, e)) continue;   // ⭐ §14 G6 — SUMO drives it already: its shadow would brake behind itself
            var p = world.GetComponentRO<SimTransform>(e).Position;
            var v = velocity && world.HasComponent<SimVelocity>(e) ? world.GetComponentRO<SimVelocity>(e).Linear : Vector3.Zero;
            float length, width;
            if (sized) EntityFootprint.Dimensions(world, e, out length, out width);
            else { length = EntityFootprint.DefaultLengthMetres; width = length * EntityFootprint.DefaultWidthFactor; }
            if (kind == TrafficBodyKind.Person && !(sized && world.HasComponent<VehicleParams>(e) && world.GetComponentRO<VehicleParams>(e).Width > 0f))
                length = width = 2f * PersonRadius;
            var (nx, ny) = placement.ToNet(p.X, p.Y);
            // A velocity is a direction, not a point: rotate it by the placement's local frame (sample one metre along it).
            var (ax, ay) = placement.ToNet(p.X + v.X, p.Y + v.Y);
            int id = identity && world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : e.Index;
            into.Add(new TrafficBody(id, kind, nx, ny, ax - nx, ay - ny, length, width));
        }
        AddObstacles(world, placement, into);
        return into.ToArray();
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4590</c> (P-2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-10, §9.2 V-24) — every ACTIVE static obstacle (a roadblock, a wreck, sandbags)
    /// is told to SUMO as bodies AT REST, so a car stops or swerves at a road it PARTLY covers (a lane obstacle — the H12 feed maps a body to the lanes it
    /// overlaps); a road it covers fully is CLOSED by the road-blockage step (<see cref="BridgeClosureSystem"/>). A body at rest takes its heading from its lane,
    /// so the obstacle's box is told as a CHAIN of squares along its long axis (side = its short side) — the same box whatever its heading. ⭐ V-24 — the ids
    /// are synthetic and NEGATIVE, never a unit's network id.
    /// </summary>
    public static void AddObstacles(EntityRepository world, SumoNetPlacement placement, List<TrafficBody> into)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.StaticObstacle>() || !world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.ObstacleShape>()) return;
        foreach (var e in world.Query().With<Fdp.Toolkit.Terrain.StaticObstacle>().With<Fdp.Toolkit.Terrain.ObstacleShape>().With<SimTransform>().Build())
        {
            var t = world.GetComponentRO<SimTransform>(e);
            var shape = world.GetComponentRO<Fdp.Toolkit.Terrain.ObstacleShape>(e);
            float yaw = Fdp.Toolkit.Terrain.TerrainObstacles.Yaw(t.Rotation);
            float len = MathF.Max(shape.Length, 0.1f), wid = MathF.Max(shape.Width, 0.1f);
            float side = MathF.Min(len, wid), longSide = MathF.Max(len, wid);
            var axis = len >= wid ? new Vector2(MathF.Cos(yaw), MathF.Sin(yaw)) : new Vector2(-MathF.Sin(yaw), MathF.Cos(yaw));
            int parts = Math.Max(1, (int)MathF.Ceiling(longSide / side));
            float step = longSide / parts;
            for (int k = 0; k < parts; k++)
            {
                var c = new Vector2(t.Position.X, t.Position.Y) + axis * (-longSide * 0.5f + step * (k + 0.5f));
                var (nx, ny) = placement.ToNet(c.X, c.Y);
                into.Add(new TrafficBody(-(e.Index * 64 + k + 1), TrafficBodyKind.Vehicle, nx, ny, 0, 0, step, side));
            }
        }
    }

    /// <summary>⭐ AQ93 §14 G6 — a body handed to SUMO: its intent is <c>Traffic</c> (what the serving node sees) or its kinematics are
    /// (on the node that moves it).</summary>
    public static bool HandedToSumo(EntityRepository world, Entity e)
        => (world.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationIntent>() && world.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(e)
            && world.GetComponentRO<Fdp.Toolkit.Navigation.NavigationIntent>(e).Mode == Fdp.Toolkit.Navigation.NavigationMode.Traffic)
           || (world.IsComponentTypeRegistered<CarKinem.Core.NavState>() && world.HasComponent<CarKinem.Core.NavState>(e)
               && world.GetComponentRO<CarKinem.Core.NavState>(e).Mode == CarKinem.Core.KinematicsMode.Traffic);

    /// <summary>§13 F2/F3 — the body class → what SUMO is told; null ⇒ nothing.</summary>
    public static TrafficBodyKind? KindOf(LocomotionClass c) => c switch
    {
        LocomotionClass.Pedestrian => TrafficBodyKind.Person,
        LocomotionClass.Wheeled or LocomotionClass.Tracked or LocomotionClass.Amphibious => TrafficBodyKind.Vehicle,
        _ => null,
    };
}
