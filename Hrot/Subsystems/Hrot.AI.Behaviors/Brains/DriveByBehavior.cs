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
        /// <summary>⭐ R-3093 (found by ua-moto-drive-by-grenade) — a hand grenade is thrown within this range of the target, or once it is abeam.
        /// ⛔ It used to be ordered at <see cref="DefaultPassMetres"/> (40 m) — beyond a throw's reach from a moving bike: it burst short of the post.</summary>
        public const float ThrowMetres = 25f;

        [StructLayout(LayoutKind.Sequential)]
        public struct DriveByParams
        {
            public int Target;
            public Vector3 Escape;
            public float PassSpeed;
            public float Speed;
            public float PassMetres;
            /// <summary>⭐ IM-1 — 1: the pillion throws one grenade at the pass instead of firing a burst.</summary>
            public byte Throw;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DriveByState
        {
            public byte Phase;
            public byte Issued;
            /// <summary>⭐ R-3093 — 1 once the pillion's throw is ordered (the throw waits for range; the burst is ordered at the pass).</summary>
            public byte Thrown;
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
                Throw = authored.Throw ? (byte)1 : (byte)0,
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
                    var beyond = PassPoint(here, world.GetComponentRO<SimTransform>(self).Rotation, at, p.Escape);
                    if (!LocomotionMoveTo.Issue(world, self, beyond, p.PassSpeed, ArrivalRadius, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                    if (p.Throw == 0) OrderThePillion(world, self, p.Target, null);
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
            // Passing: the throw waits for range (or the target abeam) — R-3093
            if (p.Throw != 0 && s.Thrown == 0 && InThrowingRange(here, world.GetComponentRO<SimTransform>(self).Rotation, at))
            {
                OrderThePillion(world, self, p.Target, at);
                s.Thrown = 1;
            }
            // … until the pass move ends (arrived beyond the target, or it failed) — then escape
            if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
            if (p.Throw != 0 && s.Thrown == 0) { OrderThePillion(world, self, p.Target, at); s.Thrown = 1; }   // never closer: throw on the way out
            s.Phase = Escaping; s.Issued = 0;
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ <c>CE-4621</c> IM-1 (found by the feature scenario <c>ua-moto-drive-by</c>, R-3089) — where the PASS goes: ALONGSIDE, along the bike's own
        /// heading to <see cref="BeyondMetres"/> past the point abeam of the target (design §5: "2–5 m/s alongside"). ⛔ It used to aim
        /// <see cref="BeyondMetres"/> past the target along the bike→target bearing, so a post 14 m off the road pulled the bike off the road and up to
        /// the post at the pass speed — a slow charge at the sentries, not a drive-by.
        /// ⭐ <c>CE-3452</c> — the line is toward the pre-chosen ESCAPE point when that lies past the target (design IM-1: "alongside → the
        /// pre-chosen escape route"); the heading when none is set or the escape is back the way the bike came. 📌 Once the bike drove its ride-up path (CE-3452), the path to the post left the road ~55 m short and the heading
        /// at the 40 m switch pointed at the buildings beside the post: the pass drove into them and the bike stood there until shot
        /// (<c>ua-moto-drive-by-grenade</c>). The heading only worked while the bike looped back onto the road first (the CE-3452 bug).
        /// </summary>
        public static Vector3 PassPoint(Vector3 here, Quaternion rotation, Vector3 at, Vector3 escape = default)
        {
            var fwd = new Vector2(escape.X - here.X, escape.Y - here.Y);
            var toAt = new Vector2(at.X - here.X, at.Y - here.Y);
            // the escape line only when the escape lies PAST the target along it (an escape back the way the bike came ⇒ its heading)
            float escapeLength = fwd.Length();
            if (escape == default || escapeLength < 1f || Vector2.Dot(toAt, fwd / escapeLength) <= 0f || Vector2.Dot(toAt, fwd / escapeLength) >= escapeLength)
            {
                var f = Vector3.Transform(Vector3.UnitX, rotation);
                fwd = new Vector2(f.X, f.Y);
            }
            if (fwd.LengthSquared() < 1e-4f) fwd = new Vector2(at.X - here.X, at.Y - here.Y);
            fwd = fwd.LengthSquared() > 1e-4f ? Vector2.Normalize(fwd) : Vector2.UnitX;
            float along = MathF.Max(0f, Vector2.Dot(new Vector2(at.X - here.X, at.Y - here.Y), fwd)) + BeyondMetres;
            return new Vector3(here.X + fwd.X * along, here.Y + fwd.Y * along, here.Z);
        }

        /// <summary>⭐ R-3093 — within <see cref="ThrowMetres"/> of the target, or the target abeam / behind along the bike's heading.</summary>
        public static bool InThrowingRange(Vector3 here, Quaternion rotation, Vector3 at)
        {
            var d = new Vector2(at.X - here.X, at.Y - here.Y);
            if (d.LengthSquared() <= ThrowMetres * ThrowMetres) return true;
            var f = Vector3.Transform(Vector3.UnitX, rotation);
            var fwd = new Vector2(f.X, f.Y);
            return fwd.LengthSquared() > 1e-4f && Vector2.Dot(d, fwd) <= 0f;   // no heading (an unset rotation) ⇒ range only
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

        /// <summary>
        /// The pillion's order. ⭐ IM-1: with <paramref name="throwAt"/> (the authored <c>Throw</c>) the order is the library <c>FireAtPoint</c> — ONE round, mount −1 =
        /// "the pillion's own throwable" (resolved by <c>FireAtPointBehavior.Resolve</c> on HIS entity, so this brain reads and writes nothing of his) — at the target's
        /// position at the pass; else the burst, <c>FireAtTarget</c>. One assignment event per run either way.
        /// </summary>
        private static void OrderThePillion(EntityRepository world, Entity bike, int targetNetId, Vector3? throwAt = null)
        {
            var pillion = PillionOf(world, bike);
            if (pillion.IsNull || !world.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) return;
            if (throwAt is { } at)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                world.Bus.PublishManaged(new AssignBehaviorEvent
                {
                    Entity = pillion, BehaviorName = BehaviorNames.FireAtPoint,
                    JsonParams = "{\"x\":" + at.X.ToString("R", inv) + ",\"y\":" + at.Y.ToString("R", inv) + ",\"z\":" + at.Z.ToString("R", inv) + ",\"mount\":-1,\"rounds\":1}",
                    Origin = BehaviorOrigin.Superior,
                });
                return;
            }
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = pillion, BehaviorName = BehaviorNames.FireAtTarget,
                JsonParams = "{\"targetNetworkId\":" + targetNetId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                Origin = BehaviorOrigin.Superior,
            });
        }
    }
}
