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

        /// <summary>⭐ <c>CE-2347</c> — a man dead or down does not keep his squad "aboard": he cannot get out, so a dismount waiting for him
        /// never ends and the rows that start one fire again. 📌 Live on <c>uc-urban-battle</c>: Squad B, its dead still seated in the wrecked
        /// IFV, started a dismount every ~9 s for 25 minutes. A unit member (a squad under a platoon) is judged by its own men.</summary>
        public static bool OutOfTheFight(EntityRepository world, Entity member)
            => !SquadManeuverRunner.IsUnit(world, member) && !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, member);

        /// <summary>True when <paramref name="member"/> is aboard a vehicle — for a member that is a UNIT, when any of ITS members is
        /// (a platoon's squad rides as soldiers).</summary>
        public static bool Aboard(EntityRepository world, Entity member, int depth = 0)
        {
            if (!world.IsAlive(member) || OutOfTheFight(world, member)) return false;
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

        /// <summary>⭐ <c>CE-2344</c> (R-2066 E) — no round at the squad for this long (s) is a LULL: the squad brings its wounded in itself.</summary>
        public const double LullSeconds = 10d;

        /// <summary>⭐ <c>CE-2344</c> (R-2066 E) — no near miss or hit on the squad for <see cref="LullSeconds"/> — ⭐ <c>CE-2351</c> and no NEW
        /// contact in that time: the quiet is counted from the fight's start, not from before it. 📐 Live <c>ua-casualty-care-heavy</c>: the
        /// reaction chose 4 s after the contact, before the first enemy round (5.5 s), so "no round for 10 s" held and it went for the man.</summary>
        public static bool InALull(EntityRepository world, Entity self)
            => !Fdp.Toolkit.Behavior.Components.RecentSensesOf.Within(world, self, Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire, LullSeconds)
               && !Fdp.Toolkit.Behavior.Components.RecentSensesOf.Within(world, self, Fdp.Toolkit.Perception.Events.SensorChange.SquadContact, LullSeconds);

        /// <summary>
        /// ⭐ <c>CE-2344</c> (R-2066 E; Squad Tactics §7.5.4) — the lull row: a man down still to care for (<see cref="Fdp.Toolkit.Squad.SquadCasualties"/>)
        /// and <see cref="InALull"/> ⇒ the squad drags him to cover itself (<c>CasualtyCare</c>). Under fire the row above reacts and the
        /// care waits. 📌 Live on <c>pc-defend-house</c>: four men down by 60 s, one carried in six minutes.
        /// </summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool CasualtyInALull(Entity self, EntityRepository world)
            => Fdp.Toolkit.Squad.SquadCasualties.Count(world, self) > 0 && InALull(world, self);

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

        /// <summary>⭐ <c>CE-4032</c> — how near an enemy must be for a squad in a halted carrier to get out (m).</summary>
        public const float HaltedContactMetres = 300f;

        /// <summary>⭐ <c>CE-2347</c> — how recently the enemy must have been perceived (ticks, 10 s at 60 Hz; the clock perception stamps
        /// <c>LastSeenTick</c> with): "an enemy near NOW". 📌 Live on <c>uc-urban-battle</c> a squad remounting after 30 s of quiet was put out
        /// again by contacts minutes old (heard ones, with no entity id, placed at the convoy's own guns and the mortar bursts).</summary>
        public const uint HaltedContactFreshTicks = 600;

        private static bool Fresh(uint lastSeen, uint now) => lastSeen > now || now - lastSeen <= HaltedContactFreshTicks;

        /// <summary>
        /// ⭐⭐ <c>CE-4032</c> — the squad sits in a carrier that has STOPPED on the ground (standing, not pushing through, not the stop that
        /// picks it up — the dismount row's own test) and an enemy is known within <see cref="HaltedContactMetres"/> of it — by the squad,
        /// or by the carrier's crew (a cabin hides its passengers' eyes). ⇒ the squad gets out and fights. 📌 Live on
        /// <c>uc-urban-battle</c>: Squad A sat in a wedged APC for ten minutes while the ambush fought on 50 m away — never shot at, so the
        /// under-fire row never fired, and the riding row above the contact rows kept it aboard. A carrier that DRIVES carries it on.
        /// </summary>
        [Fbt.Kernel.SharedAiCondition]
        public static unsafe bool ContactWhileHalted(Entity self, EntityRepository world)
        {
            var carrier = HaltedCarrier(world, self, 0);
            if (carrier.IsNull || !world.HasComponent<SimTransform>(carrier)) return false;
            var at = world.GetComponentRO<SimTransform>(carrier).Position;
            float r2 = HaltedContactMetres * HaltedContactMetres;
            if (world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()
                && world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(carrier))
            {
                ref readonly var mem = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(carrier);
                for (int i = 0; i < mem.Count && i < 32; i++)
                {
                    var e = new Entity((ulong)mem.EntityIds[i]);
                    if (mem.EntityIds[i] <= 0 || !world.IsAlive(e) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, e) || !KillZone.Hostile(world, carrier, e)) continue;
                    if (!Fresh(mem.LastSeenTick[i], world.SimulationTick)) continue;
                    if (new Vector2(mem.PositionsX[i] - at.X, mem.PositionsY[i] - at.Y).LengthSquared() <= r2) return true;
                }
            }
            if (!world.HasComponent<SquadCognitiveState>(self)) return false;
            ref readonly var squad = ref world.GetComponentRO<SquadCognitiveState>(self);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int i = 0; i < squad.Contacts.Count && i < 16; i++)
            {
                if (pool[i].EntityId > 0 && !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, new Entity((ulong)pool[i].EntityId))) continue;
                if (!Fresh(pool[i].LastSeenTick, world.SimulationTick)) continue;   // ⭐ CE-2347 — an old contact is no enemy near now
                if (new Vector2(pool[i].PositionX - at.X, pool[i].PositionY - at.Y).LengthSquared() <= r2) return true;
            }
            return false;
        }

        /// <summary>The carrier a member of <paramref name="commander"/> is aboard, when it stands on the ground (<see cref="AboardWhere.OnTheGround"/>).</summary>
        private static Entity HaltedCarrier(EntityRepository world, Entity commander, int depth)
        {
            if (!world.HasComponent<UnitRoster>(commander)) return Entity.Null;
            var roster = world.GetComponentRO<UnitRoster>(commander);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!world.IsAlive(m)) continue;
                var carrier = AirHolds.CarrierOf(world, m);
                if (!carrier.IsNull && AboardWhereIs(world, m, AboardWhere.OnTheGround, 0)) return carrier;
                if (carrier.IsNull && depth < 4 && SquadManeuverRunner.IsUnit(world, m))
                {
                    var inner = HaltedCarrier(world, m, depth + 1);
                    if (!inner.IsNull) return inner;
                }
            }
            return Entity.Null;
        }

        private enum AboardWhere : byte { OnTheGround, Unloading, Roping, Dropping }

        private static readonly int PushThroughHash = BehaviorHash.FromName(BehaviorNames.PushThrough);

        private static bool PushingThrough(EntityRepository world, Entity carrier)
            => world.HasComponent<BehaviorState>(carrier) && world.GetComponentRO<BehaviorState>(carrier).ActiveBehaviorHash == PushThroughHash;

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
            if (!world.IsAlive(member) || SquadDismountBehavior.OutOfTheFight(world, member)) return false;
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
                    //   ⭐ CE-2313 — and NOT pushing through: the carrier slows near a stop as PushThrough issues its move (measured live,
                    //   uc-convoy-hit-ifv: 0.6 m/s), and the squad got out in that dip; a carrier driving on has decided to drive on
                    //   ⭐ CE-2347 — and a carrier that CANNOT move stands, whatever its velocity says: a destroyed APC's replicated velocity
                    //   froze at its last sample (0.33 m/s for 140 s, measured live on uc-urban-battle) and its squad sat in the wreck
                    AboardWhere.OnTheGround => phase != Fdp.Toolkit.Navigation.NavigationPhase.Loading && AirHolds.OnTheGround(world, carrier)
                                               && (Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsStanding(world, carrier) || !ConvoyConditions.CanMove(world, carrier))
                                               && !PushingThrough(world, carrier),
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
