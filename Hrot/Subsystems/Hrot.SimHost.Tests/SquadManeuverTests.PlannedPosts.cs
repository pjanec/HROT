using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Systems;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-8303</c> (agg-behaviors D-P3, R-4286; 📄 docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md §7) — a splitting stochastic squad
    /// holding a Defend gets REAL posts from SquadDefend's OWN plan (<see cref="DefencePlan"/> over a <see cref="SlotCrew"/>), written into
    /// <see cref="PlannedPosts"/> by <see cref="UnitPostPlanningSystem"/>. In the defence's own suite (T-1).
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static readonly Vector3 PlannedHouse = new(6f, 20f, 0f);

        /// <summary>A stochastic SQUAD (echelon 0) of <paramref name="slots"/> slots, <paramref name="up"/> of them up, slot <paramref name="gun"/> its
        /// machine gunner, holding a Defend on <see cref="PlannedHouse"/> — and the manager has just put it in Planning.</summary>
        private static (Squad S, UnitPostPlanningSystem Sys, EntityRepository R, Entity U) PlanningSquad(int slots = 10, int up = 10, int gun = 3)
        {
            var (s, _, repo, u) = StochasticMover(slots: slots, at: PlannedHouse);
            AggregateTestUnits.Make(repo, u, slots: slots, up: up, echelon: 0);
            repo.GetComponentRW<UnitSlotTable>(u).Slots[gun].Role = Fdp.Toolkit.Aggregate.SlotRole.MachineGunner;
            s.Order("{" + Loc("Position", PlannedHouse) + ",\"SearchRadius\":25}", ResolutionRoutingTable.Plays.Defend);
            s.Tick(); s.Tick(); s.Tick();
            ref var a = ref repo.GetComponentRW<AggregateUnit>(u);
            a.Splitting = SplitStage.Planning;
            a.SplittingSince = (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime;
            return (s, new UnitPostPlanningSystem(), repo, u);
        }

        private static void PlanFrame(Squad s, UnitPostPlanningSystem sys, double seconds = 0.016)
        {
            if (seconds > 0.016) s.Pause(seconds - 0.016);
            s.Tick();
            sys.Execute(s.Repo, (float)seconds);
        }

        /// <summary>The window points SimHost answers the unit's window ask with: <paramref name="n"/> windows 3 m apart along the house.</summary>
        private static Vector3[] HouseWindows(int n = 12)
        {
            var w = new Vector3[n];
            for (int k = 0; k < n; k++) w[k] = PlannedHouse + new Vector3(-16f + 3f * k, 6f, 3f);
            return w;
        }

        private static PlannedPosts Planned(EntityRepository repo, Entity u) => repo.GetComponentRO<PlannedPosts>(u);

        /// <summary>⭐⭐ D-P3 ACCEPTANCE (the planner) — a memberless stochastic squad in a house: the window ask goes out FROM THE UNIT (it is the
        /// carrier and the origin), SimHost's points come back, and the plan writes one post per LIVE slot (Ready, count = the live slots), each a
        /// window from the answer, no two the same, the machine gunner's entry flagged. ✅ Red-proofs: the published entry not walking LIVE slots
        /// (every slot) ⇒ Count 10 ≠ 8 ⇒ red · the gun flag dropped ⇒ red.</summary>
        [Fact]
        public void DP3_TheStochasticPlanner_WritesAWindowPostPerLiveSlot_FromTheUnitsOwnAsk()
        {
            var (s, sys, repo, u) = PlanningSquad(slots: 10, up: 8, gun: 3);
            PlanFrame(s, sys);
            Assert.Equal(PlannedPosts.StagePlanning, Planned(repo, u).Stage);
            Assert.Equal((byte)0, Planned(repo, u).Ready);
            var ask = EqsChildSensor.Find(repo, u, SquadDefendPlay.WindowSite);
            Assert.False(ask.IsNull, "the window ask is the UNIT's own child sensor (the carrier)");
            Assert.Equal(u, repo.GetComponentRO<EqsSensor>(ask).ContextSlot0);   // measured from the unit (EQS §19.5)
            var windows = HouseWindows();
            AnswerAll(s, ask, windows);
            for (int i = 0; i < 30 && Planned(repo, u).Ready == 0; i++) PlanFrame(s, sys);

            var pp = Planned(repo, u);
            Assert.Equal((byte)1, pp.Ready);
            Assert.Equal(PlannedPosts.StageReady, pp.Stage);
            Assert.Equal(8, pp.Count);   // the live slots (2 of 10 are down)
            var seen = new HashSet<Vector3>();
            for (int k = 0; k < pp.Count; k++)
            {
                Assert.True(pp.TryGet(k, out var post), "live slot " + k + " has a post");
                Assert.True(post.Window, "a window post");
                Assert.Contains(post.Point, windows);
                Assert.True(seen.Add(post.Point), "no two men on one post");
                Assert.Equal(k == 3, post.Gun);   // slot 3 (live rank 3) carries the gun
            }
            Assert.False(pp.TryGet(8, out _));
            Assert.True(EqsChildSensor.Find(repo, u, SquadDefendPlay.WindowSite).IsNull, "the ask is gone once answered");
        }

        /// <summary>⭐ D-P3 — a SILENT muscle never stalls the split: no answer, and within the stage timeout the plan is over with no posts
        /// (Ready, every live slot without one ⇒ his fallback point), well inside the manager's 10 s deadline. ✅ Red-proof: the window stage's
        /// timeout removed (Settle waiting forever) ⇒ never Ready ⇒ red.</summary>
        [Fact]
        public void DP3_ASilentMuscle_GivesReadyWithNoPosts_InsideTheDeadline()
        {
            var (s, sys, repo, u) = PlanningSquad(slots: 10, up: 10);
            double start = repo.GetSingleton<GlobalTime>().TotalTime;
            PlanFrame(s, sys);   // the plan begins; its window ask is never answered
            for (int i = 0; i < 40 && Planned(repo, u).Ready == 0; i++) PlanFrame(s, sys, 0.5);
            var pp = Planned(repo, u);
            Assert.Equal((byte)1, pp.Ready);
            Assert.InRange(repo.GetSingleton<GlobalTime>().TotalTime - start, SquadDefendPlay.AskTimeoutSeconds, ResolutionBands.Default.DeadlineSeconds);
            Assert.Equal(10, pp.Count);
            for (int k = 0; k < pp.Count; k++) Assert.False(pp.TryGet(k, out _), "no post ⇒ the fallback line");
        }

        /// <summary>⭐ D-P3 — leaving Planning drops the plan's asks; a WITHDRAWN trigger (back to Stochastic, not splitting) clears
        /// <see cref="PlannedPosts"/>; a unit that is no squad, or holds no Defend, plans nothing. ✅ Red-proofs: the clear dropped ⇒ the posts
        /// survive the withdrawal ⇒ red · the abandon dropped ⇒ the ask outlives Planning ⇒ red.</summary>
        [Fact]
        public void DP3_AWithdrawnTrigger_DropsTheAsks_AndClearsThePlannedPosts()
        {
            var (s, sys, repo, u) = PlanningSquad();
            PlanFrame(s, sys);
            Assert.False(EqsChildSensor.Find(repo, u, SquadDefendPlay.WindowSite).IsNull);
            repo.GetComponentRW<AggregateUnit>(u).Splitting = SplitStage.NotSplitting;   // the manager withdrew it
            PlanFrame(s, sys);
            s.Tick();   // the child sensors leave through the frame's command buffer
            Assert.True(EqsChildSensor.Find(repo, u, SquadDefendPlay.WindowSite).IsNull, "the ask went with the plan");
            Assert.True(EqsChildSensor.Find(repo, u, SquadDefendPlay.FrameSite).IsNull, "the frame survey too");
            Assert.Equal(PlannedPosts.StageNone, Planned(repo, u).Stage);
            Assert.Equal(0, sys.PlansHeld);

            // a platoon plans nothing (squad echelon only — DP3-A)
            repo.GetComponentRW<AggregateUnit>(u).Echelon = 1;
            repo.GetComponentRW<AggregateUnit>(u).Splitting = SplitStage.Planning;
            PlanFrame(s, sys);
            Assert.True(EqsChildSensor.Find(repo, u, SquadDefendPlay.WindowSite).IsNull);
            Assert.Equal(0, sys.PlansHeld);
        }

        /// <summary>⭐ D-P3 (0 B, R-2084) — a planning frame waiting on its answer allocates nothing (the system, the crew, the plan's step).
        /// ✅ Red-proof: a <c>new Vector3[]</c> fallback buffer per unit per frame ⇒ red.</summary>
        [Fact]
        public void DP3_ASteadyPlanningFrame_AllocatesNothing()
        {
            var (s, sys, repo, u) = PlanningSquad();
            for (int i = 0; i < 4; i++) PlanFrame(s, sys);   // the plan began; its ask is out (warm-up)
            Assert.False(EqsChildSensor.Find(repo, u, SquadDefendPlay.WindowSite).IsNull);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                var gt = repo.GetSingleton<GlobalTime>(); gt.TotalTime += 0.001; repo.SetSingleton(gt);
                sys.Execute(repo, 0.001f);
            }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal((byte)0, Planned(repo, u).Ready);   // still waiting: the frames measured were the steady ones
        }

        /// <summary>⭐⭐ D-P3 DP3-A — ONE plan, two crews: the same men (a physical roster, and a slot table whose virtual men stand on the same
        /// points) given the same answer get the SAME posts — the stochastic path is the physical plan, not a copy. ✅ Red-proof: <see cref="SlotCrew"/>
        /// placing each man on the wrong fallback point (rank + 1) ⇒ the picker gives other posts ⇒ red.</summary>
        [Fact]
        public void DP3_TheRosterCrew_AndTheSlotCrew_GetTheSamePosts_FromTheSameAnswer()
        {
            var s = new Squad(members: 5);
            var repo = s.Repo;
            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new SimTransform { Position = new Vector3(8f, 0f, 0f), Rotation = Quaternion.Identity });
            AggregateTestUnits.Make(repo, unit, slots: 5, up: 5, echelon: 0);
            var men = new Vector3[SlotCrew.MaxSlots];
            for (int i = 0; i < 5; i++) men[i] = s.At(s.Members[i]);
            var windows = new[] { new Vector3(1f, 9f, 3f), new Vector3(13f, 9f, 3f), new Vector3(4f, 9f, 3f), new Vector3(10f, 9f, 6f), new Vector3(7f, 9f, 3f), new Vector3(16f, 9f, 3f) };
            var roster = repo.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(s.Commander);
            DefencePosts a = Fresh(), b = Fresh();
            DefenceAsk askA = default, askB = default;
            var none = default(AuthoredPosts);
            bool doneA = false, doneB = false;
            for (int f = 0; f < 40 && !(doneA && doneB); f++)
            {
                s.Tick();
                double now = repo.GetSingleton<GlobalTime>().TotalTime;
                var ctx = new SquadPlayContext(repo, s.Commander, now, in roster);
                var physical = new RosterCrew(in ctx);
                var stochastic = new SlotCrew(repo, unit, now, in repo.GetComponentRO<UnitSlotTable>(unit), men);
                if (!doneA) doneA = DefencePlan.Step(ref a, ref askA, in physical, in none, new Vector3(8f, 4f, 0f), 25f);
                if (!doneB) doneB = DefencePlan.Step(ref b, ref askB, in stochastic, in none, new Vector3(8f, 4f, 0f), 25f);
                foreach (var carrier in new[] { s.Commander, unit })
                {
                    var ask = EqsChildSensor.Find(repo, carrier, SquadDefendPlay.WindowSite);
                    if (!ask.IsNull && !repo.GetComponentRO<EqsCognitiveBuffer>(ask).IsReady) AnswerAll(s, ask, windows);
                    AnswerEmptySurvey(repo, carrier, SquadDefendPlay.FrameSite);
                    AnswerEmptySurvey(repo, carrier, SquadDefendPlay.CoverageSite);
                }
            }
            Assert.True(doneA && doneB, "both plans finished");
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < 5; i++)
            {
                unsafe
                {
                    Assert.NotEqual(DefencePosts.None, a.PostOf[i]);
                    Assert.Equal(a.Point(a.PostOf[i]), b.Point(b.PostOf[i]));
                }
            }

            static DefencePosts Fresh()
            {
                var p = default(DefencePosts);
                unsafe { for (int i = 0; i < DefencePosts.Capacity; i++) { p.PostOf[i] = DefencePosts.None; p.MovedAt[i] = double.NegativeInfinity; p.DwellFrom[i] = double.NegativeInfinity; } }
                p.Hunter = DefencePosts.None;
                return p;
            }
        }

        /// <summary>The survey answer SimHost gives at once, empty (no windows, no rays), on any carrier's child at <paramref name="site"/>.</summary>
        private static void AnswerEmptySurvey(EntityRepository repo, Entity carrier, int site)
        {
            var child = EqsChildSensor.Find(repo, carrier, site);
            if (child.IsNull || !repo.IsAlive(child)) return;
            if (repo.HasComponent<WorldSurveyStatus>(child) && repo.GetComponentRO<WorldSurveyStatus>(child).Version != 0) return;
            repo.SetComponent(child, new WorldSurveyStatus { LastUpdateTick = 1, LastUpdateTimeSeconds = (float)repo.GetSingleton<GlobalTime>().TotalTime, Version = 1 });
            repo.SetManagedComponent(child, new WorldSurveyAnswer { Payload = new WorldSurveyPayload() });
        }
    }
}
