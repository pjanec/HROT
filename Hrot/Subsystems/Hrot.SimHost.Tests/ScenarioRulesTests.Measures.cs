using System.Numerics;
using System.Text.RegularExpressions;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ rules S4 E-2 / E-3 (📄 docs/DESIGN_Scenario_Rules.md §00.11) — the measures of what the subject's men DID (HitRatio · Count · ReportLatency · TimeTo · Sample), scored by the ONE
    /// <c>ObjectiveRules.Score</c> (<c>lowerIsBetter</c> is its flag), and <c>common.group</c> on the status. Through the real load path, from synthetic events.</summary>
    public sealed partial class ScenarioRulesTests
    {
        private static void Shoot(ObjWorld w, Entity shooter, int n = 1)
        {
            if (!w.Repo.Bus.IsRegistered<WeaponFireIntent>()) w.Repo.Bus.Register<WeaponFireIntent>();
            for (int i = 0; i < n; i++) w.Repo.Bus.Publish(new WeaponFireIntent { Shooter = shooter });
        }

        private static string MeasureJson(ObjWorld w, Entity subject, string extra = "", string common = "")
            => $"{{{Common(w, subject, common)}{extra}}}";

        /// <summary>HitRatio — shots by the men ÷ hits on a hostile, per the CE-4155 attribute: <c>last</c> counts a hit whose Source is a man, <c>original</c> one whose OriginalSource is, <c>both</c> either;
        /// a shot by a stranger and a hit on a friend are no part of it; 0 shots ⇒ Value 0, Active, Score 0; default target 0.30. ✅ Red-proof: <c>NoHarmBehavior.Sources</c> returning the
        /// <c>Source</c> for every attribute ⇒ the <c>original</c> ratio is wrong ⇒ red.</summary>
        [Fact]
        public void HitRatio_HitsOverShots_ByAttribute_ZeroShotsScoresZero()
        {
            var w = new ObjWorld();
            var m1 = w.Man(Inside, ForceId.Friend); var m2 = w.Man(Inside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, m1, m2);
            var stranger = w.Man(Inside, ForceId.Friend); var foe = w.Man(Inside, ForceId.Hostile, health: 100f); var pal = w.Man(Inside, ForceId.Friend, health: 100f);
            var last = w.Objective("HitRatio", MeasureJson(w, squad));
            var orig = w.Objective("HitRatio", MeasureJson(w, squad, ",\"attribute\":\"original\""));
            var both = w.Objective("HitRatio", MeasureJson(w, squad, ",\"attribute\":\"both\""));
            w.Run(1);
            Assert.Equal((ObjectiveRole.Measure, "HitRatio", 0f, 0.30f, ObjectiveState.Active, 0f), (w.S(last).Role, w.S(last).Kind.ToString(), w.S(last).Value, w.S(last).Target, w.S(last).State, w.S(last).Score));

            Shoot(w, m1, 6); Shoot(w, m2, 4); Shoot(w, stranger, 5);              // 10 shots by the men
            w.Harm(foe, m1); w.Harm(foe, m2);                                      // E1, E2: direct
            w.Harm(foe, stranger, original: m1); w.Harm(foe, stranger, original: m2);   // E3, E3b: rethrown by a stranger, started by a man
            w.Harm(foe, m1, original: stranger);                                   // E4: a man's, started by a stranger
            w.Harm(foe, stranger);                                                 // a stranger's: nobody's
            w.Harm(pal, m1);                                                       // on a friend: no hit
            w.Tick();
            Assert.Equal(0.3f, w.S(last).Value, 3);    // E1 E2 E4
            Assert.Equal(0.4f, w.S(orig).Value, 3);    // E1 E2 E3 E3b
            Assert.Equal(0.5f, w.S(both).Value, 3);    // all five
            Assert.Equal(1f, w.S(last).Score, 3);      // 0.3 of 0.3
            Assert.Equal("3 hits / 10 shots", w.S(last).Reason.ToString());
        }

        private static (Entity Squad, Entity M, Entity Foe, Entity Pal, Entity Civ) FireWorld(ObjWorld w)
        {
            var m = w.Man(Inside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, m);
            return (squad, m, w.Man(Inside, ForceId.Hostile, health: 10f), w.Man(Inside, ForceId.Friend, health: 10f), w.Man(Inside, ForceId.Neutral, health: 10f));
        }

        /// <summary>Count — shots · hits · kills (a hostile out of the fight after a man's hit, once; a corpse shot again is no second kill) · fratricide (a hit on his own side, not himself) · civiliansHarmed ·
        /// reports (from the subject or anyone under it). fratricide / civiliansHarmed are lower-is-better, Target 0 (one is score 0, none score 1); the others higher-is-better against <c>target</c>.</summary>
        [Fact]
        public void Count_EveryWhat_FromEvents_FaultsAreLowerIsBetter()
        {
            var w = new ObjWorld();
            var (squad, m, foe, pal, civ) = FireWorld(w);
            var other = w.Man(Inside, ForceId.Friend);
            var c = new System.Collections.Generic.Dictionary<string, Entity>();
            foreach (var what in new[] { "shots", "hits", "kills", "fratricide", "civiliansHarmed", "reports" })
                c[what] = w.Objective("Count", MeasureJson(w, squad, $",\"what\":\"{what}\",\"target\":4"));
            w.Run(1);
            Assert.Equal((0f, 1f, 0f), (w.S(c["fratricide"]).Value, w.S(c["fratricide"]).Score, w.S(c["fratricide"]).Target));   // nothing yet: no fault, full score
            Assert.Equal(0f, w.S(c["kills"]).Score);

            Shoot(w, m, 3); Shoot(w, other, 2);
            w.Harm(foe, m); w.Harm(pal, m); w.Harm(pal, other); w.Harm(civ, m); w.Harm(m, m);   // a hit · a friend · NOT a man's · a civilian · himself
            if (!w.Repo.Bus.IsRegisteredManaged<SquadReportNotification>()) w.Repo.Bus.RegisterManaged<SquadReportNotification>();
            w.Repo.Bus.PublishManaged(new SquadReportNotification { FromUnit = squad, SentAt = 10 });
            w.Repo.Bus.PublishManaged(new SquadReportNotification { FromUnit = other, SentAt = 10 });
            w.Tick();
            Assert.Equal((3f, 1f, 0f, 1f, 1f, 1f), (w.S(c["shots"]).Value, w.S(c["hits"]).Value, w.S(c["kills"]).Value, w.S(c["fratricide"]).Value, w.S(c["civiliansHarmed"]).Value, w.S(c["reports"]).Value));
            Assert.Equal(0.75f, w.S(c["shots"]).Score, 3);                       // 3 of 4
            Assert.Equal(0f, w.S(c["fratricide"]).Score);                          // one fault ⇒ target 0 / value 1 = 0
            Assert.Equal(0f, w.S(c["civiliansHarmed"]).Score);

            w.Repo.GetComponentRW<Health>(foe).Current = 0f;                       // the hostile goes down
            w.Tick();
            Assert.Equal(1f, w.S(c["kills"]).Value);
            w.Harm(foe, m); w.Tick();                                              // a corpse shot again
            Assert.Equal(1f, w.S(c["kills"]).Value);
            Assert.Equal(2f, w.S(c["hits"]).Value);
        }

        /// <summary>ReportLatency — the mean of SentAt − ObservedAt of the subject's reports so far, lower is better against 60 s: none ⇒ Score 0; mean 60 ⇒ 1; mean 80 ⇒ 0.75; another unit's report is ignored.</summary>
        [Fact]
        public void ReportLatency_MeanOfTheSubjectsReports_LowerIsBetter()
        {
            var w = new ObjWorld();
            var (squad, _, _, _, _) = FireWorld(w);
            var stranger = w.Squad(ForceId.Friend);
            var rl = w.Objective("ReportLatency", MeasureJson(w, squad));
            w.Run(1);
            Assert.Equal((0f, 60f, 0f), (w.S(rl).Value, w.S(rl).Target, w.S(rl).Score));   // nothing reported yet is not a pass
            if (!w.Repo.Bus.IsRegisteredManaged<SquadReportNotification>()) w.Repo.Bus.RegisterManaged<SquadReportNotification>();
            void Report(Entity from, double sent, float observed) => w.Repo.Bus.PublishManaged(new SquadReportNotification { FromUnit = from, SentAt = sent, Report = new CommandReport { ObservedAt = observed } });
            Report(squad, 100, 70); Report(squad, 200, 110); Report(stranger, 300, 0);
            w.Tick();
            Assert.Equal(60f, w.S(rl).Value, 3);       // (30 + 90) / 2
            Assert.Equal(1f, w.S(rl).Score);
            Report(squad, 300, 180);                   // +120 ⇒ mean 80
            w.Tick();
            Assert.Equal(80f, w.S(rl).Value, 3);
            Assert.Equal(0.75f, w.S(rl).Score, 3);
        }

        /// <summary>The scorer — lowerIsBetter is ONE flag of <c>ObjectiveRules.Score</c>, and a Counter (a pool, higher-is-better by construction) never asks for it: a Counter at 7 of 10 scores 0.7.
        /// ✅ Red-proof: <c>CounterBehavior</c> writing its score with lowerIsBetter ⇒ 1 ⇒ red.</summary>
        [Fact]
        public void LowerIsBetter_DoesNotChangeACountersScore()
        {
            var w = new ObjWorld();
            var pool = w.Objective("Counter", CounterJson(10, "", ""));
            w.Run(1);
            var s = w.S(pool);
            s.Value = 7f;
            Assert.Equal(0.7f, ObjectiveRules.Score(in s), 3);
            Assert.Equal(1f, ObjectiveRules.Score(in s, lowerIsBetter: true));     // the same numbers, the other way
            w.Repo.GetComponentRW<ObjectiveStatus>(pool).Value = 7f;
            w.Tick();
            Assert.Equal(0.7f, w.S(pool).Score, 3);
        }

        /// <summary>TimeTo — Value = the scenario time the objective became Achieved, lower is better against <c>target</c>; before then (and when it Failed) Value = now and Score 0.</summary>
        [Fact]
        public void TimeTo_AchievedTime_LowerIsBetter_NeverIsScoreZero()
        {
            var w = new ObjWorld();
            var man = w.Man(Outside, ForceId.Friend); var squad = w.Squad(ForceId.Friend, man);
            var reach = w.Objective("ReachArea", $"{{{Common(w, squad)},\"area\":{Circle0},\"minMen\":1}}");
            var doomed = w.Objective("ReachArea", $"{{{Common(w, squad, ",\"deadlineSeconds\":3")},\"area\":{{\"circle\":{{\"center\":[500,500,0],\"radius\":1}}}},\"minMen\":1}}");
            string TimeTo(Entity obj, float target) => $"{{{NoSubject.Replace("}", ",\"subject\":" + w.Net(squad) + "}")},\"objective\":{w.Net(obj)},\"target\":{target}}}";
            var roomy = w.Objective("TimeTo", TimeTo(reach, 10f)); var tight = w.Objective("TimeTo", TimeTo(reach, 2f)); var never = w.Objective("TimeTo", TimeTo(doomed, 10f));
            w.Run(2);
            Assert.Equal((0f, "not achieved yet"), (w.S(roomy).Score, w.S(roomy).Reason.ToString()));
            Assert.Equal(w.Time, w.S(roomy).Value, 3);                           // the time so far
            w.Move(man, Inside);
            w.Run(1);
            Assert.Equal(ObjectiveState.Achieved, w.S(reach).State);
            double at = w.S(reach).ChangedAt;
            Assert.Equal((float)at, w.S(roomy).Value);
            Assert.Equal(1f, w.S(roomy).Score);                                  // at ≤ 10
            Assert.Equal(2f / (float)at, w.S(tight).Score, 3);                   // 2 / at
            w.Run(5);
            Assert.Equal((float)at, w.S(roomy).Value);                           // frozen
            Assert.Equal(ObjectiveState.Failed, w.S(doomed).State);
            Assert.Equal((0f, (float)w.Time), (w.S(never).Score, w.S(never).Value));
        }

        /// <summary>Sample — a registered [UtilityInput] read on the subject through the utility reader store: mean · max · timeAbove over the ticks; an unregistered input reads nothing (Score 0, the reason
        /// says so); lowerIsBetter scores a stress-like input.</summary>
        [Fact]
        public void Sample_ReadsARegisteredInput_MeanMaxTimeAbove_AndLowerIsBetter()
        {
            StandardInputs.RegisterAll();
            var w = new ObjWorld();
            var man = w.Man(Inside, ForceId.Friend, health: 10f);
            string Json(string agg, string extra = "") => MeasureJson(w, man, $",\"input\":\"HealthFraction\",\"aggregate\":\"{agg}\",\"threshold\":0.5{extra}");
            var mean = w.Objective("Sample", Json("mean")); var max = w.Objective("Sample", Json("max")); var above = w.Objective("Sample", Json("timeAbove"));
            var lowOk = w.Objective("Sample", Json("max", ",\"lowerIsBetter\":true,\"target\":1"));
            var ghost = w.Objective("Sample", MeasureJson(w, man, ",\"input\":\"NoSuchInputAnywhere\",\"aggregate\":\"mean\""));
            w.Run(2);                                                            // full health: fraction 1
            int a = Readings(w, mean);
            w.Repo.GetComponentRW<Health>(man).Current = 0f;                     // fraction 0
            w.Run(2);
            int total = Readings(w, mean);
            Assert.Equal((float)a / total, w.S(mean).Value, 3);
            Assert.Equal(1f, w.S(max).Value);
            Assert.Equal((a - 1) * 0.5f, w.S(above).Value, 3);                   // the first reading has no dt
            Assert.Equal(1f, w.S(lowOk).Score);                                  // max 1 ≤ target 1
            Assert.Equal((0f, 0f), (w.S(ghost).Value, w.S(ghost).Score));
            Assert.Contains("no reading of 'NoSuchInputAnywhere'", w.S(ghost).Reason.ToString());
        }

        private static int Readings(ObjWorld w, Entity e) => int.Parse(Regex.Match(w.S(e).Reason.ToString(), @"over (\d+) reading").Groups[1].Value);

        /// <summary>group — <c>common.group</c> reaches the status (and is trimmed by FixedString32); none = empty.</summary>
        [Fact]
        public void Group_FromTheCommonBlock_ReachesTheStatus()
        {
            var w = new ObjWorld();
            var a = w.Objective("Counter", $"{{\"common\":{{\"title\":\"T\",\"group\":\"fire\"}},\"start\":1}}");
            var b = w.Objective("Counter", CounterJson(1));
            w.Run(1);
            Assert.Equal(("fire", ""), (w.S(a).Group.ToString(), w.S(b).Group.ToString()));
        }

        /// <summary>A tree of measures closes by its score — the root is WeightedMean over a measure child that never ends by itself; once the exercise closed (the measure's value frozen) the root is
        /// Achieved at score ≥ pass, not "Failed: not achieved by the end". ✅ Red-proof: the <c>Measure &amp;&amp; Closed</c> clause removed from <c>ObjectiveChildren.Of</c> ⇒ Failed ⇒ red.</summary>
        [Fact]
        public void ATreeOfMeasures_ClosesByItsScore()
        {
            var w = new ObjWorld();
            var root = w.Objective("WeightedMean", $"{{{NoSubject}}}");
            var m = w.Objective("Counter", $"{{\"common\":{{\"title\":\"T\",\"parent\":{w.Net(root)}}},\"start\":10}}");
            w.Run(2);
            Assert.Equal(ObjectiveState.Active, w.S(root).State);
            Assert.Equal(1f, w.S(root).Score, 3);
            w.Environment();
            var src = Cond(w);
            Fire(w, src, Do(Step("EndExercise", "{\"reason\":\"done\"}")));
            Flip(w, src, true);
            w.Run(4);
            Assert.Equal((byte)1, w.S(m).Closed);
            Assert.Equal(ObjectiveState.Achieved, w.S(root).State);
        }

        /// <summary>⭐ rules S4 ② — a pass mark on EVERY status (GO / NO-GO = <c>Score ≥ Pass</c>), written in one place (<c>ObjectiveRules.PassOf</c>) through the real load path:
        /// a measure 1 (its target met), a Counter 0 (a pool, no verdict), an ordinary objective 1, <c>AllMustPass</c> 1, <c>WeightedMean</c> 0.6 / its <c>passScore</c> (<c>Target</c> keeps the same number),
        /// <c>MemberEvaluation</c> 0.6; an authored <c>common.pass</c> wins on a measure. ✅ Red-proof: the measure default of <c>PassOf</c> changed to 0.5 ⇒ red.</summary>
        [Fact]
        public void Pass_OnEveryKind_OneDefaultEach_AnAuthoredPassWins()
        {
            var w = new ObjWorld();
            var m = w.Man(Inside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, m);
            var measure = w.Objective("Count", MeasureJson(w, squad, ",\"what\":\"shots\",\"target\":4"));
            var authored = w.Objective("Count", MeasureJson(w, squad, ",\"what\":\"shots\",\"target\":4", ",\"pass\":0.75"));
            var pool = w.Objective("Counter", CounterJson(10, "", ""));
            var reach = w.Objective("ReachArea", $"{{{Common(w, squad)},\"area\":{Circle0}}}");
            var all = w.Objective("AllMustPass", $"{{{NoSubject}}}");
            var mean = w.Objective("WeightedMean", $"{{{NoSubject}}}");
            var meanAuthored = w.Objective("WeightedMean", $"{{{NoSubject},\"passScore\":0.8}}");
            w.Run(1);
            Assert.Equal(1f, w.S(measure).Pass);
            Assert.Equal(0.75f, w.S(authored).Pass);
            Assert.Equal(0f, w.S(pool).Pass);
            Assert.Equal(1f, w.S(reach).Pass);
            Assert.Equal(1f, w.S(all).Pass);
            Assert.Equal(0.6f, w.S(mean).Pass);
            Assert.Equal(0.8f, w.S(meanAuthored).Pass);
            Assert.Equal(0.8f, w.S(meanAuthored).Target);   // Target keeps the number it had
            Shoot(w, m, 3); w.Tick();
            Assert.Equal(0.75f, w.S(measure).Score, 3);
            Assert.False(w.S(measure).Score >= w.S(measure).Pass);   // 3 of 4 shots: NO-GO at the default pass 1
            Assert.True(w.S(authored).Score >= w.S(authored).Pass);   // GO at the authored 0.75
        }

        /// <summary>⭐ rules S4 ⑥ — <c>Count assists</c>: a hostile the subject's man hit and somebody else finished counts ONCE, when he goes down within <c>assistSeconds</c> of the subject's last hit;
        /// not a kill (the killer's own victim is no assist), not after the window, never for a stranger's victim. ✅ Red-proof: the window test removed from <c>MeasureEvidence</c> ⇒ the late one counts ⇒ red.</summary>
        [Fact]
        public void Count_Assists_TheKillBySomebodyElse_OnceAndInsideTheWindow()
        {
            var w = new ObjWorld();
            var m = w.Man(Inside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, m);
            var buddy = w.Man(Inside, ForceId.Friend);                                  // not of the subject
            var quick = w.Man(Inside, ForceId.Hostile, health: 10f);                    // hit by m, finished by buddy in time: an assist
            var slow = w.Man(Inside, ForceId.Hostile, health: 10f);                     // hit by m, finished by buddy long after: none
            var mine = w.Man(Inside, ForceId.Hostile, health: 10f);                     // m's own kill: a kill, not an assist
            var theirs = w.Man(Inside, ForceId.Hostile, health: 10f);                   // only buddy hit him: nobody's assist
            var assists = w.Objective("Count", MeasureJson(w, squad, ",\"what\":\"assists\",\"target\":3"));
            var kills = w.Objective("Count", MeasureJson(w, squad, ",\"what\":\"kills\",\"target\":3"));
            w.Run(1);
            w.Harm(quick, m); w.Harm(slow, m); w.Harm(mine, m); w.Harm(theirs, buddy);
            w.Tick();
            w.Harm(quick, buddy); w.Harm(slow, buddy); w.Tick();                        // buddy's harm supersedes m's credit
            w.Repo.GetComponentRW<Health>(quick).Current = 0f; w.Repo.GetComponentRW<Health>(mine).Current = 0f; w.Repo.GetComponentRW<Health>(theirs).Current = 0f;
            w.Tick();
            Assert.Equal((1f, 1f), (w.S(assists).Value, w.S(kills).Value));              // quick = assist, mine = kill
            w.Harm(quick, buddy); w.Tick();                                              // a corpse shot again: still one
            Assert.Equal(1f, w.S(assists).Value);
            w.Run(12);                                                                  // past the 10 s window
            w.Repo.GetComponentRW<Health>(slow).Current = 0f; w.Tick();
            Assert.Equal((1f, 1f), (w.S(assists).Value, w.S(kills).Value));              // too late: not an assist, and not m's kill either
        }
    }
}
