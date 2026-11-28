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
    [Fdp.Core.WorldDerived("log throttle for SUMO told-vehicle count")]
    private int _toldVehicles = -1, _toldPersons = -1;

    /// <summary>A person's disc radius when its footprint names no width.</summary>
    public const float PersonRadius = 0.4f;

    private readonly TrafficRuntime _runtime;
    [Fdp.Core.WorldDerived("scratch cleared in Gather each frame")]
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
                sb.Append(System.FormattableString.Invariant($" [{(b.Kind == TrafficBodyKind.Vehicle ? "" : "person ")}{b.NetworkId} net ({b.X:0.0},{b.Y:0.0}) {b.Length:0.0}x{b.Width:0.0} v ({b.Vx:0.00},{b.Vy:0.00})]"));   // ⭐ CE-11072 — persons too (a disc on the road holds a shadow)
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
        bool embarked = world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        foreach (var e in world.Query().With<SimTransform>().With<PhysicsCollider>().Build())
        {
            if (routed && world.HasComponent<ReplicationRoute>(e)) continue;   // a SUMO car's replica: SUMO already has it
            if (KindOf(Locomotions.Of(world, e)) is not { } kind) continue;
            if (HandedToSumo(world, e)) continue;   // ⭐ §14 G6 — SUMO drives it already: its shadow would brake behind itself
            // ⭐⭐ CE-11072 — a RIDER (aboard any vehicle, enclosed or exposed) is inside its vehicle's footprint: the vehicle is the body. 📌 Measured on
            //   va-dozer-breach-wall: an APC's gunner (seat 8) was told to SUMO as a pedestrian at the APC's own place, so the APC's shadow braked for its
            //   own crew and stood still 17 m after the hand-over, for good.
            if (embarked && world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) continue;
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
        AddConvoyGaps(world, placement, into);   // ⭐ CE-9002 (C-5) — the column's gaps: no SUMO car slips into the convoy
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

    /// <summary>
    /// ⭐⭐ <c>CE-9002</c> (C-5; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md §3) — a road convoy is ONE continuous obstacle to SUMO: each gap between a
    /// vehicle and the one ahead of it (the predecessor's rear to this vehicle's front) is told as a chain of squares (side = the follower's width) — along
    /// the lead's TRAIL where this node holds it (the editor, the kinematic owner), else along the straight chord between the two. A SUMO car then never
    /// sees room to cut in: an obstacle on its lane blocks it, a lane change into one is vetoed, a junction foe lane holding one stops it at the line (§1 ⑦).
    /// The squares move with the follower. ⭐ Synthetic NEGATIVE ids <c>-(follower.Index · 64 + k + 1)</c> — the static-obstacle scheme (V-24), never a
    /// unit's network id; a follower is never a static obstacle, so the two never collide.
    /// </summary>
    public static void AddConvoyGaps(EntityRepository world, SumoNetPlacement placement, List<TrafficBody> into)
    {
        if (!world.IsComponentTypeRegistered<CarKinem.Formation.FormationFollower>() || !world.IsComponentTypeRegistered<CarKinem.Formation.FormationController>()) return;
        bool trails = world.IsComponentTypeRegistered<CarKinem.Convoy.ConvoyTrail>() && world.IsComponentTypeRegistered<CarKinem.Convoy.ConvoyProgress>();
        bool velocity = world.IsComponentTypeRegistered<SimVelocity>();
        bool sized = world.IsComponentTypeRegistered<VehicleParams>();
        // ⭐ CE-9004 — a convoy is a Column formation: each follower (its slot = its rank) and the one ahead of it (slot − 1; slot 0 = the leader)
        var members = world.Query().With<CarKinem.Formation.FormationFollower>().With<SimTransform>().Build();
        foreach (var e in members)
        {
            if (!CarKinem.Systems.ConvoyFollowSystem.InColumn(world, e, out var lead) || HandedToSumo(world, e)) continue;
            int slot = world.GetComponentRO<CarKinem.Formation.FormationFollower>(e).SlotIndex;
            Entity pred = slot == 1 ? lead : default; bool found = slot == 1;
            if (!found)
                foreach (var o in members)
                {
                    if (o == e || !CarKinem.Systems.ConvoyFollowSystem.InColumn(world, o, out var ol) || ol != lead) continue;
                    if (world.GetComponentRO<CarKinem.Formation.FormationFollower>(o).SlotIndex == slot - 1) { pred = o; found = true; break; }
                }
            if (!found || !world.HasComponent<SimTransform>(pred)) continue;
            float lf = EntityFootprint.DefaultLengthMetres, wf = lf * EntityFootprint.DefaultWidthFactor, lp = lf, wp = wf;
            if (sized) { EntityFootprint.Dimensions(world, e, out lf, out wf); EntityFootprint.Dimensions(world, pred, out lp, out wp); }
            // ⭐⭐ CE-1972 (R-5063) — the squares are told AT REST (velocity 0): they are re-fed every frame, so they move with the column anyway. ⛔ They carried the
            //   follower's velocity, and SumoSharp projects a moving obstacle forward: whenever the lead's SUMO shadow was slower than its follower (the C-6 speed
            //   cap, a start, a light) the square at its rear was projected INTO it and the shadow swerved sideways (B6 evasion, 2 m/s) — the lead weaving from lane
            //   edge to lane edge, its followers copying it on the trail (📐 measured: SumoSharp alone, a capped priority shadow, 146 of 180 samples swerving)
            const float vx1 = 0f, vy1 = 0f, vx0 = 0f, vy0 = 0f;
            float side = MathF.Max(wf, 1f);

            if (trails && world.HasComponent<CarKinem.Convoy.ConvoyTrail>(lead) && world.HasComponent<CarKinem.Convoy.ConvoyProgress>(e)
                && (pred == lead || world.HasComponent<CarKinem.Convoy.ConvoyProgress>(pred)))
            {
                ref readonly var trail = ref world.GetComponentRO<CarKinem.Convoy.ConvoyTrail>(lead);
                double sp = pred == lead ? trail.RankArc(0) : world.GetComponentRO<CarKinem.Convoy.ConvoyProgress>(pred).ArcS;
                double from = world.GetComponentRO<CarKinem.Convoy.ConvoyProgress>(e).ArcS + lf * 0.5, to = sp - lp * 0.5;
                if (to - from < 0.5) continue;
                int parts = Math.Min(63, Math.Max(1, (int)Math.Ceiling((to - from) / side)));
                double step = (to - from) / parts;
                for (int k = 0; k < parts; k++)
                {
                    var (c, _) = trail.Sample(from + step * (k + 0.5));
                    var (nx, ny) = placement.ToNet(c.X, c.Y);
                    into.Add(new TrafficBody(-(e.Index * 64 + k + 1), TrafficBodyKind.Vehicle, nx, ny, vx1 - vx0, vy1 - vy0, (float)step, side));
                }
                continue;
            }

            // no trail here (a dedicated traffic node): the straight chord from the predecessor's rear to this vehicle's front
            var pf = world.GetComponentRO<SimTransform>(e); var pp = world.GetComponentRO<SimTransform>(pred);
            var ff = Vector3.Transform(Vector3.UnitX, pf.Rotation); var fp = Vector3.Transform(Vector3.UnitX, pp.Rotation);
            var a = new Vector2(pf.Position.X, pf.Position.Y) + Vector2.Normalize(new Vector2(ff.X, ff.Y) + new Vector2(1e-6f, 0f)) * (lf * 0.5f);
            var b = new Vector2(pp.Position.X, pp.Position.Y) - Vector2.Normalize(new Vector2(fp.X, fp.Y) + new Vector2(1e-6f, 0f)) * (lp * 0.5f);
            float len = Vector2.Distance(a, b);
            if (len < 0.5f) continue;
            int n = Math.Min(63, Math.Max(1, (int)MathF.Ceiling(len / side)));
            for (int k = 0; k < n; k++)
            {
                var c = Vector2.Lerp(a, b, (k + 0.5f) / n);
                var (nx, ny) = placement.ToNet(c.X, c.Y);
                into.Add(new TrafficBody(-(e.Index * 64 + k + 1), TrafficBodyKind.Vehicle, nx, ny, vx1 - vx0, vy1 - vy0, len / n, side));
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
        // ⭐ CE-4623 AB-2 (AR-17) — an animal on the road: a body SUMO's cars brake for, at its own footprint (a cow 2.4 × 0.9 m, a hen 0.4 m);
        //   a bird is in the air (null). A herd crossing a road holds the traffic.
        LocomotionClass.Quadruped => TrafficBodyKind.Vehicle,
        _ => null,
    };
}
