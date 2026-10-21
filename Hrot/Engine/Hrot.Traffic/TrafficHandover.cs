using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;


namespace Hrot.Traffic;

/// <summary>⭐ AQ93 §14 G3 — one of our vehicles handed to SUMO, as the stepping thread spawns its shadow: NET coordinates; (X, Y) is the
/// FRONT BUMPER (SumoSharp H13's position convention); heading in radians, 0 = east, counter-clockwise (atan2).</summary>
public readonly record struct TrafficHandover(long NetworkId, uint IntentId, double X, double Y, double Heading, double Speed,
                                              double DestX, double DestY, string? VType);

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
    private readonly TrafficRuntime _runtime;
    private readonly List<TrafficHandover> _scratch = new();

    public TrafficHandoverSystem(TrafficRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;
        var net = TrafficNetCache.Of(world);
        _runtime.SetHandovers(net is null ? Array.Empty<TrafficHandover>() : Gather(world, net.Placement, _scratch));
    }

    /// <summary>The hand-over requests of <paramref name="world"/>, a new array (the stepping thread keeps it).</summary>
    public static TrafficHandover[] Gather(EntityRepository world, SumoNetPlacement placement, List<TrafficHandover>? scratch = null)
    {
        var into = scratch ?? new List<TrafficHandover>();
        into.Clear();
        if (!world.IsComponentTypeRegistered<NavigationIntent>() || !world.IsComponentTypeRegistered<SimTransform>()
            || !world.IsComponentTypeRegistered<NetworkIdentity>()) return Array.Empty<TrafficHandover>();
        bool velocity = world.IsComponentTypeRegistered<SimVelocity>();
        bool sized = world.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>();
        bool typed = world.IsComponentTypeRegistered<TkbIdentity>();
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
            var (dx, dy) = placement.ToNet(intent.FinalDestination.X, intent.FinalDestination.Y);
            float speed = velocity && world.HasComponent<SimVelocity>(e)
                ? new Vector2(world.GetComponentRO<SimVelocity>(e).Linear.X, world.GetComponentRO<SimVelocity>(e).Linear.Y).Length() : 0f;
            string? vType = typed && world.HasComponent<TkbIdentity>(e)
                ? world.GetComponentRO<TkbIdentity>(e).TkbType.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            into.Add(new TrafficHandover(world.GetComponentRO<NetworkIdentity>(e).Value, intent.IntentId,
                nx, ny, Math.Atan2(ay - ny, ax - nx), speed, dx, dy, vType));
        }
        return into.ToArray();
    }
}
