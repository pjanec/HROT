using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-5 (IM-1, MV-12; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — <c>DriveBy</c>, run by the BIKE:
    /// ① ride up to the target at speed · ② within <c>PassMetres</c>: order the PILLION (the passenger in a firing seat) to <c>FireAtTarget</c>
    /// (an assignment — an event, never a write to him) and pass the target slowly, 30 m beyond it · ③ escape to the pre-chosen point at speed,
    /// Success there. ⭐ The pillion's shot is bounded by his seat's arc (<c>SeatHold</c>, M-2): he fires to the side and behind, never ahead.
    /// Reuses the move channel, the firing seat and <c>FireAtTarget</c>. ⚠ Not built (L-14): the traffic handover before the act and the
    /// re-blend after it.
    /// </summary>
    public static class DriveByBehavior
    {
        public const float DefaultPassSpeed = 4f, DefaultSpeed = 14f, DefaultPassMetres = 40f, BeyondMetres = 30f, ArrivalRadius = 6f;
        public const byte RidingUp = 0, Passing = 1, Escaping = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct DriveByParams
        {
            public int Target;
            public Vector3 Escape;
            public float PassSpeed;
            public float Speed;
            public float PassMetres;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DriveByState
        {
            public byte Phase;
            public byte Issued;
            /// <summary>Where the current ride-up move points (re-issued when the target has moved 5 m).</summary>
            public Vector3 Towards;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DriveByBlackboard
        {
            public DriveByParams Params;
            public DriveByState State;
        }

        [BehaviorResolver(BehaviorNames.DriveBy)]
        public static void Resolve(in DriveByParamsJsonDto authored, ref DriveByBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new DriveByParams
            {
                Target = authored.Target.Ref.NetworkId,
                Escape = authored.Escape,
                PassSpeed = authored.PassSpeed > 0f ? authored.PassSpeed : DefaultPassSpeed,
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
                PassMetres = authored.PassMetres > 0f ? authored.PassMetres : DefaultPassMetres,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.DriveBy, Params = typeof(DriveByParamsJsonDto), Classes = new[] { "Motorcycle" },
            Description = "Rides up to a target, passes it slowly while the pillion fires, and escapes at speed.")]
        [BTreeDefinition(BehaviorNames.DriveBy, Curated = true, Group = "Library/Insurgent", ParamsType = typeof(DriveByBlackboard))]
        public static BTreeBuilder<DriveByBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DriveByBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref DriveByParams p, ref DriveByState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            var source = LocomotionMoveTo.OrderSource(world, self);
            if (s.Phase == Escaping)
            {
                if (s.Issued == 0)
                {
                    if (!LocomotionMoveTo.Issue(world, self, p.Escape, p.Speed, ArrivalRadius, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                    s.Issued = 1;
                    return NodeStatus.Running;
                }
                return LocomotionMoveTo.Status(world, self);
            }

            var target = new Fdp.Toolkit.Replication.EntityRef(p.Target).Resolve(world);
            if (target.IsNull || !world.HasComponent<SimTransform>(target))
            {
                s.Phase = Escaping; s.Issued = 0;        // the target is gone: get away
                return NodeStatus.Running;
            }
            var at = world.GetComponentRO<SimTransform>(target).Position;
            if (s.Phase == RidingUp)
            {
                if (Vector2.Distance(new Vector2(here.X, here.Y), new Vector2(at.X, at.Y)) <= p.PassMetres)
                {
                    var dir = new Vector2(at.X - here.X, at.Y - here.Y);
                    dir = dir.LengthSquared() > 1e-4f ? Vector2.Normalize(dir) : Vector2.UnitX;
                    var beyond = new Vector3(at.X + dir.X * BeyondMetres, at.Y + dir.Y * BeyondMetres, at.Z);
                    if (!LocomotionMoveTo.Issue(world, self, beyond, p.PassSpeed, ArrivalRadius, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                    OrderThePillion(world, self, p.Target);
                    s.Phase = Passing;
                    return NodeStatus.Running;
                }
                if (s.Issued == 0 || Vector3.DistanceSquared(s.Towards, at) > 25f)
                {
                    if (!LocomotionMoveTo.Issue(world, self, at, p.Speed, ArrivalRadius, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                    s.Towards = at; s.Issued = 1;
                }
                return NodeStatus.Running;
            }
            // Passing: until the pass move ends (arrived beyond the target, or it failed) — then escape
            if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
            s.Phase = Escaping; s.Issued = 0;
            return NodeStatus.Running;
        }

        /// <summary>The pillion: a man in a FIRING seat that is not the driver's (the bike's own passenger list — its own component).</summary>
        public static Entity PillionOf(EntityRepository world, Entity bike)
        {
            if (!world.HasComponent<PassengerBuffer>(bike)) return Entity.Null;
            ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(bike);
            bool seats = world.HasComponent<CarrierSeats>(bike);
            bool stations = world.HasComponent<CarrierStations>(bike);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var man = list.Passengers[i];
                if (man.IsNull) continue;
                if (seats && i < world.GetComponentRO<CarrierSeats>(bike).SeatCount && world.GetComponentRO<CarrierSeats>(bike).Roles[i] == SeatRole.Driver) continue;
                if (stations && world.GetComponentRO<CarrierStations>(bike).IsFiringSeat(i)) return man;
            }
            return Entity.Null;
        }

        private static void OrderThePillion(EntityRepository world, Entity bike, int targetNetId)
        {
            var pillion = PillionOf(world, bike);
            if (pillion.IsNull || !world.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) return;
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = pillion, BehaviorName = BehaviorNames.FireAtTarget,
                JsonParams = "{\"targetNetworkId\":" + targetNetId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                Origin = BehaviorOrigin.Superior,
            });
        }
    }
}
