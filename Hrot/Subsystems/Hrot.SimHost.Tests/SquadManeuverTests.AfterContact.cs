using Fdp.Core.CommandHierarchy;
using Fdp.Core;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Modules.Geographic;
using Fdp.Testing;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System;
using Xunit;
namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2466</c> (R-2140, 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.9) — after a contact with an enemy it did not destroy the
    /// squad REMEMBERS the ground each enemy covers (R1, the side's <c>UnderFire</c> knowledge — Minefields §9b, built by backend CE-2468), RALLIES and
    /// reports (R2), never assaults frontally a force it broke contact from without 3 : 1 (R4), and holds short of covered ground on its route (R6).
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static void Sides(Squad s, Entity[] enemies)
        {
            if (!s.Repo.IsComponentTypeRegistered<EntityInfo>()) s.Repo.RegisterComponent<EntityInfo>();
            s.Repo.AddComponent(s.Commander, new EntityInfo { ForceId = ForceId.Friend });
            foreach (var e in enemies) s.Repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            if (!s.Repo.Bus.IsRegistered<UnderFireLearned>()) s.Repo.RegisterEvent<UnderFireLearned>();
            if (!s.Repo.IsComponentTypeRegistered<ReportOutbox>()) s.Repo.RegisterComponent<ReportOutbox>();
            if (!s.Repo.HasComponent<ReportOutbox>(s.Commander)) s.Repo.AddComponent(s.Commander, new ReportOutbox());
        }

        /// <summary>A fresh <c>Direct</c> record of the Friend side at <paramref name="point"/> — what backend's writer leaves after a learn.</summary>
        private static void Remember(Squad s, Vector3 point, float enemyStrength)
        {
            if (!s.Repo.IsComponentTypeRegistered<UnderFireKnowledge>()) s.Repo.RegisterComponent<UnderFireKnowledge>();
            double now = s.Repo.HasSingleton<GlobalTime>() ? s.Repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            var e = s.Repo.CreateEntity();
            s.Repo.AddComponent(e, new UnderFireKnowledge
            {
                Header = new PlaceKnowledgeHeader
                {
                    SubjectKind = SubjectKind.Place, SidePlus1 = (byte)((int)ForceId.Friend + 1), Kind = PlaceKnowledgeKind.UnderFire,
                    Subject = Guid.NewGuid(), ObjectIndex = -1, LearnedAt = (SimInstant)now, State = UnderFireKnowledge.StateActive,
                },
                Cause = UnderFireCause.Direct, Point = point, RangeM = AfterContact.DirectRangeMetres, EnemyStrength = enemyStrength,
            });
        }

        private static int Queued(Squad s, ReportKind kind)
        {
            ref readonly var box = ref s.Repo.GetComponentRO<ReportOutbox>(s.Commander);
            int n = 0;
            for (int i = 0; i < box.Queued.Count; i++) if (box.Queued.Items[i].Report.Kind == kind) n++;
            return n;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2466</c> R1 + R2 — an outmatched squad breaks contact from two enemy positions: it LEARNS one <c>Direct</c> record per enemy position
        /// for its own side (two corners = two records, S5), then RALLIES (a hold) and sends LACE and a SPOT of each before the reaction ends.
        /// ✅ Red-proof: the learn removed from <c>Run</c> ⇒ no <c>UnderFireLearned</c> ⇒ red.
        /// </summary>
        [Fact]
        public void CE2466_R1_R2_ABrokenContact_IsRemembered_PerEnemyPosition_AndTheSquadRalliesAndReports()
        {
            var s = new Squad(members: 2);
            var west = s.PoolMany(3, new Vector3(-20f, 60f, 0f));
            var east = new[] { s.Repo.CreateEntity(), s.Repo.CreateEntity(), s.Repo.CreateEntity() };
            {
                ref var squad = ref s.Repo.GetComponentRW<SquadCognitiveState>(s.Commander);
                var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
                for (int i = 0; i < 3; i++)
                {
                    s.Repo.AddComponent(east[i], new SimTransform { Position = new Vector3(25f, 62f, 0f), Rotation = Quaternion.Identity });
                    span[3 + i] = new SquadContact { EntityId = (long)east[i].PackedValue, PositionX = 25f, PositionY = 62f, ThreatScore = 0.9f, SourceMembersMask = 1 };
                }
                squad.Contacts.Count = 6;
            }
            Sides(s, west); Sides(s, Array.Empty<Entity>());
            foreach (var e in east) s.Repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
            int learnedWest = 0, learnedEast = 0;
            void Collect()
            {
                foreach (var l in s.Repo.Bus.Read<UnderFireLearned>())
                {
                    Assert.Equal(UnderFireCause.Direct, l.Cause);
                    Assert.Equal((byte)((int)ForceId.Friend + 1), l.SidePlus1);
                    Assert.True(l.EnemyStrength > 0f, "the strength seen is remembered");
                    if (l.Point.X < 0f) learnedWest++; else learnedEast++;
                }
            }
            for (int i = 0; i < 12; i++) { s.Tick(); Collect(); }
            Assert.Equal(BreakContactManeuver.ManeuverKind, s.Kind);
            Assert.True(learnedWest > 0 && learnedEast > 0, $"both positions learned at the decision to break contact (west {learnedWest}, east {learnedEast})");

            var hidden = new Vector3(-40f, -30f, 0f);
            s.Answer(s.RetreatQuery(), hidden);
            s.Tick(); s.Tick();
            s.Answer(s.BreakCoverQuery(), null);
            for (int i = 0; i < 4; i++) s.Tick();
            foreach (var m in s.Members) s.Teleport(m, hidden);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 6 && !done; i++) { s.Tick(); Collect(); done = s.Finished(out result); }
            Assert.False(done, "an enemy still there: the squad rallies first");
            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);
            Assert.Equal(1, Queued(s, ReportKind.Lace));
            Assert.True(Queued(s, ReportKind.Spot) >= 2, "a SPOT of each remembered enemy");
            for (int i = 0; i < 60 * 40 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done, "the rally ends: the task resumes");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>
        /// ⭐ <c>CE-2496</c> (R-2145) — an enemy position covers at least as far as it ENGAGED the squad: the farthest live member + <see cref="AfterContact.RecordNearMetres"/>,
        /// never under <see cref="AfterContact.DirectRangeMetres"/>, never over <see cref="AfterContact.MaxCoveredMetres"/>. 📐 of-squad-outmatched: two machine guns
        /// engaged from ~200 m; the fixed 150 m let the resumed move walk back into their sight. 🔴 Red-proof: the fixed range back ⇒ red.
        /// </summary>
        [Fact]
        public void CE2496_AnEnemyCovers_AsFarAsItEngagedTheSquad()
        {
            var s = new Squad(members: 2);
            var enemy = new Vector3(0f, 200f, 0f);
            foreach (var m in s.Members) s.Teleport(m, Vector3.Zero);
            Assert.Equal(225f, AfterContact.CoveredMetres(s.Repo, s.Commander, enemy), 1);                     // 200 m out + 25
            Assert.Equal(AfterContact.DirectRangeMetres, AfterContact.CoveredMetres(s.Repo, s.Commander, new Vector3(0f, 30f, 0f)));   // the urban floor
            Assert.Equal(AfterContact.MaxCoveredMetres, AfterContact.CoveredMetres(s.Repo, s.Commander, new Vector3(0f, 2000f, 0f))); // the cap
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2466</c> R4 (S7) — a squad that would ASSAULT one enemy instead BREAKS CONTACT when that enemy stands at a fresh <c>Direct</c> record of its side
        /// that remembers a stronger force; without the record (control) it assaults. ✅ Red-proof: the R4 clause removed from <c>Firm()</c> ⇒ it assaults ⇒ red.
        /// </summary>
        [Fact]
        public void CE2466_R4_NoFrontalAssault_OnAForceTheSideBrokeContactFrom()
        {
            foreach (bool remembered in new[] { false, true })
            {
                var s = new Squad(members: 6);
                var enemy = s.PoolMany(1, new Vector3(0f, 60f, 0f));
                Sides(s, enemy);
                if (remembered) Remember(s, new Vector3(5f, 62f, 0f), enemyStrength: 50f);
                s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
                for (int i = 0; i < 12; i++) s.Tick();
                if (remembered) Assert.Equal(BreakContactManeuver.ManeuverKind, s.Kind);
                else Assert.NotEqual(BreakContactManeuver.ManeuverKind, s.Kind);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2466</c> R6 (S14) — a <c>CoveredByFire</c> area ahead is never a crossing: <c>DangerAreaAhead</c> is false for it, <c>CoveredByFireAhead</c> true
        /// within 30 m along the route (false beyond). ✅ Red-proof: the exclusion dropped ⇒ DangerAreaAhead true ⇒ red.
        /// </summary>
        [Fact]
        public void CE2466_R6_CoveredGroundAhead_IsNotACrossing_ButItsOwnCondition()
        {
            var s = new Squad(4);
            s.Repo.AddComponent(s.Commander, new SimTransform { Position = new Vector3(10f, 5f, 0f), Rotation = Quaternion.Identity });
            var p = new SopAreaParams { WithinMetres = 30f };
            AreaAhead(s, distanceAlongRoute: 50f, kind: Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire);
            Assert.False(SquadSopConditions.CoveredByFireAhead(ref p, s.Commander, s.Repo));
            AreaAhead(s, distanceAlongRoute: 15f, kind: Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire);
            Assert.True(SquadSopConditions.CoveredByFireAhead(ref p, s.Commander, s.Repo));
            Assert.False(SquadSopConditions.DangerAreaAhead(ref p, s.Commander, s.Repo));
            AreaAhead(s, distanceAlongRoute: 0f, kind: Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire);
            Assert.False(SquadSopConditions.CoveredByFireAhead(ref p, s.Commander, s.Repo));   // already inside it: no hold in the open (U2)
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2466</c> R6 (S2) — the BasicSquadSop holds a bounding squad SHORT of covered ground (the <c>CoveredByFireAhead</c> reaction, the bound paused),
        /// and after <see cref="CoveredByFireAheadBehavior.HoldShortSeconds"/> it requests FIRE SUPPORT and reports TaskFailed — and goes on holding when the area
        /// is gone (never back in when the knowledge cools). ✅ Red-proof: the SOP row removed ⇒ the bound goes on ⇒ red.
        /// </summary>
        [Fact]
        public void CE2466_R6_TheSop_HoldsShortOfCoveredGround_ThenAsksForFireSupport_AndStaysHeld()
        {
            var s = new Squad(members: 6);
            Sides(s, Array.Empty<Entity>());
            s.Repo.AddComponent(s.Commander, new SimTransform { Position = new Vector3(10f, 5f, 0f), Rotation = Quaternion.Identity });
            s.Order("{\"Objective\":[10,100,0],\"BoundMetres\":20}");
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 10; i++) s.Tick();
            Assert.Equal(BehaviorNames.BoundingOverwatch, s.TaskName);
            AreaAhead(s, distanceAlongRoute: 12f, kind: Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire);
            for (int i = 0; i < SopReactionFrames + 1; i++) s.Tick();
            Assert.Equal(BehaviorNames.CoveredByFireAhead, s.TaskName);
            Assert.Equal(BehaviorNames.BoundingOverwatch, BehaviorIngressSystem.PausedTaskOf(s.Repo, s.Commander)!.BehaviorName);
            Entity sensor = default;
            for (int i = 0; i < (int)((CoveredByFireAheadBehavior.HoldShortSeconds + 3f) / 0.016f); i++)   // the harness steps 0.016 s
            {
                sensor = AreaAhead(s, distanceAlongRoute: 12f, kind: Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.CoveredByFire);
                s.Tick();
            }
            Assert.Equal(BehaviorNames.CoveredByFireAhead, s.TaskName);   // still the same hold (never restarted, so its clock ran)
            Assert.Equal(1, Queued(s, ReportKind.Request));
            Assert.Equal(1, Reports(s.Repo, s.Commander, ProgressKind.TaskFailed));
            s.Repo.GetComponentRW<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(sensor).Count = 0;   // the knowledge cooled
            for (int i = 0; i < 60; i++) s.Tick();
            Assert.Equal(BehaviorNames.CoveredByFireAhead, s.TaskName);   // still held: never back in
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2466</c> R3 (S1; backend's <c>CE-2469</c> steer point) — a BOUND takes its direction from the PRICED route, not the straight line: with the route
        /// answer's steer point 45° east of the objective's line, the moving element's bound points lie east of its men; with no steer (control) straight north.
        /// ✅ Red-proof: <c>Aim</c> back to the objective ⇒ the bound goes north ⇒ red.
        /// </summary>
        [Fact]
        public void CE2466_R3_ABound_FollowsThePricedRoute_NotTheStraightLine()
        {
            foreach (bool steer in new[] { false, true })
            {
                var s = new Squad(members: 4);
                s.Repo.AddComponent(s.Commander, new SimTransform { Position = new Vector3(6f, 0f, 0f), Rotation = Quaternion.Identity });
                var sensor = AreaAhead(s, distanceAlongRoute: 500f);
                void Stamp()
                {
                    ref var b = ref s.Repo.GetComponentRW<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(sensor);
                    b.Count = 0; b.LastUpdateTick = 1;
                    b.LastUpdateTimeSeconds = (float)((Fdp.ModuleHost.Abstractions.ISimulationView)s.Repo).Time;
                    b.HasSteer = steer ? (byte)1 : (byte)0;
                    b.SteerPoint = new Vector3(26f, 20f, 0f);   // 20 m along a route bending east (45°)
                }
                Stamp();
                s.Order("{\"Objective\":[6,100,0],\"BoundMetres\":20}");
                for (int i = 0; i < 6; i++) { Stamp(); s.Tick(); }
                var moves = s.LastOrders().Where(kv => kv.Value.Intent == BehaviorNames.AdvanceTo).ToList();
                Assert.NotEmpty(moves);
                foreach (var (m, o) in moves)
                {
                    float east = o.Objective.X - s.At(m).X;
                    if (steer) Assert.True(east > 5f, $"along the route: east of the man ({east:0.0} m)");
                    else Assert.True(MathF.Abs(east) < 3f, $"no route: straight at the objective ({east:0.0} m)");
                }
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2467</c> (Squad Tactics §7.10) — a squad on an ATTACK order that still has 3 : 1 over its contacts is not paused by the contact reaction
        /// under fire (its attack drill is the answer); outnumbered (control) the same squad reacts. ✅ Red-proof: the guard dropped from <c>NotDefending</c>
        /// ⇒ the superior squad reacts too ⇒ red.
        /// </summary>
        [Fact]
        public void CE2467_AnAttackOrder_WithSuperiority_IsNotPausedByTheContactReaction_OutnumberedItReacts()
        {
            foreach (bool superior in new[] { true, false })
            {
                var s = new Squad(members: 6);
                s.PoolMany(superior ? 1 : 12, new Vector3(0f, 60f, 0f));
                if (!superior)
                {
                    ref var squad = ref s.Repo.GetComponentRW<SquadCognitiveState>(s.Commander);
                    var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                        ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
                    for (int i = 0; i < 12; i++) span[i].Flags |= SquadContact.AnonymousFlag;   // heard: each counts as one armed man
                }
                s.Order("{\"Position\":[0,60,0]}", BehaviorNames.InfantrySquadAttack);
                s.StartSop("BasicSquadSop");
                for (int i = 0; i < 10; i++) s.Tick();
                Assert.Equal(BehaviorNames.InfantrySquadAttack, s.TaskName);
                Assert.Equal(superior, ReactToContactBehavior.PressesTheAttack(s.Repo, s.Commander));

                s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
                for (int i = 0; i < SopReactionFrames; i++) s.Tick();
                Assert.Equal(superior ? BehaviorNames.InfantrySquadAttack : BehaviorNames.ReactToContact, s.TaskName);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2481</c> (Squad Tactics §7.10) — a RUNNING ordered attack keeps pressing past the 3 : 1 that starts an assault, down to 1.5 : 1 (the
        /// culminating point): 📐 uf-corner-assault, 6 v 2 is exactly 3 : 1, and one man grazed (100 → 87.5) at 16.1 s stopped the whole squad. Here the harness's
        /// six men are unarmed (0.3 each, <c>ThreatDanger.Unarmed</c>): 1.8 against ONE heard enemy is a share of 0.357 — past 3 : 1, inside 1.5 : 1 ⇒ still
        /// pressing; four men down to a quarter health ⇒ no longer. 🔴 Red-proof: <c>PressesTheAttack</c> back on <c>AssaultShare</c> ⇒ the first assert red.
        /// </summary>
        [Fact]
        public void CE2481_ARunningAttack_PressesUntilOneAndAHalfToOne_NotStoppedByOneGraze()
        {
            var s = new Squad(members: 6);
            foreach (var m in s.Members) s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            s.PoolMany(1, new Vector3(0f, 60f, 0f));
            ref var squad = ref s.Repo.GetComponentRW<SquadCognitiveState>(s.Commander);
            var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
            span[0].Flags |= SquadContact.AnonymousFlag;   // heard: one armed man
            s.Order("{\"Position\":[0,60,0]}", BehaviorNames.InfantrySquadAttack);
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 10; i++) s.Tick();
            float share = Fdp.Toolkit.Utility.SquadInputs.EnemyShare(Fdp.Toolkit.Utility.SquadInputs.EnemyDangerOf(s.Repo, s.Commander), Fdp.Toolkit.Utility.SquadInputs.OwnStrengthOf(s.Repo, s.Commander));
            Assert.InRange(share, AfterContact.AssaultShare + 0.01f, AfterContact.PressShare - 0.01f);   // the case the hysteresis is for
            Assert.True(ReactToContactBehavior.PressesTheAttack(s.Repo, s.Commander), $"share {share:0.000}: past 3 : 1, still pressing");

            for (int m = 0; m < 4; m++) s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(s.Members[m]).Current = 25f;
            Assert.False(ReactToContactBehavior.PressesTheAttack(s.Repo, s.Commander), "past the culminating point");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2467</c> (Squad Tactics §7.10) — an assault LEARNS the ground the enemy covers (a <c>Direct</c> record of its side, the same learn as a break
        /// contact) the moment it starts, and asks the terrain for its flank only once that knowledge is there — so the flank's EQS path cost and the movers' routes
        /// are priced round the enemy's fire on the navmesh node (📐 U2: the flank was the junction mouth, reached straight up the enemy's street). With no record
        /// it still asks after <see cref="SuppressAndManeuverPlay.FlankWaitSeconds"/>. 🔴 Red-proof: the wait dropped ⇒ the flank is asked before the record ⇒ red.
        /// </summary>
        [Fact]
        public void CE2467_TheAssault_LearnsTheGroundTheEnemyCovers_AndAsksItsFlankOnceThatIsKnown()
        {
            foreach (bool recordLands in new[] { true, false })
            {
                var s = new Squad(members: 4);
                var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
                s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0001);
                Sides(s, new[] { enemy });
                if (!s.Repo.IsComponentTypeRegistered<UnderFireKnowledge>()) s.Repo.RegisterComponent<UnderFireKnowledge>();
                s.Order("{}", BehaviorNames.SuppressAndManeuver);
                int learned = 0;
                for (int i = 0; i < 3; i++) { s.Tick(); foreach (var l in s.Repo.Bus.Read<UnderFireLearned>()) { Assert.Equal(UnderFireCause.Direct, l.Cause); learned++; } }
                Assert.True(learned > 0, "the assault learned the ground the enemy covers");
                Assert.True(s.FlankQuery().IsNull, "no flank asked before the knowledge is there");
                if (recordLands)
                {
                    Remember(s, new Vector3(0f, 60f, 0f), enemyStrength: 1f);
                    s.Tick(); s.Tick();
                    Assert.False(s.FlankQuery().IsNull, "the knowledge is there: the flank is asked");
                }
                else
                {
                    for (int i = 0; i < (int)(SuppressAndManeuverPlay.FlankWaitSeconds / 0.016) + 5; i++) s.Tick();
                    Assert.False(s.FlankQuery().IsNull, "no record ever lands: the flank is asked anyway");
                }
            }
        }
    }
}
