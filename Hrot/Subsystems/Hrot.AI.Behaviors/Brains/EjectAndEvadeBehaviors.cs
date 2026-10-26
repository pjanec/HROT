using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The params of <see cref="AirborneIndividualNodes.Eject"/>: the interaction it started (runtime only).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EjectNodeParams
    {
        [System.Text.Json.Serialization.JsonIgnore] public uint Started;
    }

    /// <summary>The params of <see cref="AirborneIndividualNodes.GetAway"/>: how far from the nearest known enemy, and how fast.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GetAwayParams
    {
        public float AwayMetres;
        public float Speed;
        /// <summary>Runtime: 1 once the move is on the locomotion channel; where it goes.</summary>
        [System.Text.Json.Serialization.JsonIgnore] public byte Moving;
        [System.Text.Json.Serialization.JsonIgnore] public Vector3 To;
    }

    /// <summary>
    /// ⭐ CE-1118 H-3 (📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.5 D-24, D-25) — the individual airborne nodes: an aircraft's
    /// ejection (the interaction action <see cref="BehaviorConstants.ActionIdEject"/>), and a downed man's wait for the ground and his
    /// move away from the enemy.
    /// </summary>
    public static class AirborneIndividualNodes
    {
        /// <summary>
        /// ⭐ D-24 — starts the aircraft's <c>Eject</c> action (<c>EjectExecutor</c> raises the ejection) and forwards its answer once:
        /// Success when the aircraft is abandoned (the pilot spawned first, B-8), Failure when no ejection came.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Eject(ref EjectNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Started != 0 && EmbarkNodes.OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdEject))
            {
                ref var running = ref world.GetComponentRW<InteractionChannel>(self);
                if (running.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = running.Status;
                EmbarkNodes.Release(world, self);
                p.Started = 0;
                return done;
            }
            p.Started = 0;
            if (!world.HasComponent<InteractionChannel>(self))
            {
                Fdp.Core.Logging.FdpLog<EmbarkNodesLog>.Warn($"Eject: the aircraft has no InteractionChannel. (entity {self.Index})");
                return NodeStatus.Failure;
            }
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            EmbarkNodes.Start(world, self, ref channel, BehaviorConstants.ActionIdEject);
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        /// <summary>⭐ D-25 — Running while the unit is coming down (its <c>Descent</c>, the harness time included) or aboard; Success
        /// once it stands on the ground.</summary>
        [SharedAiCondition]
        public static bool OnTheGround(Entity self, EntityRepository world)
            => AirHolds.CarrierOf(world, self).IsNull && !AirborneDrill.Descending(world, self);

        /// <summary>Running until <see cref="OnTheGround"/>.</summary>
        [SharedAiAction]
        public static NodeStatus WaitForTheGround(Entity self, EntityRepository world)
            => OnTheGround(self, world) ? NodeStatus.Success : NodeStatus.Running;

        /// <summary>The remembered position of the nearest live enemy (an anonymous heard spot counts); false when none is remembered.</summary>
        public static unsafe bool NearestKnownEnemy(EntityRepository world, Entity self, out Vector3 at)
        {
            at = default;
            if (!world.HasComponent<TargetMemory>(self) || !world.HasComponent<SimTransform>(self)) return false;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            float best = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (!TargetMemory.IsAnonymous(in mem, i) && !Injury.IsInTheFight(world, new Entity((ulong)mem.EntityIds[i]))) continue;
                var p = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                float d = Vector2.DistanceSquared(new Vector2(p.X, p.Y), new Vector2(me.X, me.Y));
                if (d < best) { best = d; at = p; }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// ⭐ D-25 — GET AWAY: from where he stands, straight away from the nearest remembered enemy until he is
        /// <see cref="GetAwayParams.AwayMetres"/> from it (the plain move). Success at once when he knows no enemy or is already that far;
        /// Success on arrival; Failure when he cannot move.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus GetAway(ref GetAwayParams p, Entity self, EntityRepository world)
        {
            if (p.Moving != 0)
            {
                var s = LocomotionMoveTo.Status(world, self);
                if (s == NodeStatus.Running) return NodeStatus.Running;
                p.Moving = 0;
                return NodeStatus.Success;   // arrived, or as far as the ground let him: the cover step takes over from here
            }
            if (!world.HasComponent<SimTransform>(self) || !NearestKnownEnemy(world, self, out var enemy)) return NodeStatus.Success;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            var away = new Vector2(me.X - enemy.X, me.Y - enemy.Y);
            float dist = away.Length();
            float want = p.AwayMetres > 0f ? p.AwayMetres : EvadeAndSignalBehavior.DefaultAwayMetres;
            if (dist >= want) return NodeStatus.Success;
            away = dist > 0.1f ? away / dist : Vector2.UnitX;
            var to2 = new Vector2(enemy.X, enemy.Y) + away * want;
            var to = new Vector3(to2.X, to2.Y, me.Z);
            if (!LocomotionMoveTo.Issue(world, self, to, p.Speed > 0f ? p.Speed : EvadeAndSignalBehavior.DefaultSpeed, 2f, MoveGait.Crouched, source: MoveSource.Flee))
                return NodeStatus.Failure;
            p.Moving = 1; p.To = to;
            return NodeStatus.Running;
        }
    }

    /// <summary>
    /// ⭐ CE-1118 H-3 (D-24) — <c>Eject</c>, a library behaviour for an AIRCRAFT (a mission task: after a <c>TimerElapsed</c> trigger it
    /// plays the malfunction): one node, <see cref="AirborneIndividualNodes.Eject"/>. ⛔ It spawns nothing — <c>EjectionSystem</c> does.
    /// </summary>
    public static class EjectBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct EjectBlackboard
        {
            public EjectNodeParams Eject;
        }

        [BehaviorResolver(BehaviorNames.Eject)]
        public static void Resolve(in NoParams authored, ref EjectBlackboard block, EntityRepository world, Entity self) => block.Eject = default;

        [Behavior(BehaviorNames.Eject, Params = typeof(NoParams), Categories = BehaviorCategory.MilitaryApc)]   // the aircraft behaviours' category (AirPlatformStrike)
        [BTreeDefinition(BehaviorNames.Eject, Curated = true, Group = "Library/Air", ParamsType = typeof(EjectBlackboard))]
        public static BTreeBuilder<EjectBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<EjectBlackboard, BTreeContext>()
                .Action(bb => bb.Eject, AirborneIndividualNodes.Eject);
    }

    /// <summary>
    /// ⭐ CE-1118 H-3 (D-25, 📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.5) — <c>EvadeAndSignal</c>, the downed pilot: waits for
    /// the ground (his descent and harness time), gets away from the nearest known enemy (<see cref="AirborneIndividualNodes.GetAway"/>), then
    /// into a point hidden from every known threat (the existing <see cref="EqsTacticsNodes.FallBack"/>) and lies there (the existing
    /// <see cref="PostureNodes.HoldProne"/>, firing only at what he sees) — Running for ever. The beacon and the rescue stay Air Ops §4c / §9.
    /// </summary>
    public static class EvadeAndSignalBehavior
    {
        public const float DefaultAwayMetres = 120f;
        public const float DefaultSpeed = 3f;

        [StructLayout(LayoutKind.Sequential)]
        public struct EvadeAndSignalBlackboard
        {
            public GetAwayParams Away;
            public EqsTacticsParams Cover;
            public EqsTacticsState CoverState;
            public EngageParams Hold;
            public EngageState HoldState;
        }

        [BehaviorResolver(BehaviorNames.EvadeAndSignal)]
        public static void Resolve(in EvadeAndSignalParamsJsonDto authored, ref EvadeAndSignalBlackboard block, EntityRepository world, Entity self)
        {
            float speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed;
            block.Away = new GetAwayParams { AwayMetres = authored.AwayMetres > 0f ? authored.AwayMetres : DefaultAwayMetres, Speed = speed };
            block.Cover = new EqsTacticsParams { SearchRadius = 40f, MinRepositionMetres = 5f, Speed = speed, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f };
            block.CoverState = default;
            block.Hold = new EngageParams { CooldownSeconds = 1f };
            block.HoldState = default;
        }

        [Behavior(BehaviorNames.EvadeAndSignal, Params = typeof(EvadeAndSignalParamsJsonDto), Categories = BehaviorCategory.Infantry)]
        [BTreeDefinition(BehaviorNames.EvadeAndSignal, Curated = true, Group = "Library/Basic", ParamsType = typeof(EvadeAndSignalBlackboard))]
        public static BTreeBuilder<EvadeAndSignalBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<EvadeAndSignalBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(AirborneIndividualNodes.WaitForTheGround)
                    .Action(bb => bb.Away, AirborneIndividualNodes.GetAway)
                    .StatefulAction(bb => bb.Cover, bb => bb.CoverState, EqsTacticsNodes.FallBack)
                    .StatefulAction(bb => bb.Hold, bb => bb.HoldState, PostureNodes.HoldProne));
    }
}
