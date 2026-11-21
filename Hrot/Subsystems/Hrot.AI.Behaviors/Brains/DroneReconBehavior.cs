using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Motion;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-7028</c> — the recon drone's orbit and its working state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DroneReconParams
    {
        public Vector3 Point;
        public float RadiusM;
        public float HeightAgl;
        public SimInstant StartedAt;
        public SimInstant NextSteer;
        public byte Started;
        /// <summary>1 once its battery (or, CE-7014, its link) is gone: it comes down where it is.</summary>
        public byte Down;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7028</c> (City Battle §11 R-1c) — the RECON drone over an area: it flies to the point and CIRCLES it at <see cref="DroneReconParams.HeightAgl"/>
    /// (the FPV's search orbit — straight at the point from afar, then the point 30° ahead on the circle), its camera filling its memory; at its
    /// battery's end — or in an enemy jammer's bubble (CE-7014) — it comes down where it is. ⭐ THE OBSERVER CHAIN IS UNCHANGED: as a member of its
    /// team's squad, its memory merges into the squad's pool (<c>SquadPerceptionMergeSystem</c>), the staff SPOTs the pool up, and an
    /// indirect-fire element there becomes a FireSupport request (<c>StaffComposerSystem.ComposeSupport</c>) the HQ routes to the guns.
    /// <para>⭐ Parallel (R-4212): the drone changes only its own locomotion channel; the jammer index is the serial pre-pass's.</para>
    /// </summary>
    public static class DroneReconBehavior
    {
        public const float DefaultRadiusM = 150f, DefaultHeightAgl = 80f, ApproachFactor = 1.2f;
        public const double SteerPeriod = 0.5;

        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public DroneReconParams Recon;
        }

        [BehaviorResolver(BehaviorNames.DroneRecon)]
        public static void Resolve(in DroneReconOrder order, ref Blackboard block, EntityRepository world, Entity self)
        {
            var at = (Vector3)order.Location;
            block.Recon = new DroneReconParams
            {
                Point = new Vector3(at.X, at.Y, Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? at.Z),
                RadiusM = order.RadiusM > 0f ? order.RadiusM : DefaultRadiusM,
                HeightAgl = order.HeightAgl > 0f ? order.HeightAgl : DefaultHeightAgl,
            };
        }

        [Behavior(BehaviorNames.DroneRecon, Params = typeof(DroneReconOrder), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Circles an area and watches it (a recon drone).")]
        [BTreeDefinition(BehaviorNames.DroneRecon, Curated = true, Group = "Library/Orders", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Recon, Recon);

        /// <summary>One tick: circle the point at the height; battery or link gone ⇒ down where it is (Success once on the ground).</summary>
        [SharedAiAction]
        public static NodeStatus Recon(ref DroneReconParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (p.Started == 0) { p.Started = 1; p.StartedAt = (SimInstant)now; p.NextSteer = default; }
            Vector3 at = world.GetComponentRO<SimTransform>(self).Position;
            var flight = world.HasComponent<FlightParams>(self) ? world.GetComponentRO<FlightParams>(self) : default;
            float speed = flight.MaxSpeedMps > 0f ? flight.MaxSpeedMps : 20f;
            if (p.Down == 0 && ((flight.EnduranceSeconds > 0f && now - p.StartedAt >= flight.EnduranceSeconds) || DroneJamming.Jammed(world, self, at)))
            {
                p.Down = 1;
                p.NextSteer = default;
            }
            if (now < p.NextSteer) return NodeStatus.Running;
            p.NextSteer = (SimInstant)(now + SteerPeriod);
            if (p.Down != 0)
            {
                if (world.HasComponent<FlightState>(self) && world.GetComponentRO<FlightState>(self).Airborne == 0) return NodeStatus.Success;
                float ground = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f;
                return LocomotionMoveTo.Issue(world, self, new Vector3(at.X, at.Y, ground), speed, 0f, source: MoveSource.Order) ? NodeStatus.Running : NodeStatus.Failure;
            }
            var off = new Vector2(at.X - p.Point.X, at.Y - p.Point.Y);
            Vector3 to;
            if (off.Length() > ApproachFactor * p.RadiusM) to = p.Point;
            else
            {
                float bearing = MathF.Atan2(off.Y, off.X) + AirAttackNodes.OrbitLeadRad;
                to = p.Point + new Vector3(MathF.Cos(bearing) * p.RadiusM, MathF.Sin(bearing) * p.RadiusM, 0f);
            }
            return LocomotionMoveTo.Issue(world, self, new Vector3(to.X, to.Y, p.Point.Z + p.HeightAgl), speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Order)
                ? NodeStatus.Running : NodeStatus.Failure;
        }
    }

    /// <summary>⭐ <c>CE-7028</c> — the authored order: the area to watch; a zero field keeps its default.</summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct DroneReconOrder
    {
        public const string BehaviorId = BehaviorNames.DroneRecon;

        [System.Text.Json.Serialization.JsonPropertyName("Location")] public Fdp.Toolkit.Replication.PointLink Location;
        /// <summary>The orbit's radius (m); 0 = 150.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("RadiusM")] public float RadiusM;
        /// <summary>Its height over the point (m); 0 = 80.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("HeightAgl")] public float HeightAgl;
    }
}
