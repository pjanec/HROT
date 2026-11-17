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
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-5 (IM-2, MV-9; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — <c>ParkedBomb</c>, run by the VEHICLE
    /// (a motorcycle, a car): ride to the spot, stop, arm the cargo charge's timer (<see cref="ChargeTimers.Arm"/> — a follow-up inside the
    /// parallel phase) and put the riders out (<see cref="EjectPassengersCommand"/>, applied by the vehicle's owner). The charge goes off on its
    /// timer through <c>Charges.Ask(charge, Null)</c> (<c>ChargeSystem</c>) after the riders have left. Reuses: the move channel, the charge, the
    /// eject command — no new mechanism.
    /// </summary>
    public static class ParkedBombBehavior
    {
        public const float DefaultDelaySeconds = 30f;
        public const float DefaultSpeed = 12f;
        public const float ArrivalRadius = 4f;
        /// <summary>⭐ R-3090 — how far back along the vehicle's heading the riders walk when no <c>Leave</c> point is authored.</summary>
        public const float LeaveMetres = 80f;
        /// <summary>⭐ R-3090 — the riders' walk (m/s).</summary>
        public const float LeaveSpeed = 1.6f;
        /// <summary>⭐ R-3092 — how many times a blocked ride to the spot is re-ordered before the play gives up.</summary>
        public const byte MaxRetries = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct ParkedBombParams
        {
            /// <summary>The charge's network id (resolved each tick: a load re-allocates handles).</summary>
            public int Charge;
            public Vector3 Spot;
            public SimDuration Delay;
            public float Speed;
            /// <summary>⭐ R-3090 — where the riders walk; zero = <see cref="LeaveMetres"/> back along the heading.</summary>
            public Vector3 Leave;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ParkedBombState
        {
            /// <summary>0 = not yet ordered · 1 = riding to the spot · 2 = armed, the riders getting out (R-3090).</summary>
            public byte Riding;
            /// <summary>⭐ R-3092 — blocked rides retried so far (at most <see cref="MaxRetries"/>).</summary>
            public byte Retries;
            public Entity Rider0, Rider1;
            public Vector3 Leave;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ParkedBombBlackboard
        {
            public ParkedBombParams Params;
            public ParkedBombState State;
        }

        [BehaviorResolver(BehaviorNames.ParkedBomb)]
        public static void Resolve(in ParkedBombParamsJsonDto authored, ref ParkedBombBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new ParkedBombParams
            {
                Charge = authored.Charge.Ref.NetworkId,
                Spot = authored.Spot,
                Delay = (float)authored.Delay > 0f ? authored.Delay : (SimDuration)DefaultDelaySeconds,
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
                Leave = authored.Leave,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.ParkedBomb, Params = typeof(ParkedBombParamsJsonDto), Categories = BehaviorCategory.Civilian | BehaviorCategory.AllMilitary,
            Description = "Rides its cargo bomb to a spot, parks, sets the timer and puts the riders out.")]
        [BTreeDefinition(BehaviorNames.ParkedBomb, Curated = true, Group = "Library/Casualties and engineering", ParamsType = typeof(ParkedBombBlackboard))]
        public static BTreeBuilder<ParkedBombBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ParkedBombBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>One tick: ride to the spot; there, stop, arm and put the riders out ⇒ Success.</summary>
        public static NodeStatus Tick(ref ParkedBombParams p, ref ParkedBombState s, Entity self, EntityRepository world)
        {
            var charge = new Fdp.Toolkit.Replication.EntityRef(p.Charge).Resolve(world);
            if (charge.IsNull || !Charges.IsCharge(world, charge)) return NodeStatus.Failure;
            if (s.Riding == 0)
            {
                if (!LocomotionMoveTo.Issue(world, self, p.Spot, p.Speed, ArrivalRadius, MoveGait.Keep, source: LocomotionMoveTo.OrderSource(world, self)))
                    return NodeStatus.Failure;
                s.Riding = 1;
                return NodeStatus.Running;
            }
            if (s.Riding == 2) return Leaving(ref s, self, world);
            var move = LocomotionMoveTo.Status(world, self);
            if (move == NodeStatus.Running) return NodeStatus.Running;
            if (move == NodeStatus.Failure)
            {
                // ⭐ R-3092 (found by ua-moto-parked-bomb; the stall itself is CE-3452) — a ride the move reports BLOCKED is ordered again, as a
                //   rider restarts a stalled bike; ⛔ before, the first stall ended the whole play and the bomb was never parked
                if (s.Retries >= MaxRetries) return NodeStatus.Failure;
                s.Retries++;
                s.Riding = 0;
                return NodeStatus.Running;
            }
            LocomotionMoveTo.Stop(world, self);
            if (!ChargeTimers.Arm(world, charge, p.Delay)) return NodeStatus.Failure;
            // ⭐ R-3090 — remember who rides (the vehicle's OWN passenger list; never the charge in its cargo station) and where they will walk
            s.Rider0 = s.Rider1 = Entity.Null;
            if (world.HasComponent<PassengerBuffer>(self))
            {
                ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(self);
                for (int i = 0; i < PassengerBuffer.Capacity; i++)
                {
                    var man = list.Passengers[i];
                    if (man.IsNull || man == charge) continue;
                    if (s.Rider0.IsNull) s.Rider0 = man; else if (s.Rider1.IsNull) { s.Rider1 = man; break; }
                }
            }
            ref readonly var here = ref world.GetComponentRO<SimTransform>(self);
            s.Leave = p.Leave != Vector3.Zero ? p.Leave : LeavePoint(here.Position, here.Rotation);
            if (world.Bus.IsRegistered<EjectPassengersCommand>()) world.Bus.Publish(new EjectPassengersCommand { Vehicle = self });
            s.Riding = 2;
            return s.Rider0.IsNull ? NodeStatus.Success : NodeStatus.Running;
        }

        /// <summary>⭐ R-3090 — <see cref="LeaveMetres"/> back along the vehicle's heading (the way it came).</summary>
        public static Vector3 LeavePoint(Vector3 at, Quaternion rotation)
        {
            var f = Vector3.Transform(Vector3.UnitX, rotation);
            var back = new Vector2(-f.X, -f.Y);
            back = back.LengthSquared() > 1e-4f ? Vector2.Normalize(back) : -Vector2.UnitX;
            return new Vector3(at.X + back.X * LeaveMetres, at.Y + back.Y * LeaveMetres, at.Z);
        }

        /// <summary>
        /// ⭐ R-3090 (found by the feature scenario <c>ua-moto-parked-bomb</c>) — once the riders are OFF (the vehicle's own passenger list no longer
        /// holds them), order each to walk to the leave point: the same assignment event <c>DriveBy</c> sends its pillion. ⛔ An order sent while he
        /// is still aboard would land on the embarked man. ⛔ Before it the play ended at the eject and the riders stood beside their own bomb.
        /// </summary>
        private static NodeStatus Leaving(ref ParkedBombState s, Entity self, EntityRepository world)
        {
            if (world.HasComponent<PassengerBuffer>(self))
            {
                ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(self);
                for (int i = 0; i < PassengerBuffer.Capacity; i++)
                    if (!list.Passengers[i].IsNull && (list.Passengers[i] == s.Rider0 || list.Passengers[i] == s.Rider1)) return NodeStatus.Running;
            }
            if (world.Bus.IsRegisteredManaged<AssignBehaviorEvent>())
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string json = "{\"target\":[" + s.Leave.X.ToString("0.##", inv) + "," + s.Leave.Y.ToString("0.##", inv) + "," + s.Leave.Z.ToString("0.##", inv)
                            + "],\"speed\":" + LeaveSpeed.ToString("0.##", inv) + ",\"arrivalRadius\":5}";
                if (!s.Rider0.IsNull) world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = s.Rider0, BehaviorName = BehaviorNames.MoveToLocation, JsonParams = json, Origin = BehaviorOrigin.Superior });
                if (!s.Rider1.IsNull) world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = s.Rider1, BehaviorName = BehaviorNames.MoveToLocation, JsonParams = json, Origin = BehaviorOrigin.Superior });
            }
            return NodeStatus.Success;
        }
    }
}
