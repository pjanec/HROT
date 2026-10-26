using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2307</c> (CH-1, CH-2, Urban Battle §4 ⑤; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5b) — <c>PushThrough</c>: a
    /// carrier hit while it drives keeps going — <see cref="PushThroughParams.Metres"/> on along its heading (its velocity as this node knows it;
    /// standing, its facing), then Success; the vehicle SOP's reaction ends and its task (the route) resumes. Failure on the timeout or when it
    /// cannot move. ⛔ It never stops in the kill zone: that is the drill.
    /// </summary>
    public static class PushThroughBehavior
    {
        public const float DefaultMetres = 150f;
        public const float DefaultSpeed = 15f;
        public const float DefaultTimeoutSeconds = 30f;
        public const float ArrivalRadius = 8f;

        [StructLayout(LayoutKind.Sequential)]
        public struct PushThroughParams
        {
            public float Metres;
            public float Speed;
            public float TimeoutSeconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PushThroughState
        {
            /// <summary>When it started (0 = not yet) and where it drives to.</summary>
            public double Since;
            public Vector3 To;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PushThroughBlackboard
        {
            public PushThroughParams Params;
            public PushThroughState State;
        }

        [BehaviorResolver(BehaviorNames.PushThrough)]
        public static void Resolve(in PushThroughParamsJsonDto authored, ref PushThroughBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new PushThroughParams
            {
                Metres = authored.Metres > 0f ? authored.Metres : DefaultMetres,
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
                TimeoutSeconds = authored.TimeoutSeconds > 0f ? authored.TimeoutSeconds : DefaultTimeoutSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.PushThrough, Params = typeof(PushThroughParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.PushThrough, Curated = true, Group = "Library/Vehicle", ParamsType = typeof(PushThroughBlackboard))]
        public static BTreeBuilder<PushThroughBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<PushThroughBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Drive);

        /// <summary>One tick: the first picks the point ahead and drives there; Success on arrival.</summary>
        public static NodeStatus Drive(ref PushThroughParams p, ref PushThroughState ws, Entity self, EntityRepository world)
        {
            if (!ConvoyConditions.CanMove(world, self) || !world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            if (ws.Since <= 0)
            {
                ws.Since = now;
                ws.To = Ahead(world, self, p.Metres);
                if (!LocomotionMoveTo.Issue(world, self, ws.To, p.Speed, ArrivalRadius, MoveGait.Keep, onSurface: true, source: MoveSource.Route)) return NodeStatus.Failure;
                return NodeStatus.Running;
            }
            var at = world.GetComponentRO<SimTransform>(self).Position;
            if (new Vector2(ws.To.X - at.X, ws.To.Y - at.Y).Length() <= ArrivalRadius || LocomotionMoveTo.Status(world, self) == NodeStatus.Success)
                return NodeStatus.Success;
            if (now - ws.Since >= p.TimeoutSeconds) return NodeStatus.Failure;
            return NodeStatus.Running;
        }

        /// <summary>The point <paramref name="metres"/> ahead: along its velocity when it moves, else along its facing (+Y turned by its rotation).</summary>
        public static Vector3 Ahead(EntityRepository world, Entity self, float metres)
        {
            var at = world.GetComponentRO<SimTransform>(self).Position;
            Vector2 dir = default;
            if (Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out _, out var velocity) && velocity.Length() > Fdp.Toolkit.Spatial.Eqs.VehicleCover.StandingSpeed)
                dir = new Vector2(velocity.X, velocity.Y);
            if (dir.LengthSquared() < 1e-4f)
            {
                var f = Vector3.Transform(Vector3.UnitY, world.GetComponentRO<SimTransform>(self).Rotation);
                dir = new Vector2(f.X, f.Y);
            }
            if (dir.LengthSquared() < 1e-4f) dir = Vector2.UnitY;
            dir = Vector2.Normalize(dir) * metres;
            return new Vector3(at.X + dir.X, at.Y + dir.Y, at.Z);
        }
    }

    /// <summary>⭐ <c>CE-2307</c> (CH-1, CH-4) — the vehicle SOP's push-through test: the carrier has a live PASSENGER aboard (a passenger
    /// seat — not its crew, not a weapon in a station), it DRIVES (not standing) and it can move (no mobility kill).</summary>
    public static class ConvoyConditions
    {
        [SharedAiCondition]
        public static unsafe bool CanPushThrough(Entity self, EntityRepository world)
            => CanMove(world, self) && !Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsStanding(world, self) && CarriesPassenger(world, self);

        public static bool CanMove(EntityRepository world, Entity self)
            => world.IsAlive(self) && CombatLife.IsAlive(world, self)
               && (!world.HasComponent<ActorCapabilityState>(self)
                   || (world.GetComponentRO<ActorCapabilityState>(self).Capabilities & ActorCapabilities.CanMove) != 0);

        public static unsafe bool CarriesPassenger(EntityRepository world, Entity self)
        {
            if (!world.IsComponentTypeRegistered<PassengerBuffer>() || !world.HasComponent<PassengerBuffer>(self)) return false;
            var buffer = world.GetComponentRO<PassengerBuffer>(self);
            var seats = world.IsComponentTypeRegistered<CarrierSeats>() && world.HasComponent<CarrierSeats>(self)
                ? world.GetComponentRO<CarrierSeats>(self) : default;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var e = buffer.Passengers[i];
                if (e.IsNull || !world.IsAlive(e)) continue;
                if (i < seats.SeatCount && seats.Roles[i] != SeatRole.Passenger) continue;   // its crew, or a weapon in a station
                if (Injury.IsInTheFight(world, e)) return true;
            }
            return false;
        }
    }
}
