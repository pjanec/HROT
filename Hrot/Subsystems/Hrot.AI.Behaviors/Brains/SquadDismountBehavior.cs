using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-1056</c> I-2d — a squad dismount (resolved from <see cref="SquadDismountParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadDismountParams
    {
        /// <summary>Failure when somebody is still aboard after this long (s).</summary>
        public float TimeoutSeconds;
        /// <summary>The hold speed of a member already out.</summary>
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — <c>SquadDismount</c> on the commander: every member aboard a vehicle gets the library <c>Disembark</c> (a
    /// unit member gets <c>SquadDismount</c>), a member already out holds; Success the moment nobody is aboard — the squad's SOP then
    /// fights. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5a.
    /// </summary>
    public static class SquadDismountBehavior
    {
        /// <summary>The default <see cref="SquadDismountParams.TimeoutSeconds"/> (s).</summary>
        public const float DefaultTimeoutSeconds = 10f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SquadDismountBlackboard
        {
            public SquadDismountParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadDismount)]
        public static void Resolve(in SquadDismountParamsJsonDto authored, ref SquadDismountBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadDismountParams
            {
                TimeoutSeconds = authored.TimeoutSeconds > 0f ? authored.TimeoutSeconds : DefaultTimeoutSeconds,
                Speed = authored.Speed > 0f ? authored.Speed : 3f,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SquadDismount, Params = typeof(SquadDismountParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134 (moved off the DTO at the merge)
        [BTreeDefinition(BehaviorNames.SquadDismount, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadDismountBlackboard))]
        public static BTreeBuilder<SquadDismountBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadDismountBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadDismountParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadDismountPlay, SquadDismountParams>(ref p, ref ws, self, world);

        /// <summary>True when <paramref name="member"/> is aboard a vehicle — for a member that is a UNIT, when any of ITS members is
        /// (a platoon's squad rides as soldiers).</summary>
        public static bool Aboard(EntityRepository world, Entity member, int depth = 0)
        {
            if (!world.IsAlive(member)) return false;
            if (world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(member)) return true;
            if (depth >= 4 || !SquadManeuverRunner.IsUnit(world, member) || !world.HasComponent<UnitRoster>(member)) return false;
            var roster = world.GetComponentRO<UnitRoster>(member);
            for (int i = 0; i < roster.Count; i++)
                if (Aboard(world, roster.SubordinateEntities[i], depth + 1)) return true;
            return false;
        }

        /// <summary>True when any live member of <paramref name="commander"/>'s roster is <see cref="Aboard"/>.</summary>
        public static bool AnyMemberAboard(EntityRepository world, Entity commander)
        {
            if (!world.HasComponent<UnitRoster>(commander)) return false;
            var roster = world.GetComponentRO<UnitRoster>(commander);
            for (int i = 0; i < roster.Count; i++)
                if (Aboard(world, roster.SubordinateEntities[i])) return true;
            return false;
        }
    }

    /// <summary>⭐ <c>CE-1056</c> I-2d — the dismount as a play over <see cref="SquadDismountManeuver"/>.</summary>
    public struct SquadDismountPlay : ISquadPlay<SquadDismountParams>
    {
        private static readonly SquadHsmShell s_shell = new(SquadDismountManeuver.BuildTransitionTable(), SquadDismountManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadDismountManeuver.ManeuverKind;
        public ushort StartPhase => SquadDismountManeuver.PhaseOut;
        public ushort AbortPhase => SquadDismountManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadDismountParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref SquadDismountParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public NodeStatus Outcome(ref SquadDismountParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            if (!SquadDismountBehavior.AnyMemberAboard(ctx.World, ctx.Commander)) return NodeStatus.Success;
            if (p.TimeoutSeconds > 0f && ctx.Now - ws.StartedAt >= p.TimeoutSeconds) return NodeStatus.Failure;
            return NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadDismountParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref SquadDismountParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadDismountParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ctx.Live(member, out var m);
            return SquadDismountBehavior.Aboard(ctx.World, m)
                ? new MemberOrder { Kind = MemberOrderKind.Disembark, RoleId = SquadDismountManeuver.RoleDismounting }
                : new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadDismountManeuver.RoleHolding };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }
}

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — the squad SOP's condition for the dismount row (📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md
    /// §5a): <see cref="SopConditions.SensedFresh"/> AND a member is aboard — "under fire while mounted". One shared condition (BTree,
    /// HSM, blueprint), so the row reuses the SOP's existing sense variable.
    /// </summary>
    public static class SquadSopConditions
    {
        [Fbt.Kernel.SharedAiCondition]
        public static bool SensedFreshAboard(ref SopSenseParams p, Entity self, EntityRepository world)
            => SopConditions.SensedFresh(ref p, self, world) && SquadDismountBehavior.AnyMemberAboard(world, self);
    }
}
