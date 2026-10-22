using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 §14 G4/G5 (R-4023) — on the node that MOVES a body (it owns its <c>SimTransform</c>, as <c>CarKinematicsSystem</c> asks),
/// a body in <see cref="KinematicsMode.Traffic"/> follows its SUMO shadow car <c>hrot:&lt;networkId&gt;</c> — the pose every node
/// already resolves into the <see cref="TrafficFrame"/>. Ownership never moves; NED publishes the pose as usual.
/// </summary>
/// <remarks>
/// <para>⭐ The result goes through the executor that already owns it (<c>NavigationExecutionSystem</c>), never written here:
/// no shadow yet ⇒ <c>NavState.IsBlocked</c> (a deliberate hold — it does not count as stuck); the shadow gone near the destination
/// ⇒ <c>NavState.HasArrived</c> (⇒ <c>Arrived</c>); gone anywhere else (the traffic node died, SUMO rerouted it away) ⇒ mode
/// <c>None</c>, and the executor's normal blocked / replan path takes over.</para>
/// </remarks>
[UpdateInPhase(SystemPhase.Simulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class TrafficShadowFollowSystem : IEcsModuleSystem
{
    /// <summary>A SUMO shadow car's name: the handed-over body's network id.</summary>
    public const string ShadowPrefix = "hrot:";

    /// <summary>How near the destination the shadow must vanish for an arrival. 📐 SUMO ends it at the destination's lane offset
    /// (H13) but up to one step SHORT of it (≤ 7 m at 14 m/s, Dt 0.5), and the mirror resolves one step behind the clock (another
    /// ≤ 7 m): a shadow that arrived is last seen ~14 m short (measured 2026-10-17, demo-city). 25 m keeps the margin.</summary>
    public const float ArrivalSlackMetres = 25f;

    private readonly Dictionary<string, TrafficCarPose> _shadows = new(StringComparer.Ordinal);

    public static string ShadowIdOf(long networkId) => ShadowPrefix + networkId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<NavState>()) return;
        if (!world.IsComponentTypeRegistered<NetworkIdentity>()) return;
        // ⭐ R-3039 S1 — the follow's memory is a component on the body (TrafficPack registers it). ⛔ Never a silent fallback to "never seen".
        if (!world.IsComponentTypeRegistered<TrafficShadowFollow>())
            throw new InvalidOperationException("TrafficShadowFollowSystem needs TrafficShadowFollow registered (TrafficPack registers it).");
        double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0.0;

        _shadows.Clear();
        if (TrafficFrame.Of(world) is { } frame)
            foreach (var car in frame.Cars)
                if (car.Id is { } id && id.StartsWith(ShadowPrefix, StringComparison.Ordinal)) _shadows[id] = car;

        foreach (var e in world.Query().With<NavState>().With<SimTransform>().WithOwned<SimTransform>().With<NetworkIdentity>().Build())
        {
            var nav = world.GetComponent<NavState>(e);
            if (nav.Mode != KinematicsMode.Traffic) continue;
            long id = world.GetComponentRO<NetworkIdentity>(e).Value;
            var f = world.HasComponent<TrafficShadowFollow>(e) ? world.GetComponentRO<TrafficShadowFollow>(e) : default;

            if (_shadows.TryGetValue(ShadowIdOf(id), out var shadow))
            {
                Follow(world, e, shadow, f, now);
                world.AddComponent(e, new TrafficShadowFollow { Seen = 1, LastX = shadow.X, LastY = shadow.Y, At = now });
                if (nav.IsBlocked != 0) { nav.IsBlocked = 0; world.SetComponent(e, nav); }
                continue;
            }
            if (f.Seen == 0)
            {
                // Waiting for SUMO to spawn the shadow: a deliberate hold, not "stuck".
                if (nav.IsBlocked == 0) { nav.IsBlocked = 1; world.SetComponent(e, nav); }
                continue;
            }
            // The shadow is gone: arrived near the destination, or lost.
            var at = world.GetComponentRO<SimTransform>(e).Position;
            float reach = MathF.Max(nav.ArrivalRadius, ArrivalSlackMetres);
            bool arrived = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y)) <= reach;
            nav.Mode = KinematicsMode.None;
            nav.TargetSpeed = 0f;
            nav.IsBlocked = 0;
            nav.HasArrived = arrived ? (byte)1 : (byte)0;
            world.SetComponent(e, nav);
            ResetMotion(world, e);
            world.RemoveComponent<TrafficShadowFollow>(e);
        }
        // A body that left the mode some other way (a new intent), or that this node no longer moves: forget its follow.
        // (EntityQuery walks an index, so removing the queried component here is safe.)
        foreach (var e in world.Query().With<TrafficShadowFollow>().Build())
        {
            bool following = world.HasComponent<NavState>(e) && world.GetComponentRO<NavState>(e).Mode == KinematicsMode.Traffic
                             && world.HasComponent<SimTransform>(e) && world.HasAuthority<SimTransform>(e) && world.HasComponent<NetworkIdentity>(e);
            if (!following) world.RemoveComponent<TrafficShadowFollow>(e);
        }
    }

    private static void Follow(EntityRepository world, Entity e, in TrafficCarPose shadow, in TrafficShadowFollow f, double now)
    {
        var tf = world.GetComponent<SimTransform>(e);
        tf.Position = new Vector3(shadow.X, shadow.Y, shadow.Z);
        tf.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, shadow.YawRad);
        world.SetComponent(e, tf);

        var here = new Vector2(shadow.X, shadow.Y);
        var v = f.Seen != 0 && now > f.At ? (here - new Vector2(f.LastX, f.LastY)) / (float)(now - f.At) : Vector2.Zero;
        if (world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(e))
        {
            var vel = world.GetComponent<SimVelocity>(e);
            vel.Linear = new Vector3(v, 0f);
            world.SetComponent(e, vel);
        }
        if (world.IsComponentTypeRegistered<VehicleState>() && world.HasComponent<VehicleState>(e))
        {
            var st = world.GetComponent<VehicleState>(e);
            st.Speed = v.Length();   // ⭐ so the kinematics resume from the speed it really had (D7: never from a stale state)
            world.SetComponent(e, st);
        }
    }

    // Taken back: our kinematics resume at rest, where the shadow left the body.
    private static void ResetMotion(EntityRepository world, Entity e)
    {
        if (world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(e))
            world.SetComponent(e, new SimVelocity());
        if (world.IsComponentTypeRegistered<VehicleState>() && world.HasComponent<VehicleState>(e))
        {
            var st = world.GetComponent<VehicleState>(e);
            st.Speed = 0f;
            st.Accel = 0f;
            world.SetComponent(e, st);
        }
    }
}
