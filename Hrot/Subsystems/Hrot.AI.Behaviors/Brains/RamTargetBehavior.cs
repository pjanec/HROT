using System.Numerics;
using System.Runtime.InteropServices;
using CarKinem.Core;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Civilians;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-2 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md RM-2, RM-3, RM-5; KV-5, KV-6) — <c>RamTarget</c>, run by a VEHICLE: it marks
    /// its OWN hull with a <see cref="RamOrder"/> (the mover's avoidance then does not give way to the target — to no person for a crowd — and its breach gate lets it
    /// break a weak barrier), commits a LETHAL hostile act on the CAR (AW-19) and its driver's as a follow-up, and pursues: every
    /// <see cref="ReissueSeconds"/> a move to the target's LEAD point (<see cref="TargetLead.Point"/>, the drone's predictor) at the ram speed. Success when the target
    /// is down or gone (a crowd: when the hull reaches the place). Its deactivator switches the order OFF. ⛔ It hurts nobody itself — the impact does (K-1).
    /// </summary>
    public static class RamTargetBehavior
    {
        public const float DefaultSpeed = 16f, ReissueSeconds = 0.5f, CrowdReachedM = 3f;
        /// <summary>⭐ K-7 (R-2134) — the ram BECOMES a lethal act this close to its target (m): the run-in RM-2 speaks of. 📐 Committed at the order's start
        /// (370 m out on cp-vbied) every soldier fired in self-defence at once and the checkpoint's watch and ladder never had a vehicle to judge.</summary>
        public const float CommitMetres = 50f;

        [StructLayout(LayoutKind.Sequential)]
        public struct RamTargetParams
        {
            public int Target;
            public Vector3 At;
            public float Speed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RamTargetState
        {
            public byte Started;
            public SimInstant NextIssue;
            /// <summary>1 once the ram's lethal act is committed (inside <see cref="CommitMetres"/>).</summary>
            public byte Committed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RamTargetBlackboard
        {
            public RamTargetParams Params;
            public RamTargetState State;
        }

        [BehaviorResolver(BehaviorNames.RamTarget)]
        public static void Resolve(in RamTargetParamsJsonDto authored, ref RamTargetBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new RamTargetParams
            {
                Target = authored.Target.Ref.NetworkId, At = authored.At, Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.RamTarget, Params = typeof(RamTargetParamsJsonDto), Categories = BehaviorCategory.Insurgent,
            Description = "A vehicle drives into a person or a crowd: it does not swerve for its target and may break a weak barrier; the car commits a lethal hostile act.")]
        [BTreeDefinition(BehaviorNames.RamTarget, Curated = true, Group = "Library/Insurgent", ParamsType = typeof(RamTargetBlackboard))]
        public static BTreeBuilder<RamTargetBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<RamTargetBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref RamTargetParams p, ref RamTargetState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self) || !world.IsComponentTypeRegistered<RamOrder>()) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            var target = p.Target != 0 ? new Fdp.Toolkit.Replication.EntityRef(p.Target).Resolve(world) : Entity.Null;
            bool crowd = p.Target == 0;
            if (!crowd && (target.IsNull || !world.IsAlive(target) || !world.HasComponent<SimTransform>(target))) return Done(self, world, NodeStatus.Success);
            if (!crowd && world.IsComponentTypeRegistered<Health>() && world.HasComponent<Health>(target) && world.GetComponentRO<Health>(target).Current <= 0f)
                return Done(self, world, NodeStatus.Success);   // the target is down

            if (s.Started == 0)
            {
                var order = new RamOrder { Active = 1, Crowd = crowd ? (byte)1 : (byte)0, Target = new Fdp.Toolkit.Replication.EntityRef(p.Target) };
                if (world.HasComponent<RamOrder>(self)) world.GetComponentRW<RamOrder>(self) = order;
                else world.AddComponent(self, order);   // added WITH its final value (the parallel rule)
                s.Started = 1;
            }

            Vector3 aim;
            if (crowd)
            {
                aim = p.At;
                if (Vector2.Distance(new Vector2(here.X, here.Y), new Vector2(aim.X, aim.Y)) <= CrowdReachedM) return Done(self, world, NodeStatus.Success);
            }
            else
            {
                var at = world.GetComponentRO<SimTransform>(target).Position;
                var tv = world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(target) ? world.GetComponentRO<SimVelocity>(target).Linear : Vector3.Zero;
                var sv = world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(self) ? world.GetComponentRO<SimVelocity>(self).Linear : Vector3.Zero;
                aim = TargetLead.Point(here, Vector3.Zero, System.MathF.Max(1f, System.MathF.Max(p.Speed, sv.Length())), at, tv);
            }
            // RM-3 (KV-5, R-2134) — inside the run-in the CAR commits the Lethal act (soldiers shoot the car), its driver too; after the phase (marking
            //   witnesses touches others)
            Vector3 goal = crowd ? p.At : (Vector3)world.GetComponentRO<SimTransform>(target).Position;
            if (s.Committed == 0 && Vector2.Distance(new Vector2(here.X, here.Y), new Vector2(goal.X, goal.Y)) <= CommitMetres)
            {
                Acts.Commit(world, self);
                var driver = DriverOf(world, self);
                if (!driver.IsNull) Acts.Commit(world, driver);
                s.Committed = 1;
            }
            if (now >= (double)s.NextIssue)
            {
                if (!LocomotionMoveTo.Issue(world, self, aim, p.Speed, 0.5f, MoveGait.Keep, source: LocomotionMoveTo.OrderSource(world, self))) return Done(self, world, NodeStatus.Failure);
                s.NextIssue = (SimInstant)(now + ReissueSeconds);
            }
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.RamTargetBehavior.Tick")]
        public static void Deactivate(ref RamTargetParams p, ref RamTargetState s, Entity self, EntityRepository world)
        {
            Off(self, world);
            s.Started = 0;
            s.Committed = 0;
        }

        private static NodeStatus Done(Entity self, EntityRepository world, NodeStatus status)
        {
            Off(self, world);
            return status;
        }

        /// <summary>The order OFF on the hull (never removed: the off replicates as a change).</summary>
        private static void Off(Entity self, EntityRepository world)
        {
            if (world.IsComponentTypeRegistered<RamOrder>() && world.HasComponent<RamOrder>(self) && world.GetComponentRO<RamOrder>(self).Active != 0)
                world.GetComponentRW<RamOrder>(self).Active = 0;
        }

        /// <summary>The man in the vehicle's first seat (its driver, by the seat order); none when empty.</summary>
        internal static Entity DriverOf(EntityRepository world, Entity vehicle)
        {
            if (!world.IsComponentTypeRegistered<PassengerBuffer>() || !world.HasComponent<PassengerBuffer>(vehicle)) return Entity.Null;
            ref readonly var buf = ref world.GetComponentRO<PassengerBuffer>(vehicle);
            return buf.Count > 0 ? buf.Passengers[0] : Entity.Null;
        }

        /// <summary>RM-3 — a LETHAL hostile act, after the brains' phase when inside it (<c>HostileActs.Commit</c> marks the witnesses: other entities).</summary>
        internal static class Acts
        {
            public static void Commit(EntityRepository world, Entity actor)
            {
                if (ParallelReadCheck.Defers(world)) { ParallelReadCheck.CurrentBatch!.Later(LethalAfterPhase.Instance, actor, 0, 0L); return; }
                HostileActs.Commit(world, actor, HostileActKind.Lethal);
            }

            private sealed class LethalAfterPhase : Fdp.Core.Jobs.IPhaseFollowUp
            {
                public static readonly LethalAfterPhase Instance = new();
                public void Apply(EntityRepository repo, Entity e, int code, long arg) => HostileActs.Commit(repo, e, HostileActKind.Lethal);
            }
        }
    }
}
