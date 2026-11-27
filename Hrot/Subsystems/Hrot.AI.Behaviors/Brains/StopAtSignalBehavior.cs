using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> follow-up (R-2133's ⏭; 📄 docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-2, CP-3) — <c>StopAtSignal</c>: a vehicle the
    /// checkpoint SIGNALS slows and stops at the stop line, whatever it was doing — a REACTION the checkpoint sends it at the signal (an event, never a write;
    /// the reaction rules decide — a vehicle whose ROE keeps it on its task never takes it — and a vehicle under a RAM order, the VBIED or the rammer, ignores it). It holds there while it is searched and
    /// ends once its people have got out and back in (released), or nobody got out within <see cref="WavedOnSeconds"/> (waved on) — the task it paused
    /// (a route, the traffic) then goes on. 📐 Before: every checkpoint scenario authored the car's stop in its route.
    /// </summary>
    public static class StopAtSignalBehavior
    {
        /// <summary>It closes on the line at <see cref="ApproachSpeed"/> (m/s) — below the ladder's "ignored the signal" fraction of a normal approach — and stops
        /// within <see cref="ArrivalMetres"/> of it (inside the watch's "at the line", 12 m). 📐 A car's path follower cannot hit a 3 m circle: with two legs
        /// (8 m/s to 30 m short, 3 m/s on) it overshot and looped back ~20 s at each point and never read as stopped (cp-routine-car).</summary>
        public const float ApproachSpeed = 5f, ArrivalMetres = 8f;
        /// <summary>The last stretch (m) is driven at <see cref="CreepSpeed"/> so it can stop at the line — 📐 at 5 m/s it rolled through the line (cp-routine-car).</summary>
        public const float CreepMetres = 20f, CreepSpeed = 2f;
        /// <summary>Stopped and nobody asked out within this (s): waved on. ⚠ Only a safety: the checkpoint searches every routine vehicle standing at its line
        /// (K-6), and its "out, hands up" came ~33 s after the stop on <c>cp-routine-car</c> — 📐 at 20 s the car was released first, drove on inside the
        /// stand-off and read as a THREAT.</summary>
        public const float WavedOnSeconds = 120f;
        /// <summary>Whatever happens, it gives up after this (s).</summary>
        public const float LimitSeconds = 300f;

        [StructLayout(LayoutKind.Sequential)]
        public struct StopAtSignalParams { public Vector3 StopAt; }

        [StructLayout(LayoutKind.Sequential)]
        public struct StopAtSignalState
        {
            public SimInstant Since, StoppedAt;
            /// <summary>Where it stops: the line, on its OWN course (abeam of the checkpoint's stop point along its heading at the signal).</summary>
            public Vector3 Line, Short;
            /// <summary>0 closing on the line · 1 stopped.</summary>
            public byte Phase, Issued, Left, AboardAtStop;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StopAtSignalBlackboard
        {
            public StopAtSignalParams Params;
            public StopAtSignalState State;
        }

        [BehaviorResolver(BehaviorNames.StopAtSignal)]
        public static void Resolve(in StopAtSignalParamsJsonDto authored, ref StopAtSignalBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new StopAtSignalParams { StopAt = new Vector3(authored.X, authored.Y, 0f) };
            block.State = default;
        }

        [Behavior(BehaviorNames.StopAtSignal, Conduct = Fdp.Toolkit.Behavior.Conduct.Halts, Params = typeof(StopAtSignalParamsJsonDto),
            Categories = BehaviorCategory.Civilian | BehaviorCategory.Insurgent, Description = "A vehicle signalled by a checkpoint slows and stops at the line until it is let go.")]
        [BTreeDefinition(BehaviorNames.StopAtSignal, Curated = true, Group = "Library/Civilian", ParamsType = typeof(StopAtSignalBlackboard))]
        public static BTreeBuilder<StopAtSignalBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<StopAtSignalBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref StopAtSignalParams p, ref StopAtSignalState s, Entity self, EntityRepository world)
        {
            // ⭐ a vehicle under a RAM order (its own component) never stops for a signal — 📐 cp-ram-barrier: the rammer obeyed and never rammed
            if (world.IsComponentTypeRegistered<global::CarKinem.Core.RamOrder>() && global::CarKinem.Core.RamOrder.On(world, self, out _)) return NodeStatus.Success;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            if (s.Since <= 0) s.Since = (SimInstant)now;
            if (now - s.Since >= LimitSeconds) return NodeStatus.Success;
            if (s.Phase == 0)
            {
                if (s.Issued == 0)
                {
                    s.Line = LineOnCourse(in p, self, world, 0f);
                    s.Short = LineOnCourse(in p, self, world, CreepMetres);
                    bool far = s.Short != s.Line;
                    if (!LocomotionMoveTo.Issue(world, self, far ? s.Short : s.Line, far ? ApproachSpeed : CreepSpeed, ArrivalMetres, MoveGait.Keep, onSurface: true))
                        return NodeStatus.Failure;
                    s.Issued = far ? (byte)1 : (byte)2;
                    return NodeStatus.Running;
                }
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                if (s.Issued == 1)
                {
                    if (!LocomotionMoveTo.Issue(world, self, s.Line, CreepSpeed, ArrivalMetres, MoveGait.Keep, onSurface: true)) return NodeStatus.Failure;
                    s.Issued = 2;
                    return NodeStatus.Running;
                }
                LocomotionMoveTo.Stop(world, self);
                s.Phase = 1; s.StoppedAt = (SimInstant)now; s.AboardAtStop = Aboard(world, self);
                return NodeStatus.Running;
            }
            int aboard = Aboard(world, self);
            if (aboard < s.AboardAtStop) s.Left = 1;                                       // the search: they got out
            if (s.Left != 0) return aboard >= s.AboardAtStop ? NodeStatus.Success : NodeStatus.Running;   // released: all back in
            return now - s.StoppedAt >= WavedOnSeconds ? NodeStatus.Success : NodeStatus.Running;          // nobody asked out: waved on
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.StopAtSignalBehavior.Tick")]
        public static void Deactivate_Tick(ref StopAtSignalParams p, ref StopAtSignalState s, Entity self, EntityRepository world)
        {
            if (s.Phase == 0 && s.Issued != 0) LocomotionMoveTo.Stop(world, self);
            s = default;
        }

        /// <summary>
        /// The line ON ITS OWN COURSE: the point along its heading abeam of the checkpoint's stop point. 📐 Aimed at the stop point itself (off its lane), the
        /// car turned at the signal and wandered. Already at / past it ⇒ where it is. No heading ⇒ the stop point itself.
        /// </summary>
        public static Vector3 LineOnCourse(in StopAtSignalParams p, Entity self, EntityRepository world, float shortBy)
        {
            if (!world.HasComponent<SimTransform>(self)) return p.StopAt;
            var t = world.GetComponentRO<SimTransform>(self);
            var here = (Vector3)t.Position;
            var f3 = Vector3.Transform(Vector3.UnitX, t.Rotation);
            var fwd = new Vector2(f3.X, f3.Y);
            if (fwd.LengthSquared() < 1e-4f) return p.StopAt;
            fwd = Vector2.Normalize(fwd);
            float along = Vector2.Dot(new Vector2(p.StopAt.X - here.X, p.StopAt.Y - here.Y), fwd);
            if (along <= ArrivalMetres) return here;
            if (shortBy > 0f) { if (along - shortBy <= ArrivalMetres) along = along > 0 ? along : 0f; else along -= shortBy; }
            return new Vector3(here.X + fwd.X * along, here.Y + fwd.Y * along, 0f);
        }

        /// <summary>The live people aboard (its own passenger list).</summary>
        public static byte Aboard(EntityRepository world, Entity vehicle)
        {
            if (!world.HasComponent<PassengerBuffer>(vehicle)) return 0;
            var buffer = world.GetComponentRO<PassengerBuffer>(vehicle);
            byte n = 0;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (!buffer.Passengers[i].IsNull && !Fdp.Toolkit.Combat.Items.IsMountable(world, buffer.Passengers[i])) n++;
            return n;
        }
    }
}
