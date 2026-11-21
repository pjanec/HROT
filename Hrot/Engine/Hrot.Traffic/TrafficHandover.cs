using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;


namespace Hrot.Traffic;

/// <summary>⭐ AQ93 §14 G3 — one of our vehicles handed to SUMO, as the stepping thread spawns its shadow: NET coordinates; (X, Y) is the
/// FRONT BUMPER (SumoSharp H13's position convention); heading in radians, 0 = east, counter-clockwise (atan2).</summary>
public readonly record struct TrafficHandover(int NetworkId, uint IntentId, double X, double Y, double Heading, double Speed,
                                              double DestX, double DestY, string? VType, bool Priority = false, double? MaxSpeed = null);

/// <summary>
/// ⭐ AQ93 §14 G2/G3 (R-4023) — each frame on a SERVING node's main thread: every body whose <see cref="NavigationIntent"/> says
/// <see cref="NavigationMode.Traffic"/> becomes a hand-over request for the stepping thread (<see cref="TrafficRuntime.SetHandovers"/>),
/// which spawns its shadow <c>hrot:&lt;networkId&gt;</c> in SUMO and despawns it when the intent changes.
/// </summary>
/// <remarks>⭐ The intent arrives here by NED ingress on the traffic node (a read-only translator, §14 G2) and is local in the
/// embedded editor. ⛔ Ownership never moves: the body's owner FOLLOWS the shadow (<see cref="TrafficShadowFollowSystem"/>).</remarks>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class TrafficHandoverSystem : IEcsModuleSystem
{
    /// <summary>⭐ CE-9000 (C-6) — the speed a column's cap factor scales when the lead asked no speed and names no top speed (m/s, an urban limit).</summary>
    public const double CapReferenceSpeed = 13.9;

    private readonly TrafficRuntime _runtime;
    [Fdp.Core.WorldDerived("scratch cleared in Gather each frame")]
    private readonly List<TrafficHandover> _scratch = new();

    public TrafficHandoverSystem(TrafficRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;
        var net = TrafficNetCache.Of(world);
        _runtime.SetHandovers(net is null ? Array.Empty<TrafficHandover>() : Gather(world, net.Placement, _scratch, net.Model));
    }

    /// <summary>The hand-over requests of <paramref name="world"/>, a new array (the stepping thread keeps it).</summary>
    /// <param name="model">⭐ CE-9020 — the parsed net: a destination OFF it is sent as the nearest road point (<see cref="TrafficRoadPoint"/>);
    /// <c>null</c> sends the destination as it is (SUMO refuses one with no lane near).</param>
    public static TrafficHandover[] Gather(EntityRepository world, SumoNetPlacement placement, List<TrafficHandover>? scratch = null, Sim.Ingest.NetworkModel? model = null)
    {
        var into = scratch ?? new List<TrafficHandover>();
        into.Clear();
        if (!world.IsComponentTypeRegistered<NavigationIntent>() || !world.IsComponentTypeRegistered<SimTransform>()
            || !world.IsComponentTypeRegistered<NetworkIdentity>()) return Array.Empty<TrafficHandover>();
        bool velocity = world.IsComponentTypeRegistered<SimVelocity>();
        bool sized = world.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>();
        bool typed = world.IsComponentTypeRegistered<TkbIdentity>();
        bool columns = world.IsComponentTypeRegistered<CarKinem.Formation.FormationController>() && world.IsComponentTypeRegistered<CarKinem.Formation.FormationFollower>();
        EntityQuery? followers = null;
        foreach (var e in world.Query().With<NavigationIntent>().With<SimTransform>().With<NetworkIdentity>().Build())
        {
            var intent = world.GetComponentRO<NavigationIntent>(e);
            if (intent.Mode != NavigationMode.Traffic) continue;
            var tf = world.GetComponentRO<SimTransform>(e);
            var fwd3 = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var fwd = new Vector2(fwd3.X, fwd3.Y);
            fwd = fwd.LengthSquared() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(fwd);
            float length = EntityFootprint.DefaultLengthMetres;
            if (sized) EntityFootprint.Dimensions(world, e, out length, out _);
            var front = new Vector2(tf.Position.X, tf.Position.Y) + fwd * (length * 0.5f);
            var (nx, ny) = placement.ToNet(front.X, front.Y);
            var (ax, ay) = placement.ToNet(front.X + fwd.X, front.Y + fwd.Y);   // the heading through the placement (UTM convergence)
            var dest = new Vector2(intent.FinalDestination.X, intent.FinalDestination.Y);
            // ⭐ CE-9020 (mode 1 → 2) — SUMO drives to where it CAN take the destination; the rest is ours (MoveToExecutor)
            if (model is not null && TrafficRoadPoint.Near(model, placement, dest, out var road, out _, new Vector2(tf.Position.X, tf.Position.Y))) dest = road;
            var (dx, dy) = placement.ToNet(dest.X, dest.Y);
            float speed = velocity && world.HasComponent<SimVelocity>(e)
                ? new Vector2(world.GetComponentRO<SimVelocity>(e).Linear.X, world.GetComponentRO<SimVelocity>(e).Linear.Y).Length() : 0f;
            string? vType = typed && world.HasComponent<TkbIdentity>(e)
                ? world.GetComponentRO<TkbIdentity>(e).TkbType.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            bool priority = NavigationConstants.RoadUseOf(intent.Flags) == RoadUse.InTrafficPriority;   // ⭐ CE-9000 (C-1) — the escort's beacon and siren
            // ⭐ CE-9008 — the shadow's TOP SPEED (generic): the speed its brain asked for (the intent's TargetSpeed; 0 = none, SUMO's own) …
            double? cap = intent.TargetSpeed > 0f ? intent.TargetSpeed : null;
            // … and lower while it LEADS a stretched road column (C-6, mode 1): the tail's lag from the members' replicated positions — this node holds no
            //   trail on a cluster (C-8), the same chords the gap squares use (TrafficBodies.AddConvoyGaps)
            if (columns && world.HasComponent<CarKinem.Formation.FormationController>(e)
                && world.GetComponentRO<CarKinem.Formation.FormationController>(e).Type == CarKinem.Formation.FormationType.Column)
            {
                followers ??= world.Query().With<CarKinem.Formation.FormationFollower>().With<SimTransform>().Build();
                float gap = CarKinem.Systems.ConvoyLeadSystem.GapOf(in world.GetComponentRO<CarKinem.Formation.FormationController>(e));
                if (CarKinem.Systems.ConvoyLeadSystem.ChordLag(world, followers, e, gap, out float lag))
                {
                    float factor = CarKinem.Systems.ConvoyLeadSystem.CapFactor(lag, gap);
                    if (factor < 1f)
                    {
                        float top = sized && world.HasComponent<CarKinem.Core.VehicleParams>(e) ? world.GetComponentRO<CarKinem.Core.VehicleParams>(e).MaxSpeedFwd : 0f;
                        double reference = cap ?? (top > 0f ? top : CapReferenceSpeed);
                        cap = Math.Max(0.0, factor * reference);
                    }
                }
            }
            into.Add(new TrafficHandover(world.GetComponentRO<NetworkIdentity>(e).Value, intent.IntentId,
                nx, ny, Math.Atan2(ay - ny, ax - nx), speed, dx, dy, vType, priority, cap));
        }
        return into.ToArray();
    }
}
