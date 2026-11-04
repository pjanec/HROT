using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S7 (📄 docs/DESIGN_Scenario_Rules.md §00.13) — the per-player game score: the capture event (P-4), the four new <c>Count</c> readings (P-3), the <c>Points</c> composite (P-2),
    /// <c>MemberEvaluation</c> by side with a points total (P-5) and the <c>Best</c> award (P-7). Through the real load path, from synthetic events.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>A common block with a title, an optional subject (net id) and extras (parent, points, scored …).</summary>
        private static string Cm(string title, int subject = 0, string extra = "")
            => $"\"common\":{{\"title\":\"{title}\"{(subject != 0 ? $",\"subject\":{subject}" : "")}{extra}}}";

        private static Entity Down(ObjWorld w, Entity man) => SetHealth(w, man, 0f);
        private static Entity SetHealth(ObjWorld w, Entity man, float hp) { w.Repo.GetComponentRW<Health>(man).Current = hp; return man; }

        private static List<ObjectiveCapturedEvent> ReadCaptured(ObjWorld w)
        {
            w.Repo.Bus.SwapBuffers();   // what the tick just published is readable now (the next tick swaps again)
            return w.Repo.Bus.IsRegistered<ObjectiveCapturedEvent>() ? w.Repo.ReadEvents<ObjectiveCapturedEvent>().ToArray().ToList() : new();
        }

        /// <summary>P-4 — a capture point that changes hands to a side publishes <c>ObjectiveCapturedEvent</c> ONCE, naming the point, the side and the men of that side standing inside at that moment
        /// (a man outside and a man of the other side are not named); losing the point to Neutral publishes nothing. ✅ Red-proof: the <c>PublishCaptured</c> call removed from
        /// <c>HoldAreaBehavior.CaptureBySide</c> ⇒ no event ⇒ red.</summary>
        [Fact]
        public void P4_ACapture_PublishesOnce_WithTheMenInside()
        {
            var w = new ObjWorld();
            var a = w.Man(Inside, ForceId.Friend, 10f); var b = w.Man(Inside, ForceId.Friend, 10f); var c = w.Man(Inside, ForceId.Friend, 10f);
            var outside = w.Man(Outside, ForceId.Friend); var hostileIn = w.Man(Inside, ForceId.Hostile);
            var pt = w.Objective("HoldArea", $"{{{NoSubject},\"area\":{Circle0},\"minMen\":2,\"capture\":true,\"captureSeconds\":2}}");
            var seen = new List<ObjectiveCapturedEvent>();
            for (int i = 0; i < 12; i++)
            {
                w.Tick();
                seen.AddRange(ReadCaptured(w));
            }
            Assert.Equal(ForceId.Friend, w.S(pt).Owner);
            var e = Assert.Single(seen);
            Assert.Equal((pt, ForceId.Friend, 3), (e.Point, e.Side, e.MenCount));
            var named = Enumerable.Range(0, e.MenCount).Select(i => e.Men[i]).ToHashSet();
            Assert.True(named.SetEquals(new[] { a, b, c }), "the men of the side inside: " + named.Count);
            Assert.DoesNotContain(outside, named);
            Assert.DoesNotContain(hostileIn, named);
        }

        private static (ObjWorld W, Entity Squad, Entity M, Entity Foe, Entity Pal, Entity Other) ScoreWorld()
        {
            var w = new ObjWorld();
            var m = w.Man(Inside, ForceId.Friend, 10f);
            var squad = w.Squad(ForceId.Friend, m);
            return (w, squad, m, w.Man(Inside, ForceId.Hostile, health: 10f), w.Man(Inside, ForceId.Friend, health: 10f), w.Man(Inside, ForceId.Friend, health: 10f));
        }

        private static Entity CountOf(ObjWorld w, Entity squad, string what, string extra = "")
            => w.Objective("Count", $"{{{Cm(what, w.Net(squad), extra)},\"what\":\"{what}\"}}");

        /// <summary>P-3 assists — a hostile the man harmed, whose credit another source then took, that goes down within 10 s of the man's last harm is ONE assist (and not a kill for him); one that goes down
        /// after more than 10 s is nothing; a man who also gets the kill (harms it again) has no assist. ✅ Red-proof: <c>AssistSeconds</c> = 100 ⇒ the late one counts ⇒ red.</summary>
        [Fact]
        public void P3_Assists_WithinTenSeconds_NotLater_NotWithTheKill()
        {
            var (w, squad, m, foe, _, other) = ScoreWorld();
            var late = w.Man(Inside, ForceId.Hostile, health: 10f); var mine = w.Man(Inside, ForceId.Hostile, health: 10f);
            var assists = CountOf(w, squad, "assists"); var kills = CountOf(w, squad, "kills");
            w.Run(1);
            w.Harm(foe, m); w.Harm(late, m); w.Harm(mine, m); w.Tick();
            w.Harm(foe, other); w.Harm(late, other); w.Harm(mine, other); w.Tick();      // somebody else's harm: the credit is dropped
            w.Harm(mine, m); w.Tick();                                                   // …but he harms `mine` again: it is his kill, if it comes
            w.Kill(foe, other); w.Tick();                                                // ≈ 1.5 s after his harm
            Assert.Equal((1f, 0f), (w.S(assists).Value, w.S(kills).Value));
            w.Run(12);
            w.Kill(late, other); w.Kill(mine, m); w.Tick();                                                                   // `late` goes down > 10 s after his last harm; `mine` is his kill
            Assert.Equal((1f, 1f), (w.S(assists).Value, w.S(kills).Value));
            w.Tick(); w.Tick();
            Assert.Equal(1f, w.S(assists).Value);                                        // once
        }

        /// <summary>P-3 team kills — a friend the man harmed that goes down is ONE team kill, never a kill (fratricide stays the harm count); a corpse shot again is not a second one. A fault: lower is better,
        /// target 0 ⇒ score 0. ✅ Red-proof: the <c>Friends</c> settle line removed from <c>MeasureEvidence.Update</c> ⇒ 0 team kills ⇒ red.</summary>
        [Fact]
        public void P3_TeamKills_CountedOnce_NeverAKill()
        {
            var (w, squad, m, foe, pal, other) = ScoreWorld();
            var tk = CountOf(w, squad, "teamKills"); var kills = CountOf(w, squad, "kills"); var fr = CountOf(w, squad, "fratricide");
            w.Run(1);
            Assert.Equal((0f, 1f), (w.S(tk).Value, w.S(tk).Score));
            w.Harm(pal, m); w.Tick();
            w.Kill(pal, m); w.Tick();
            Assert.Equal((1f, 0f, 0f, 1f), (w.S(tk).Value, w.S(tk).Score, w.S(kills).Value, w.S(fr).Value));
            w.Harm(pal, m); w.Tick(); w.Tick();                                          // a corpse shot again
            Assert.Equal(1f, w.S(tk).Value);
            w.Harm(other, m); w.Tick(); SetHealth(w, other, 1f); w.Harm(foe, m); w.Tick();
            w.Kill(other, m); w.Tick();
            Assert.Equal((2f, 0f), (w.S(tk).Value, w.S(kills).Value));
        }

        /// <summary>P-3 deaths — his men going out of the fight, ONCE each; a man already down at the start is no death; a revived man can die again (2). Lower is better. ✅ Red-proof: the
        /// <c>InFight</c> list not pruned in <c>MeasureEvidence.Deaths</c> ⇒ a corpse counted every tick ⇒ red.</summary>
        [Fact]
        public void P3_Deaths_OncePerMan_ARevivedManCanDieAgain()
        {
            var w = new ObjWorld();
            var a = w.Man(Inside, ForceId.Friend, 10f); var b = w.Man(Inside, ForceId.Friend, 10f); var already = w.Man(Inside, ForceId.Friend, 10f);
            Down(w, already);
            var squad = w.Squad(ForceId.Friend, a, b, already);
            var deaths = CountOf(w, squad, "deaths");
            w.Run(1);
            Assert.Equal(0f, w.S(deaths).Value);
            Down(w, a); w.Run(2);
            Assert.Equal(1f, w.S(deaths).Value);
            Down(w, b); w.Run(2);
            Assert.Equal(2f, w.S(deaths).Value);
            SetHealth(w, a, 10f); w.Run(1);
            Assert.Equal(2f, w.S(deaths).Value);                                         // revived: nothing
            Down(w, a); w.Run(2);
            Assert.Equal(3f, w.S(deaths).Value);
            Assert.Equal(0f, w.S(deaths).Score);                                         // a fault, target 0
        }

        /// <summary>P-3 captures — a capture event naming one of his men is ONE capture for him; a squad whose man stood outside gets none. End to end: the real <c>HoldArea</c> publishes, the measure reads next
        /// frame. ✅ Red-proof: the captures loop removed from <c>MeasureEvidence.Update</c> ⇒ 0 ⇒ red.</summary>
        [Fact]
        public void P3_Captures_CreditedToAManInside_NotToOneOutside()
        {
            var w = new ObjWorld();
            var in1 = w.Man(Inside, ForceId.Friend, 10f); var in2 = w.Man(Inside, ForceId.Friend, 10f); var out1 = w.Man(Outside, ForceId.Friend, 10f);
            var squadIn = w.Squad(ForceId.Friend, in1, in2); var squadOut = w.Squad(ForceId.Friend, out1);
            var pt = w.Objective("HoldArea", $"{{{NoSubject},\"area\":{Circle0},\"minMen\":2,\"capture\":true,\"captureSeconds\":2}}");
            var cIn = CountOf(w, squadIn, "captures"); var cOut = CountOf(w, squadOut, "captures");
            w.Run(8);
            Assert.Equal(ForceId.Friend, w.S(pt).Owner);
            Assert.Equal((1f, 0f), (w.S(cIn).Value, w.S(cOut).Value));
            w.Run(4);
            Assert.Equal(1f, w.S(cIn).Value);                                            // once per event
        }

        /// <summary>P-2 — <c>Points</c>: Value = Σ scored children's Value × points (0 = ×1): kills ×100, team kills ×−200, an UNSCORED deaths count left out; Role Measure, Active; Score = Value / target. A
        /// <c>Points</c> child with points 0 counts ×1 into its parent. ✅ Red-proof: <c>ObjectiveChildren.PointsOf</c> not skipping <c>Scored == 0</c> ⇒ the deaths are added ⇒ red.</summary>
        [Fact]
        public void P2_Points_SumsScoredChildrenTimesPoints_NestedCountsOnce()
        {
            var (w, squad, m, foe, pal, other) = ScoreWorld();
            var f2 = w.Man(Inside, ForceId.Hostile, health: 10f); var f3 = w.Man(Inside, ForceId.Hostile, health: 10f); var dying = w.Man(Inside, ForceId.Friend, health: 10f);
            var mate = w.Squad(ForceId.Friend, dying);
            var root = w.Objective("Points", $"{{{Cm("Score")},\"target\":200}}");
            string under(string extra) => $",\"parent\":{w.Net(root)}{extra}";
            var kills = w.Objective("Count", $"{{{Cm("Kills", w.Net(squad), under(",\"points\":100"))},\"what\":\"kills\"}}");
            var tks = w.Objective("Count", $"{{{Cm("TK", w.Net(squad), under(",\"points\":-200"))},\"what\":\"teamKills\"}}");
            var deaths = w.Objective("Count", $"{{{Cm("Deaths", w.Net(mate), under(",\"scored\":false"))},\"what\":\"deaths\"}}");
            var inner = w.Objective("Points", $"{{{Cm("Inner", 0, under(""))}}}");
            var shots = w.Objective("Count", $"{{{Cm("Shots", w.Net(squad), $",\"parent\":{w.Net(inner)},\"points\":1")},\"what\":\"shots\"}}");
            w.Run(1);
            foreach (var victim in new[] { foe, f2, f3 }) w.Harm(victim, m);
            w.Harm(pal, m); Shoot(w, m, 4); w.Tick();
            foreach (var v in new[] { foe, f2, f3, pal }) w.Kill(v, m);
            Down(w, dying);                                                              // an own man going down by nobody's credited hit: a death, no kill
            w.Tick(); w.Tick();
            Assert.Equal((3f, 1f, 1f), (w.S(kills).Value, w.S(tks).Value, w.S(deaths).Value));
            Assert.Equal((4f, ObjectiveRole.Measure, ObjectiveState.Active), (w.S(inner).Value, w.S(inner).Role, w.S(inner).State));
            Assert.Equal(104f, w.S(root).Value);                                         // 3×100 − 200 + inner 4 (×1); deaths unscored
            Assert.Equal((ObjectiveRole.Measure, ObjectiveState.Active, "Points"), (w.S(root).Role, w.S(root).State, w.S(root).Kind.ToString()));
            Assert.Equal(0.52f, w.S(root).Score, 3);                                     // 104 of 200
            Assert.Equal("104 points", w.S(root).Reason.ToString());
        }

        /// <summary>⭐ CE-2384 (T3; DESIGN_Evaluation_Cost_And_Cadence §3.5) — 30 <c>Points</c> parents, 5 children each: the rules' tree is indexed ONCE per frame, however many
        /// aggregators read it (before, every <c>PointsOf</c> walked every rule). And every parent still sums its OWN children. ✅ Red-proof: <c>RuleTree.Refresh</c> rebuilding on
        /// every call ⇒ ~30 builds a frame ⇒ red.</summary>
        [Fact]
        public void CE2384_TheRuleTree_IsIndexedOncePerFrame_AndEachParentSumsItsOwnChildren()
        {
            var (w, squad, m, foe, pal, other) = ScoreWorld();
            var roots = new Entity[30];
            for (int r = 0; r < roots.Length; r++)
            {
                roots[r] = w.Objective("Points", $"{{{Cm("Score " + r)},\"target\":100}}");
                for (int c = 0; c < 5; c++)
                    w.Objective("Count", $"{{{Cm("Shots " + r + "." + c, w.Net(squad), $",\"parent\":{w.Net(roots[r])},\"points\":{r + 1}")},\"what\":\"shots\"}}");
            }
            w.Run(1);
            Shoot(w, m, 1); w.Repo.Tick(); w.Tick();
            int before = Hrot.AI.Behaviors.Brains.Objectives.RuleTree.BuildsOf(w.Repo);
            for (int f = 0; f < 3; f++) { w.Repo.Tick(); w.Tick(); }                     // three frames, 30 aggregators each
            Assert.Equal(3, Hrot.AI.Behaviors.Brains.Objectives.RuleTree.BuildsOf(w.Repo) - before);
            for (int r = 0; r < roots.Length; r++) Assert.Equal(5f * (r + 1), w.S(roots[r]).Value);   // 5 children × 1 shot × (r+1) points — its own, nobody else's
        }

        private static void EndTheExercise(ObjWorld w)
        {
            w.Environment();
            var src = Cond(w);
            Fire(w, src, Do(Step("EndExercise", "{\"reason\":\"done\"}")));
            Flip(w, src, true);
        }

        /// <summary>P-5 — <c>members: side</c>: every man of the subject's side gets an instance as he APPEARS, whoever his unit (a respawned bot is in no roster) — and a hostile, a unit and an
        /// objective do not; <c>roster</c> (the default) still serves the roster only. <c>aggregate: points</c>: Value = Σ the instances' Value, the state stays Active however the instances fare, and
        /// it is Achieved (not "Failed: not achieved by the end") once the exercise closed and every instance has. ✅ Red-proof: <c>Serve</c> always walking the roster ⇒ the respawned man gets none ⇒ red.</summary>
        [Fact]
        public void P5_MembersBySide_ServesARespawnedMan_AggregatePointsSums()
        {
            var w = EvalWorld(Lib("Default"));
            var a = Soldier(w, new Vector3(1, 1, 0)); var squad = w.Squad(ForceId.Friend, a);
            w.Repo.AddComponent(squad, new EntityGuid(Guid.NewGuid()));
            var hostile = Soldier(w, new Vector3(2, 2, 0)); w.Repo.GetComponentRW<EntityInfo>(hostile).ForceId = ForceId.Hostile;
            var evalSide = w.Objective("MemberEvaluation", $"{{{Cm("E", w.Net(squad))},\"library\":\"{EvalLibrary}\",\"members\":\"side\",\"aggregate\":\"points\"}}");
            w.Repo.AddComponent(evalSide, new EntityGuid(Guid.NewGuid()));
            w.Run(3);
            Assert.Equal(new[] { GuidOf(w, a).ToString("D") }, w.Created.Select(r => ArgsOf(r)["subject"]!.GetValue<string>()).ToArray());   // the roster man (the hostile, the unit, the objective: none)

            var respawned = Soldier(w, new Vector3(9, 9, 0));                            // in NO roster
            w.Run(3);
            var subjects = w.Created.Select(r => ArgsOf(r)["subject"]!.GetValue<string>()).ToHashSet();
            Assert.Equal(new[] { GuidOf(w, a), GuidOf(w, respawned) }.Select(g => g.ToString("D")).OrderBy(x => x).ToArray(), subjects.OrderBy(x => x).ToArray());
            Assert.Equal(2, w.Created.Count);                                            // asked once each

            // the roster mode, same world: the respawned man is invisible to it
            var evalRoster = Evaluation(w, squad);
            w.Run(3);
            Assert.Equal(3, w.Created.Count);                                            // + only `a`

            // aggregate points: the instances' Values sum; the verdict is never Failed
            var m1 = w.Objective("Count", $"{{{Cm("a", w.Net(squad), $",\"parent\":{w.Net(evalSide)}")},\"what\":\"shots\"}}");
            var squad2 = w.Squad(ForceId.Friend, respawned);
            var m2 = w.Objective("Count", $"{{{Cm("b", w.Net(squad2), $",\"parent\":{w.Net(evalSide)}")},\"what\":\"shots\"}}");
            var failing = w.Objective("ReachArea", $"{{{Cm("r", w.Net(squad2), $",\"parent\":{w.Net(evalSide)},\"deadlineSeconds\":1")},\"area\":{{\"circle\":{{\"center\":[500,500,0],\"radius\":1}}}}}}");
            w.Run(1);
            Shoot(w, a, 3); Shoot(w, respawned, 2); w.Tick(); w.Tick();
            Assert.Equal(ObjectiveState.Failed, w.S(failing).State);
            Assert.Equal((5f, ObjectiveState.Active, ObjectiveRole.Objective), (w.S(evalSide).Value, w.S(evalSide).State, w.S(evalSide).Role));
            Assert.Contains("5 points", w.S(evalSide).Reason.ToString());

            EndTheExercise(w);
            w.Run(6);
            Assert.Equal((ObjectiveState.Achieved, (byte)1, 5f), (w.S(evalSide).State, w.S(evalSide).Closed, w.S(evalSide).Value));
        }

        /// <summary>P-7 — <c>Best</c>: among the measures titled <c>measure</c> under <c>among</c>, the highest value (≥ <c>min</c>) wins; while the exercise runs it is Active and names nobody; at the close it is
        /// Achieved with <c>ObjectiveLinks.Subject</c> = the winner's man. <c>lowerIsBetter</c> picks the lowest (≤ min); nobody passing <c>min</c> ⇒ Failed "nobody qualified". ✅ Red-proof: the winner chosen
        /// by the FIRST candidate instead of the best (the <c>c.Value &gt; value</c> test dropped) ⇒ the wrong man ⇒ red.</summary>
        [Fact]
        public void P7_Best_TheBestMeasureWins_TheWinnerIsTheSubject_LowerIsBetter_MinFloor()
        {
            var w = new ObjWorld();
            var men = new[] { w.Man(Inside, ForceId.Friend, 10f), w.Man(Inside, ForceId.Friend, 10f), w.Man(Inside, ForceId.Friend, 10f) };
            var root = w.Objective("WeightedMean", $"{{{NoSubject}}}");
            var shots = men.Select(m => w.Objective("Count", $"{{{Cm("Shots", w.Net(m), $",\"parent\":{w.Net(root)}")},\"what\":\"shots\"}}")).ToArray();
            var fewest = men.Select(m => w.Objective("Count", $"{{{Cm("Hits", w.Net(m), $",\"parent\":{w.Net(root)}")},\"what\":\"hits\"}}")).ToArray();
            string best(string title, string extra) => $"{{{Cm("Best " + title, 0, $",\"parent\":{w.Net(root)},\"scored\":false")},\"among\":{w.Net(root)},\"measure\":\"{title}\"{extra}}}";
            var top = w.Objective("Best", best("Shots", ",\"min\":1"));
            var low = w.Objective("Best", best("Shots", ",\"lowerIsBetter\":true,\"min\":9"));
            var none = w.Objective("Best", best("Shots", ",\"min\":50"));
            var noMeasure = w.Objective("Best", best("Nothing", ""));
            w.Run(1);
            Shoot(w, men[0], 4); Shoot(w, men[1], 9); Shoot(w, men[2], 2);
            w.Run(2);
            Assert.Equal((ObjectiveState.Active, 9f), (w.S(top).State, w.S(top).Value));       // the leader shows while it runs…
            Assert.True(w.Repo.GetComponentRO<ObjectiveLinks>(top).Subject.IsNone);          // …but nobody is named until the close
            EndTheExercise(w);
            w.Run(6);
            Assert.Equal((ObjectiveState.Achieved, (byte)1, 9f), (w.S(top).State, w.S(top).Closed, w.S(top).Value));
            Assert.Equal(men[1], w.Repo.GetComponentRO<ObjectiveLinks>(top).Subject.Resolve(w.Repo));   // the winner is a READ of the link
            Assert.Contains("9", w.S(top).Reason.ToString());
            Assert.Equal((ObjectiveState.Achieved, 2f), (w.S(low).State, w.S(low).Value));      // lowest that is ≤ 9: man 2 with 2 shots
            Assert.Equal(men[2], w.Repo.GetComponentRO<ObjectiveLinks>(low).Subject.Resolve(w.Repo));
            Assert.Equal(("nobody qualified", ObjectiveState.Failed), (w.S(none).Reason.ToString(), w.S(none).State));
            Assert.Equal(ObjectiveState.Failed, w.S(noMeasure).State);                          // no such measure at all
            Assert.Equal((byte)1, w.S(none).Closed);
            Assert.True(w.Repo.GetComponentRO<ObjectiveLinks>(none).Subject.IsNone);
        }

        private static Entity Named(ObjWorld w, Entity e, string name) { w.Repo.GetComponentRW<EntityInfo>(e).Name = new FixedString64(name); return e; }

        /// <summary>P-8a — <c>streak</c> / <c>bestStreak</c>: kills by his men since the subject last LOST a man, read from the ManDownedEvents in order. 3 kills ⇒ 3 / 3; one of his men downed ⇒ 0 / 3; two more ⇒ 2 / 3.
        /// ✅ Red-proof: the <c>IsHis … Streak = 0</c> reset removed from <c>MeasureEvidence.Update</c> ⇒ the streak goes on to 5 ⇒ red.</summary>
        [Fact]
        public void P8a_Streak_CountsKillsSinceHeLostAMan_BestStreakStays()
        {
            var w = new ObjWorld();
            var m = w.Man(Inside, ForceId.Friend, 10f); var m2 = w.Man(Inside, ForceId.Friend, 10f); var squad = w.Squad(ForceId.Friend, m, m2);
            var foes = Enumerable.Range(0, 6).Select(_ => w.Man(Inside, ForceId.Hostile, 10f)).ToArray();
            var streak = CountOf(w, squad, "streak"); var best = CountOf(w, squad, "bestStreak");
            w.Run(1);
            for (int i = 0; i < 3; i++) { w.Kill(foes[i], m); w.Tick(); }
            Assert.Equal((3f, 3f), (w.S(streak).Value, w.S(best).Value));
            w.Kill(m, foes[3]); w.Tick();                                                   // one of HIS men is downed
            Assert.Equal((0f, 3f), (w.S(streak).Value, w.S(best).Value));
            Assert.Equal(1f, w.S(streak).Target);                                           // higher is better: not a fault (a fault's target is 0)
            for (int i = 4; i < 6; i++) { w.Kill(foes[i], m2); w.Tick(); }
            Assert.Equal((2f, 3f), (w.S(streak).Value, w.S(best).Value));
        }

        private static (Entity Bounty, Entity Streak) BountyOn(ObjWorld w, Entity man, int min = 5)
        {
            var streak = w.Objective("Count", $"{{{Cm("Streak", w.Net(man))},\"what\":\"streak\"}}");
            var bounty = w.Objective("Bounty", $"{{{Cm("Bounty", w.Net(man), ",\"scored\":false")},\"streak\":{w.Net(streak)},\"minStreak\":{min}}}");
            return (bounty, streak);
        }

        /// <summary>P-8b — <c>Bounty</c>: starts Pending; Active when the linked streak reaches 5 and he is in the fight; his downing claims it (Pending, FireCount 1, LastFired, "claimed by …"); it is Active again only
        /// once the streak went below 5 and back to 5 (FireCount stays 1 until the next claim); below 5 it stays Pending. At the close: Achieved if it stands, else Skipped. ✅ Red-proof: the claim branch
        /// (<c>wasActive &amp;&amp; Claimed</c>) removed ⇒ it never goes Pending ⇒ red.</summary>
        [Fact]
        public void P8b_Bounty_ActiveWhileItStands_ClaimedByHisDowning_ReArmedByANewStreak_ClosesByIt()
        {
            var w = new ObjWorld();
            var hawk = Named(w, w.Man(Inside, ForceId.Friend, 10f), "Hawk"); var raven = Named(w, w.Man(Inside, ForceId.Hostile, 10f), "Raven");
            var foes = Enumerable.Range(0, 12).Select(_ => w.Man(Inside, ForceId.Hostile, 10f)).ToArray();
            var (bounty, streak) = BountyOn(w, hawk);
            w.Run(1);
            Assert.Equal((ObjectiveState.Pending, 0), (w.S(bounty).State, w.S(bounty).FireCount));
            int k = 0;
            for (; k < 4; k++) { w.Kill(foes[k], hawk); w.Tick(); }
            w.Run(1);
            Assert.Equal(ObjectiveState.Pending, w.S(bounty).State);                           // 4 of 5
            w.Kill(foes[k++], hawk); w.Run(1.5);
            Assert.Equal((ObjectiveState.Active, 5f), (w.S(bounty).State, w.S(streak).Value));
            w.Kill(hawk, raven); w.Run(1.5);                                                   // Hawk is downed by Raven
            Assert.Equal((ObjectiveState.Pending, 1), (w.S(bounty).State, w.S(bounty).FireCount));
            Assert.True(w.S(bounty).LastFired > 0);
            Assert.Equal("claimed by Raven", w.S(bounty).Reason.ToString());
            w.Repo.GetComponentRW<Health>(hawk).Current = 10f; w.Run(1);                       // revived
            for (int i = 0; i < 4; i++) { w.Kill(foes[k++], hawk); w.Tick(); }
            w.Run(1);
            Assert.Equal((ObjectiveState.Pending, 4f), (w.S(bounty).State, w.S(streak).Value));   // below 5 stays Pending
            w.Kill(foes[k++], hawk); w.Run(1.5);
            Assert.Equal((ObjectiveState.Active, 1), (w.S(bounty).State, w.S(bounty).FireCount));   // a new streak of 5: it stands again, FireCount unchanged
            EndTheExercise(w);
            w.Run(6);
            Assert.Equal((ObjectiveState.Achieved, (byte)1), (w.S(bounty).State, w.S(bounty).Closed));   // it still stood at the end

            var w2 = new ObjWorld();
            var nobody = w2.Man(Inside, ForceId.Friend, 10f);
            var (b2, _) = BountyOn(w2, nobody);
            w2.Run(2); EndTheExercise(w2); w2.Run(6);
            Assert.Equal((ObjectiveState.Skipped, (byte)1), (w2.S(b2).State, w2.S(b2).Closed));    // never stood
        }

        private static void ManDown(ObjWorld w, Entity victim, Entity source, ForceId side)
            => w.Repo.Bus.Publish(new Fdp.Toolkit.Combat.Events.ManDownedEvent { Victim = victim, Source = source, VictimSide = side });

        /// <summary>P-8b′ — his man leaves the fight ONE TICK BEFORE his <c>ManDownedEvent</c> arrives: the bounty stays Active "down — awaiting the downing" and the event still claims it (FireCount 1, "claimed by …"); with no
        /// event within 3 s it goes Pending "down, no claim" (FireCount stays 0). ✅ Red-proof: the wait branch removed (it drops to Pending at once) ⇒ the late event cannot claim ⇒ red.</summary>
        [Fact]
        public void P8b2_ABountyWhoseManIsSeenDownFirst_WaitsForTheEvent_ThreeSeconds()
        {
            var w = new ObjWorld();
            var hawk = Named(w, w.Man(Inside, ForceId.Friend, 10f), "Hawk"); var raven = Named(w, w.Man(Inside, ForceId.Hostile, 10f), "Raven");
            var foes = Enumerable.Range(0, 3).Select(_ => w.Man(Inside, ForceId.Hostile, 10f)).ToArray();
            var (bounty, _) = BountyOn(w, hawk, min: 3);
            w.Run(1);
            foreach (var f in foes) { w.Kill(f, hawk); w.Tick(); }
            w.Run(1.5);
            Assert.Equal(ObjectiveState.Active, w.S(bounty).State);
            SetHealth(w, hawk, 0f); w.Tick();                                                  // seen down — no event yet
            Assert.Equal((ObjectiveState.Active, "down — awaiting the downing", 0), (w.S(bounty).State, w.S(bounty).Reason.ToString(), w.S(bounty).FireCount));
            ManDown(w, hawk, raven, ForceId.Friend); w.Tick();                                 // the event, a tick late
            Assert.Equal((ObjectiveState.Pending, 1, "claimed by Raven"), (w.S(bounty).State, w.S(bounty).FireCount, w.S(bounty).Reason.ToString()));

            var w2 = new ObjWorld();
            var h2 = w2.Man(Inside, ForceId.Friend, 10f);
            var f2 = Enumerable.Range(0, 3).Select(_ => w2.Man(Inside, ForceId.Hostile, 10f)).ToArray();
            var (b2, _) = BountyOn(w2, h2, min: 3);
            w2.Run(1);
            foreach (var f in f2) { w2.Kill(f, h2); w2.Tick(); }
            w2.Run(1.5);
            SetHealth(w2, h2, 0f); w2.Run(2);
            Assert.Equal(ObjectiveState.Active, w2.S(b2).State);                               // still waiting inside the 3 s
            w2.Run(2);
            Assert.Equal((ObjectiveState.Pending, "down, no claim", 0), (w2.S(b2).State, w2.S(b2).Reason.ToString(), w2.S(b2).FireCount));
        }

        /// <summary>P-8c′ — the killer's <c>bountyKills</c> counts a kill whose bounty man was seen down a tick before the event (the bounty still waits, Active); a kill whose event comes after the 3 s wait (Pending "no claim") does not.
        /// ✅ Red-proof: the wait branch removed ⇒ the bounty is Pending when the event arrives ⇒ BountyKills 0 ⇒ red.</summary>
        [Fact]
        public void P8c2_BountyKills_CountsAKillWhoseEventArrivesWhileTheBountyWaits_NotAfterTheWait()
        {
            foreach (bool late in new[] { false, true })
            {
                var w = new ObjWorld();
                var hawk = Named(w, w.Man(Inside, ForceId.Hostile, 10f), "Hawk"); var dummy = w.Man(Inside, ForceId.Friend, 10f); var killer = w.Man(Inside, ForceId.Friend, 10f);
                var streak = w.Objective("Count", $"{{{Cm("Streak", w.Net(hawk))},\"what\":\"streak\"}}");
                var bounty = w.Objective("Bounty", $"{{{Cm("Bounty", w.Net(hawk), ",\"scored\":false")},\"streak\":{w.Net(streak)},\"minStreak\":1}}");
                var bk = w.Objective("Count", $"{{{Cm("BK", w.Net(killer))},\"what\":\"bountyKills\"}}");
                w.Run(1);
                w.Kill(dummy, hawk); w.Run(1.5);
                Assert.Equal(ObjectiveState.Active, w.S(bounty).State);
                SetHealth(w, hawk, 0f); w.Tick();                                              // seen down first
                if (late) w.Run(4);                                                            // the wait runs out: Pending "down, no claim"
                ManDown(w, hawk, killer, ForceId.Hostile); w.Run(1.5);
                Assert.Equal(late ? 0f : 1f, w.S(bk).Value);
                if (late) Assert.Equal("down, no claim", w.S(bounty).Reason.ToString());
            }
        }

        /// <summary>P-8c — <c>bountyKills</c>: a kill whose victim's <c>Bounty</c> is Active (or was claimed at this very time) counts, whichever of the bounty and the count ticks first in the frame; a kill of a man with no
        /// bounty does not. ✅ Red-proof: the <c>b.LastFired == now</c> clause removed from <c>BountyStands</c> ⇒ the bounty-ticks-first order misses it ⇒ red.</summary>
        [Fact]
        public void P8c_BountyKills_CreditAKillOnABountyMan_InEitherTickOrder_NotOnAnyoneElse()
        {
            foreach (bool countFirst in new[] { true, false })
            {
                var w = new ObjWorld();
                var hawk = Named(w, w.Man(Inside, ForceId.Hostile, 10f), "Hawk"); var plain = w.Man(Inside, ForceId.Hostile, 10f);
                var dummy = w.Man(Inside, ForceId.Friend, 10f); var killer = w.Man(Inside, ForceId.Friend, 10f);
                Entity count = default, bounty = default, streak;
                if (countFirst) count = w.Objective("Count", $"{{{Cm("BK", w.Net(killer))},\"what\":\"bountyKills\"}}");
                streak = w.Objective("Count", $"{{{Cm("Streak", w.Net(hawk))},\"what\":\"streak\"}}");
                bounty = w.Objective("Bounty", $"{{{Cm("Bounty", w.Net(hawk), ",\"scored\":false")},\"streak\":{w.Net(streak)},\"minStreak\":1}}");
                if (!countFirst) count = w.Objective("Count", $"{{{Cm("BK", w.Net(killer))},\"what\":\"bountyKills\"}}");
                var kills = w.Objective("Count", $"{{{Cm("K", w.Net(killer))},\"what\":\"kills\"}}");
                w.Run(1);
                w.Kill(dummy, hawk); w.Run(1.5);                                              // Hawk's streak is 1 ⇒ the bounty stands
                Assert.Equal(ObjectiveState.Active, w.S(bounty).State);
                w.Kill(hawk, killer); w.Run(1.5);
                Assert.Equal((1f, 1f, ObjectiveState.Pending), (w.S(count).Value, w.S(kills).Value, w.S(bounty).State));
                w.Kill(plain, killer); w.Run(1.5);
                Assert.Equal((1f, 2f), (w.S(count).Value, w.S(kills).Value));                  // a man with no bounty: a kill, not a bounty kill
            }
        }

        /// <summary>P-8d — <c>{subject}</c> in a <c>Message</c> is the name of the sending trigger's subject (empty name ⇒ "#netid"). ✅ Red-proof: the replace removed from <c>ScenarioVerbs.WithSubject</c> ⇒ the text keeps
        /// the literal <c>{subject}</c> ⇒ red.</summary>
        [Fact]
        public void P8d_Message_SubjectIsTheTriggersSubjectsName()
        {
            var w = new ObjWorld();
            var hawk = Named(w, w.Man(Inside, ForceId.Friend), "Hawk"); var anon = w.Man(Inside, ForceId.Friend);
            w.Environment();
            var src = Cond(w);
            string json(Entity who) => "{\"common\":{\"title\":\"T\",\"subject\":" + w.Net(who) + "},\"when\":" + WhenAchieved(w.Net(src)) + ",\"do\":"
                + Do(Step("Message", "{\"toKind\":\"All\",\"text\":\"Bounty on {subject}!\"}")) + ",\"repeat\":true}";
            var t1 = w.Trigger(json(hawk)); var t2 = w.Trigger(json(anon));
            w.Run(1.5);
            Flip(w, src, true); w.Run(1.5);
            Assert.Equal(new[] { "Bounty on #" + w.Net(anon) + "!", "Bounty on Hawk!" }, w.Messages.Select(m => m.Text).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        /// <summary>S7 ⑤ — <c>Best.also</c> (the Untouchable's kills floor): among "Deaths" (lowerIsBetter, ≤ 0) a candidate qualifies only if his SIBLING "Kills" (same parent) is ≥ 5: 0 kills / 0 deaths does NOT win, 5 kills / 0
        /// deaths does, 5 kills / 1 death does not (deaths). ✅ Red-proof: the sibling check ignored in <c>Find</c> ⇒ the idle man wins ⇒ red.</summary>
        [Fact]
        public void S7_5_Best_Also_AKillsFloorOnTheSiblingMeasure()
        {
            var w = new ObjWorld();
            var root = w.Objective("WeightedMean", $"{{{NoSubject}}}");
            var idle = w.Man(Inside, ForceId.Friend, 10f); var ace = w.Man(Inside, ForceId.Friend, 10f); var fallen = w.Man(Inside, ForceId.Friend, 10f);
            var foes = Enumerable.Range(0, 11).Select(_ => w.Man(Inside, ForceId.Hostile, 10f)).ToArray();
            var killer = w.Man(Inside, ForceId.Hostile, 10f);
            foreach (var m in new[] { idle, ace, fallen })
            {
                var group = w.Objective("WeightedMean", $"{{{Cm("Score " + w.Net(m), 0, $",\"parent\":{w.Net(root)}")}}}");
                w.Objective("Count", $"{{{Cm("Kills", w.Net(m), $",\"parent\":{w.Net(group)}")},\"what\":\"kills\"}}");
                w.Objective("Count", $"{{{Cm("Deaths", w.Net(m), $",\"parent\":{w.Net(group)}")},\"what\":\"deaths\"}}");
            }
            var award = w.Objective("Best", $"{{{Cm("Untouchable", 0, $",\"parent\":{w.Net(root)},\"scored\":false")},\"among\":{w.Net(root)},\"measure\":\"Deaths\",\"min\":0,\"lowerIsBetter\":true,\"also\":{{\"measure\":\"Kills\",\"min\":5}}}}");
            w.Run(1);
            for (int i = 0; i < 5; i++) { w.Kill(foes[i], ace); w.Tick(); w.Kill(foes[5 + i], fallen); w.Tick(); }
            w.Kill(fallen, killer); w.Run(2);
            EndTheExercise(w);
            w.Run(6);
            Assert.Equal((ObjectiveState.Achieved, (byte)1), (w.S(award).State, w.S(award).Closed));
            Assert.Equal(ace, w.Repo.GetComponentRO<ObjectiveLinks>(award).Subject.Resolve(w.Repo));   // 5 kills / 0 deaths; the idle man (0 / 0) has no kills floor, the fallen one a death

            var w2 = new ObjWorld();
            var root2 = w2.Objective("WeightedMean", $"{{{NoSubject}}}");
            var lazy = w2.Man(Inside, ForceId.Friend, 10f);
            var g2 = w2.Objective("WeightedMean", $"{{{Cm("Score", 0, $",\"parent\":{w2.Net(root2)}")}}}");
            w2.Objective("Count", $"{{{Cm("Kills", w2.Net(lazy), $",\"parent\":{w2.Net(g2)}")},\"what\":\"kills\"}}");
            w2.Objective("Count", $"{{{Cm("Deaths", w2.Net(lazy), $",\"parent\":{w2.Net(g2)}")},\"what\":\"deaths\"}}");
            var a2 = w2.Objective("Best", $"{{{Cm("Untouchable", 0, $",\"parent\":{w2.Net(root2)},\"scored\":false")},\"among\":{w2.Net(root2)},\"measure\":\"Deaths\",\"min\":0,\"lowerIsBetter\":true,\"also\":{{\"measure\":\"Kills\",\"min\":5}}}}");
            w2.Run(2); EndTheExercise(w2); w2.Run(6);
            Assert.Equal((ObjectiveState.Failed, "nobody qualified"), (w2.S(a2).State, w2.S(a2).Reason.ToString()));
        }
    }
}
