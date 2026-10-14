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
    /// <summary>⭐ <c>CE-2173</c> slice 3 (B10) — a squad boarding (resolved from <see cref="SquadEmbarkParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadEmbarkParams
    {
        /// <summary>The vehicle to board.</summary>
        public Entity Vehicle;
        /// <summary>Failure when somebody is still out after this long (s).</summary>
        public float TimeoutSeconds;
        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the Embark node's default.</summary>
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 (B10, R-2015) — <c>SquadEmbark</c> on the commander: every live member boards one vehicle (the library
    /// <c>Embark</c>; a soldier carrying a casualty gets <c>LoadCasualty</c> and seats him first; a unit member gets <c>SquadEmbark</c>);
    /// Success the moment every live member — and every man they carried — is aboard it. The mirror of <see cref="SquadDismountBehavior"/>.
    /// 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9.
    /// </summary>
    public static class SquadEmbarkBehavior
    {
        /// <summary>The default <see cref="SquadEmbarkParams.TimeoutSeconds"/> (s): a squad walks to its helicopter from the landing zone's edge.</summary>
        public const float DefaultTimeoutSeconds = 60f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SquadEmbarkBlackboard
        {
            public SquadEmbarkParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadEmbark)]
        public static void Resolve(in SquadEmbarkParamsJsonDto authored, ref SquadEmbarkBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadEmbarkParams
            {
                Vehicle = authored.Vehicle.Resolve(world),
                TimeoutSeconds = authored.TimeoutSeconds > 0f ? authored.TimeoutSeconds : DefaultTimeoutSeconds,
                Speed = authored.Speed,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SquadEmbark, Params = typeof(SquadEmbarkParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SquadEmbark, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadEmbarkBlackboard))]
        public static BTreeBuilder<SquadEmbarkBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadEmbarkBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadEmbarkParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadEmbarkPlay, SquadEmbarkParams>(ref p, ref ws, self, world);

        /// <summary>True when <paramref name="member"/> is aboard <paramref name="vehicle"/> and carries nobody — for a member that is a
        /// UNIT, when every live one of ITS members is (a platoon's squad rides as soldiers).</summary>
        public static bool AboardIt(EntityRepository world, Entity member, Entity vehicle, int depth = 0)
        {
            if (!world.IsAlive(member)) return true;   // the dead do not hold the squad back
            if (SquadManeuverRunner.IsUnit(world, member))
            {
                if (depth >= 4 || !world.HasComponent<UnitRoster>(member)) return true;
                var roster = world.GetComponentRO<UnitRoster>(member);
                for (int i = 0; i < roster.Count; i++)
                    if (!AboardIt(world, roster.SubordinateEntities[i], vehicle, depth + 1)) return false;
                return true;
            }
            if (!Embarkation.LoadOf(world, member).IsNull) return false;   // still carrying a man: he is seated first
            return world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(member)
                && world.GetComponentRO<IsEmbarkedTag>(member).VehicleEntity == vehicle;
        }

        /// <summary>True when every member of <paramref name="commander"/>'s roster is <see cref="AboardIt"/>.</summary>
        public static bool AllAboard(EntityRepository world, Entity commander, Entity vehicle)
        {
            if (!world.HasComponent<UnitRoster>(commander)) return false;
            var roster = world.GetComponentRO<UnitRoster>(commander);
            for (int i = 0; i < roster.Count; i++)
                if (!AboardIt(world, roster.SubordinateEntities[i], vehicle)) return false;
            return true;
        }
    }

    /// <summary>⭐ <c>CE-2173</c> slice 3 — the boarding as a play over <see cref="SquadEmbarkManeuver"/>.</summary>
    public struct SquadEmbarkPlay : ISquadPlay<SquadEmbarkParams>
    {
        private static readonly SquadHsmShell s_shell = new(SquadEmbarkManeuver.BuildTransitionTable(), SquadEmbarkManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadEmbarkManeuver.ManeuverKind;
        public ushort StartPhase => SquadEmbarkManeuver.PhaseIn;
        public ushort AbortPhase => SquadEmbarkManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadEmbarkParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            if (!ctx.World.IsAlive(p.Vehicle) || !ctx.World.HasComponent<PassengerBuffer>(p.Vehicle)) return PlayStart.Fail;
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref SquadEmbarkParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public NodeStatus Outcome(ref SquadEmbarkParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!ctx.World.IsAlive(p.Vehicle) || !SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            if (SquadEmbarkBehavior.AllAboard(ctx.World, ctx.Commander, p.Vehicle)) return NodeStatus.Success;
            if (p.TimeoutSeconds > 0f && ctx.Now - ws.StartedAt >= p.TimeoutSeconds) return NodeStatus.Failure;
            return NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadEmbarkParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref SquadEmbarkParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadEmbarkParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ctx.Live(member, out var m);
            bool loading = !SquadManeuverRunner.IsUnit(ctx.World, m) && !Embarkation.LoadOf(ctx.World, m).IsNull;
            return new MemberOrder
            {
                Kind = MemberOrderKind.Embark, Target = p.Vehicle, Speed = p.Speed,
                RoleId = loading ? SquadEmbarkManeuver.RoleLoading : SquadEmbarkManeuver.RoleBoarding,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }
}
