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
        /// <remarks>⭐ <c>CE-2173</c> slice 3 (Air Operations §9b) — only a member aboard a carrier that is ON THE GROUND counts
        /// (<see cref="AirHolds.OnTheGround"/>) and is not picking the squad up (<c>Loading</c>): a squad shot at in a flying helicopter, or while
        /// boarding its way out, rides on (📐 before it the row dismounted it in the
        /// air — <c>Embarkation.Disembark</c> has no airborne gate).</remarks>
        [Fbt.Kernel.SharedAiCondition]
        public static bool SensedFreshAboard(ref SopSenseParams p, Entity self, EntityRepository world)
            => SopConditions.SensedFresh(ref p, self, world) && AnyMemberAboard(world, self, AboardWhere.OnTheGround);

        /// <summary>
        /// ⭐ <c>CE-2173</c> slice 3 (B8, Air Operations §9b) — the squad's carrier is UNLOADING it: a member is aboard a vehicle whose route
        /// holds it at an <c>Unload</c> stop (<see cref="Fdp.Toolkit.Navigation.NavigationPhase.Unloading"/>). ⭐ <c>Unloading</c>, not "landed",
        /// so a squad that has just boarded at a <c>Pickup</c> stop is not put out again.
        /// </summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool CarrierUnloading(Entity self, EntityRepository world) => AnyMemberAboard(world, self, AboardWhere.Unloading);

        /// <summary>⭐ CE-1118 H-1 (Airborne Insertion §6) — the squad's helicopter holds at a <c>Rope</c> stop
        /// (<see cref="Fdp.Toolkit.Navigation.NavigationPhase.Roping"/>) with a member aboard: the fast-rope row.</summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool CarrierRoping(Entity self, EntityRepository world) => AnyMemberAboard(world, self, AboardWhere.Roping);

        /// <summary>⭐ CE-1118 H-1 (§6) — the squad's aircraft flies a Drop leg (<see cref="Fdp.Toolkit.Navigation.NavigationPhase.Dropping"/>)
        /// with a member aboard: the jumpmaster's row.</summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool CarrierDropping(Entity self, EntityRepository world) => AnyMemberAboard(world, self, AboardWhere.Dropping);

        /// <summary>⭐ <c>CE-2173</c> slice 3 (B8) — the squad RIDES: it has a live member and every live one is aboard a vehicle (a unit
        /// member: all of its own). ⭐ ALL, not any — while the squad boards, the first man in must not pause the boarding.</summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool Riding(Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return false;
            var roster = world.GetComponentRO<UnitRoster>(self);
            bool any = false;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!world.IsAlive(m)) continue;
                if (!AllRiding(world, m, 0)) return false;
                any = true;
            }
            return any;
        }

        private static bool AllRiding(EntityRepository world, Entity member, int depth)
        {
            if (!SquadManeuverRunner.IsUnit(world, member)) return !AirHolds.CarrierOf(world, member).IsNull;
            if (depth >= 4 || !world.HasComponent<UnitRoster>(member)) return true;
            var roster = world.GetComponentRO<UnitRoster>(member);
            for (int i = 0; i < roster.Count; i++)
                if (world.IsAlive(roster.SubordinateEntities[i]) && !AllRiding(world, roster.SubordinateEntities[i], depth + 1)) return false;
            return true;
        }

        private enum AboardWhere : byte { OnTheGround, Unloading, Roping, Dropping }

        private static bool AnyMemberAboard(EntityRepository world, Entity commander, AboardWhere where)
        {
            if (!world.HasComponent<UnitRoster>(commander)) return false;
            var roster = world.GetComponentRO<UnitRoster>(commander);
            for (int i = 0; i < roster.Count; i++)
                if (AboardWhereIs(world, roster.SubordinateEntities[i], where, 0)) return true;
            return false;
        }

        private static bool AboardWhereIs(EntityRepository world, Entity member, AboardWhere where, int depth)
        {
            if (!world.IsAlive(member)) return false;
            var carrier = AirHolds.CarrierOf(world, member);
            if (!carrier.IsNull)
            {
                var phase = world.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(carrier)
                    ? world.GetComponentRO<Fdp.Toolkit.Navigation.NavigationStatus>(carrier).Phase
                    : Fdp.Toolkit.Navigation.NavigationPhase.Idle;
                // OnTheGround: standing, and not the stop that is picking the squad UP — under fire there, the aircraft is the way out
                return where switch
                {
                    // ⭐ CE-2307 (CH-3, §5b) — and STANDING: a carrier that drives on (PushThrough) carries its squad out of the kill zone
                    AboardWhere.OnTheGround => phase != Fdp.Toolkit.Navigation.NavigationPhase.Loading && AirHolds.OnTheGround(world, carrier)
                                               && Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsStanding(world, carrier),
                    AboardWhere.Roping      => phase == Fdp.Toolkit.Navigation.NavigationPhase.Roping,     // ⭐ CE-1118 H-1
                    AboardWhere.Dropping    => phase == Fdp.Toolkit.Navigation.NavigationPhase.Dropping,   // ⭐ CE-1118 H-1
                    _                       => phase == Fdp.Toolkit.Navigation.NavigationPhase.Unloading,
                };
            }
            if (depth >= 4 || !SquadManeuverRunner.IsUnit(world, member) || !world.HasComponent<UnitRoster>(member)) return false;
            var roster = world.GetComponentRO<UnitRoster>(member);
            for (int i = 0; i < roster.Count; i++)
                if (AboardWhereIs(world, roster.SubordinateEntities[i], where, depth + 1)) return true;
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-2159</c> X-c (Squad Tactics §7.7.6) — a danger area is AHEAD: the commander's danger-area sensor (the march's) holds
        /// one the route reaches within <see cref="SopAreaParams.WithinMetres"/>, and the squad's centre is not yet past its far side.
        /// ⭐ State, not the <c>AreaAhead</c> edge: the edge fires once, when the area first shows on the route — often 100 m+ out —
        /// and the far-side test keeps a buffer not yet refreshed after the crossing from starting it again.
        /// </summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool DangerAreaAhead(ref SopAreaParams p, Entity self, EntityRepository world)
        {
            if (!DangerCrossingPlay.NextArea(world, self, out var area)) return false;
            if (area.DistanceAlongRoute > (p.WithinMetres > 0f ? p.WithinMetres : 30f)) return false;
            var line = area.FarSideHandle - area.NearSideHandle; line.Z = 0f;
            if (line.LengthSquared() < 1f) return false;
            if (!world.HasComponent<SimTransform>(self)) return true;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            return System.Numerics.Vector3.Dot(at - area.FarSideHandle, System.Numerics.Vector3.Normalize(line)) < 0f;
        }
    }
}
