using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Look
{
    /// <summary>⭐ <c>CE-3252</c> (LD-D) — who decides where he looks right now. ⚠ Member names unique (one IDL scope per file).</summary>
    public enum LookSource : byte
    {
        /// <summary>Nothing: his eyes follow his body.</summary>
        LookFree = 0,
        /// <summary>A behaviour's look request (an order, a sector, the last window).</summary>
        LookOrdered = 1,
        /// <summary>His weapon's aim (W-14 wins while he aims).</summary>
        LookAim = 2,
        /// <summary>A short glance off his way while he walks (LD-C).</summary>
        LookGlance = 3,
    }

    /// <summary>
    /// ⭐ <c>CE-3252</c> (LD-1 / LD-2, R-2048, R-2049; LD-E) — WHERE A PERSON LOOKS, on the node that owns his body (the Muscle group),
    /// replicated to every node (<c>dtLookState</c>: <see cref="Target"/>, <see cref="HasTarget"/>, <see cref="Source"/>,
    /// <see cref="HeadYaw"/>). The look-at executor writes the WANTED look (<see cref="LookOf.Want"/>); the gait turns the body to a
    /// lasting one (<c>CarKinematicsSystem</c>); <see cref="LookFollowSystem"/> writes the head. Vision, the map and HTTP read it through
    /// <see cref="LookOf"/>. 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a, §3.2b.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LookState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct LookState
    {
        /// <summary>The point he was asked to look at (world m); followed each frame while <see cref="TargetEntity"/> lives.</summary>
        public Vector3 Target;
        /// <summary>The entity he was asked to look at (this node's; null for a point). ⛔ Not on the wire.</summary>
        public Entity TargetEntity;
        /// <summary>1 while a look request stands (0 = none — the head returns to the body).</summary>
        public byte HasTarget;
        /// <summary>Who decides the look now.</summary>
        public LookSource Source;
        /// <summary>The head's turn off the body (radians, + = to his left, within ±<see cref="LookOf.MaxHeadYaw"/>).</summary>
        public float HeadYaw;
        /// <summary>LD-C — when the current glance began (sim s; 0 = none). ⛔ Not on the wire.</summary>
        public double GlanceSince;
        /// <summary>LD-C — no glance before this (sim s): the head rests on the way between glances. ⛔ Not on the wire.</summary>
        public double RestUntil;
    }

    /// <summary>⭐ <c>CE-3252</c> — the ONE reader (vision's cone, the map's cone, HTTP) and the ONE writer of the wanted look.</summary>
    public static class LookOf
    {
        /// <summary>LD-C — how far the head turns off the body.</summary>
        public static readonly float MaxHeadYaw = 70f * MathF.PI / 180f;
        /// <summary>LD-C — the longest glance while walking (s).</summary>
        public const double GlanceSeconds = 2.0;
        /// <summary>LD-C — the head rests on the way at least this long between glances (s).</summary>
        public const double RestSeconds = 1.0;

        /// <summary>The yaw of a rotation's forward (X-forward, Z-up), radians, 0 = +X.</summary>
        public static float YawOf(Quaternion rotation)
        {
            var f = Vector3.Transform(Vector3.UnitX, rotation);
            return MathF.Atan2(f.Y, f.X);
        }

        /// <summary>⭐ <c>CE-1204</c> (Entity Control §8l LK-3, R-1017) — how far <paramref name="e"/>'s head turns off his body (radians):
        /// his type's <c>Gen.VehicleParameters.HeadYawLimitDeg</c>, else <see cref="MaxHeadYaw"/>. The ONE neck — his eyes
        /// (<see cref="LookFollowSystem"/>) and the operator's free look both read it.</summary>
        public static float HeadLimit(ISimulationView view, Entity e)
        {
            if (view is EntityRepository repo && Combat.CombatTkb.TryGetTemplate(repo, e, out var t)
                && t.GetDescriptor<Tkb.Domain.VehicleParametersDto>() is { HeadYawLimitDeg: > 0f } p)
                return p.HeadYawLimitDeg * MathF.PI / 180f;
            return MaxHeadYaw;
        }

        /// <summary>The head's turn off the body (0 without a <see cref="LookState"/>).</summary>
        public static float HeadYaw(ISimulationView view, Entity e)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<LookState>()) return 0f;
            return view.HasComponent<LookState>(e) ? view.GetComponentRO<LookState>(e).HeadYaw : 0f;
        }

        /// <summary>The direction his eyes point: the body's forward turned by the head about Z. With no head turn it is exactly the
        /// body's forward, so a vehicle, an aircraft or a man with no look reads as before.</summary>
        public static Vector3 EyeForward(ISimulationView view, Entity e, Quaternion bodyRotation)
        {
            var forward = Vector3.Transform(Vector3.UnitX, bodyRotation);
            float head = HeadYaw(view, e);
            return head == 0f ? forward : Vector3.Transform(forward, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, head));
        }

        /// <summary>LD-G's muscle half — the executor's write: look at <paramref name="point"/>, or follow <paramref name="target"/>
        /// (alive, with a transform) when given. Adds the component when the type is registered. False when it is not.</summary>
        public static bool Want(EntityRepository world, Entity e, Vector3 point, Entity target = default)
        {
            if (!world.IsComponentTypeRegistered<LookState>() || !world.IsAlive(e)) return false;
            if (!world.HasComponent<LookState>(e)) world.AddComponent(e, default(LookState));
            ref var look = ref world.GetComponentRW<LookState>(e);
            bool follow = !target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target);
            look.Target = follow ? world.GetComponentRO<SimTransform>(target).Position : point;
            look.TargetEntity = follow ? target : Entity.Null;
            look.HasTarget = 1;
            look.Source = LookSource.LookOrdered;
            return true;
        }

        /// <summary>Stop looking: the head returns to the body.</summary>
        public static void Release(EntityRepository world, Entity e)
        {
            if (!world.IsComponentTypeRegistered<LookState>() || !world.HasComponent<LookState>(e)) return;
            ref var look = ref world.GetComponentRW<LookState>(e);
            look.HasTarget = 0;
            look.TargetEntity = Entity.Null;
        }

        /// <summary>Wrap an angle into (−π, π].</summary>
        public static float Wrap(float a)
        {
            while (a > MathF.PI) a -= 2f * MathF.PI;
            while (a <= -MathF.PI) a += 2f * MathF.PI;
            return a;
        }
    }
}
