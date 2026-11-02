using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.AI.Behaviors.Brains.Objectives;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S6 (📄 docs/DESIGN_Scenario_Rules.md §00.12 G-1 … G-4) — the game profile v1: a point taken BY SIDE with a clock, the <c>Counter</c> measure,
    /// the <c>AddToCounter</c> verb and the trigger's <c>whileTrue</c>. Through the real load path, like the S1/S2 rails.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        private const string NoSubject = "\"common\":{\"title\":\"T\"}";

        private static Entity MenIn(ObjWorld w, Vector3 at, ForceId side, int n)
        {
            Entity last = default;
            for (int i = 0; i < n; i++) last = w.Man(at, side);
            return last;
        }

        /// <summary>G-1 — <c>captureSeconds</c> 10: 3 Friend vs 1 Hostile inside (by SIDE, no subject) ⇒ Owner stays Neutral before 10 s of advantage and is Friend after; then
        /// 4 Hostile vs 3 Friend ⇒ the point is lost AT ONCE (Neutral), and Hostile owns it after 10 more seconds. ✅ Red-proof: the "outnumbered owner ⇒ Neutral" lines removed from
        /// <c>HoldAreaBehavior.CaptureBySide</c> ⇒ the Owner stays Friend ⇒ red.</summary>
        [Fact]
        public void G1_Capture_OutnumberFor10s_ThenLostWhenOutnumbered()
        {
            var w = new ObjWorld();
            MenIn(w, Inside, ForceId.Friend, 3);
            MenIn(w, Inside, ForceId.Hostile, 1);
            var pt = w.Objective("HoldArea", $"{{{NoSubject},\"area\":{Circle0},\"minMen\":2,\"capture\":true,\"captureSeconds\":10}}");
            w.Run(9);
            Assert.Equal((ObjectiveState.Active, ForceId.Neutral), (w.S(pt).State, w.S(pt).Owner));   // 3 v 1 — the clock is running, not done
            Assert.Equal(10, w.S(pt).ProgressNeed);
            Assert.InRange(w.S(pt).ProgressCur, 7, 9);
            w.Run(4);
            Assert.Equal((ObjectiveState.Active, ForceId.Friend), (w.S(pt).State, w.S(pt).Owner));
            Assert.Contains("owner Friend", w.S(pt).Reason.ToString());

            MenIn(w, Inside, ForceId.Hostile, 3);                      // 4 Hostile v 3 Friend
            w.Run(0.5);
            Assert.Equal(ForceId.Neutral, w.S(pt).Owner);              // lost at once
            w.Run(9);
            Assert.Equal(ForceId.Neutral, w.S(pt).Owner);              // Hostile's clock is not done
            w.Run(2);
            Assert.Equal(ForceId.Hostile, w.S(pt).Owner);

            // captureSeconds 0 = the S1 rule: instant, the SUBJECT's roster (the dedicated S1 rail pins the rest)
            var c = new ObjWorld();
            var blue = new[] { c.Man(Inside, ForceId.Friend), c.Man(Inside, ForceId.Friend) };
            var old = c.Objective("HoldArea", $"{{{Common(c, c.Squad(ForceId.Friend, blue))},\"area\":{Circle0},\"minMen\":2,\"capture\":true,\"captureSeconds\":0}}");
            c.Run(3);
            Assert.Equal(ForceId.Friend, c.S(old).Owner);
        }

        private static string CounterJson(float start, string deaths = "", string extra = "")
            => $"{{{NoSubject},\"start\":{start}{(deaths.Length > 0 ? ",\"deaths\":" + deaths : "")}{extra}}}";

        /// <summary>G-2 — a tickets counter (<c>Friend</c> per −1) and a two-sided lead counter: Value = start, Role Measure, Target = start (score = value / start); each man newly OUT of the fight
        /// takes his side's <c>per</c>; men already down at the start are no deaths; a revive refunds nothing; a removed corpse does not hide the next death; the floor holds. ✅ Red-proof: the
        /// floor clamp removed from <c>CounterBehavior.Tick</c> ⇒ the pool goes to −1 ⇒ red.</summary>
        [Fact]
        public void G2_Counter_StartsAtStart_DeathsOfItsSideTakeOne_ARevivedManNeverRefunds_Floor()
        {
            var w = new ObjWorld();
            var f = new Entity[4];
            for (int i = 0; i < 4; i++) f[i] = w.Man(Inside, ForceId.Friend, health: 10f);
            var h = w.Man(Inside, ForceId.Hostile, health: 10f);
            w.Repo.GetComponentRW<Health>(f[3]).Current = 0f;            // down before the count begins: no death of the counter's
            var tickets = w.Objective("Counter", CounterJson(10, "[{\"side\":\"Friend\",\"per\":-1}]"));
            var lead = w.Objective("Counter", CounterJson(0, "[{\"side\":\"Hostile\",\"per\":1},{\"side\":\"Friend\",\"per\":-1}]", ",\"floor\":-100,\"ceiling\":3"));
            var pool2 = w.Objective("Counter", CounterJson(2, "[{\"side\":\"Friend\",\"per\":-1}]"));
            w.Run(2);
            Assert.Equal((ObjectiveRole.Measure, "Counter", 10f, 10f, 1f), (w.S(tickets).Role, w.S(tickets).Kind.ToString(), w.S(tickets).Value, w.S(tickets).Target, w.S(tickets).Score));
            Assert.Equal("10 of 10", w.S(tickets).Reason.ToString());
            Assert.Equal(ObjectiveState.Active, w.S(tickets).State);

            w.Repo.GetComponentRW<Health>(f[0]).Current = 0f; w.Repo.GetComponentRW<Health>(f[1]).Current = 0f;
            w.Run(1);
            Assert.Equal(8f, w.S(tickets).Value);
            Assert.Equal(0.8f, w.S(tickets).Score, 3);
            Assert.Equal(-2f, w.S(lead).Value);
            w.Repo.GetComponentRW<Health>(h).Current = 0f;               // the other side's death moves ONLY the lead counter
            w.Run(1);
            Assert.Equal((8f, -1f), (w.S(tickets).Value, w.S(lead).Value));

            w.Repo.GetComponentRW<Health>(f[0]).Current = 10f;           // revived: nothing is refunded
            w.Run(1);
            Assert.Equal((8f, -1f), (w.S(tickets).Value, w.S(lead).Value));
            w.Repo.DestroyEntity(f[1]);                                  // a corpse cleaned up…
            w.Run(1);
            Assert.Equal(8f, w.S(tickets).Value);
            w.Repo.GetComponentRW<Health>(f[2]).Current = 0f;            // …and the next man down still counts
            w.Run(1);
            Assert.Equal(7f, w.S(tickets).Value);
            Assert.Equal(-2f, w.S(lead).Value);

            Assert.Equal(0f, w.S(pool2).Value);                          // 2 tickets, 3 Friend downs seen in all ⇒ the floor (0), never −1
            Assert.True(w.S(pool2).Value >= 0f);
        }

        /// <summary>G-3 — <c>AddToCounter</c>: adds to a Counter's value (negative drains), clamps at its ceiling at the counter's next tick; refuses a target that is no Counter (the trigger
        /// ends Failed with the reason) or does not exist. ✅ Red-proof: the <c>Kind</c> check removed from <c>ScenarioVerbs.AddToCounter</c> ⇒ the HoldArea target is "added to" ⇒ red.</summary>
        [Fact]
        public void G3_AddToCounter_MovesTheValue_RefusesANonCounter()
        {
            var w = new ObjWorld();
            var pool = w.Objective("Counter", CounterJson(10, "", ",\"ceiling\":12"));
            var hold = w.Objective("HoldArea", $"{{{NoSubject},\"area\":{Circle0},\"capture\":true}}");
            w.Run(2);

            var cA = Cond(w); var plus = Fire(w, cA, Do(Step("AddToCounter", $"{{\"target\":{w.Net(pool)},\"amount\":-3}}")));
            Flip(w, cA, true); w.Run(1);
            Assert.Equal(TriggerState.Done, Phase(w, plus));
            Assert.Equal(7f, w.S(pool).Value);
            Assert.Equal("7 of 10", w.S(pool).Reason.ToString());

            var cB = Cond(w); var big = Fire(w, cB, Do(Step("AddToCounter", $"{{\"target\":{w.Net(pool)},\"amount\":50}}")));
            Flip(w, cB, true); w.Run(1);
            Assert.Equal(TriggerState.Done, Phase(w, big));
            Assert.Equal(12f, w.S(pool).Value);                          // the counter's ceiling holds it

            var cC = Cond(w); var bad = Fire(w, cC, Do(Step("AddToCounter", $"{{\"target\":{w.Net(hold)},\"amount\":5}}")));
            Flip(w, cC, true); w.Run(1);
            Assert.Equal(ObjectiveState.Failed, w.S(bad).State);
            Assert.Contains("not a Counter", w.S(bad).Reason.ToString());
            Assert.Equal(0f, w.S(hold).Value);

            var cD = Cond(w); var ghost = Fire(w, cD, Do(Step("AddToCounter", "{\"target\":99999,\"amount\":1}")));
            Flip(w, cD, true); w.Run(1);
            Assert.Equal(ObjectiveState.Failed, w.S(ghost).State);
            Assert.Contains("target not found", w.S(ghost).Reason.ToString());
        }

        /// <summary>G-4 — <c>whileTrue</c>: a repeating trigger whose condition is still true when its cooldown ends fires again (every cooldown while it holds); <c>maxFires</c> caps it; a
        /// condition that dropped by the end of the cooldown only re-arms. Without <c>whileTrue</c> the T2 rule holds (it fires once while the condition stays true). ✅ Red-proof: the
        /// <c>WhileTrue</c> branch removed from <c>TriggerBehavior.Step</c>'s Cooldown arm ⇒ fires once ⇒ red.</summary>
        [Fact]
        public void G4_WhileTrue_FiresEveryCooldownWhileTheConditionHolds_MaxFiresCaps()
        {
            TrgActions.Reset();
            var w = new ObjWorld(); var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"repeat\":true,\"cooldown\":3,\"maxFires\":3,\"whileTrue\":true"));
            w.Run(1.5);
            Flip(w, src, true); w.Run(0.5);                                    // the edge: fire 1
            Assert.Equal(1, TrgActions.Calls.Count);
            w.Run(2);
            Assert.Equal((1, TriggerState.Cooldown), (TrgActions.Calls.Count, Phase(w, t)));   // the cooldown is not over
            w.Run(2);
            Assert.Equal(2, TrgActions.Calls.Count);                           // still true after the cooldown ⇒ fire 2, with no drop and rise
            w.Run(4);
            Assert.Equal(3, TrgActions.Calls.Count);                           // fire 3
            w.Run(1);
            Assert.Equal((TriggerState.Done, 3), (Phase(w, t), w.S(t).FireCount));
            w.Run(12);
            Assert.Equal(3, TrgActions.Calls.Count);                           // maxFires caps it

            // the condition dropped before the cooldown ended ⇒ only a re-arm; the next rise is an edge as ever
            TrgActions.Reset();
            var w2 = new ObjWorld(); var src2 = Source(w2);
            var t2 = w2.Trigger(TrigJson(w2.Net(src2), ",\"repeat\":true,\"cooldown\":3,\"whileTrue\":true"));
            w2.Run(1.5); Flip(w2, src2, true); w2.Run(0.5);
            Assert.Equal(1, TrgActions.Calls.Count);
            Flip(w2, src2, false); w2.Run(4);
            Assert.Equal((1, TriggerState.Armed), (TrgActions.Calls.Count, Phase(w2, t2)));

            // without whileTrue: T2 — one fire, however long the condition stays true
            TrgActions.Reset();
            var w3 = new ObjWorld(); var src3 = Source(w3);
            var t3 = w3.Trigger(TrigJson(w3.Net(src3), ",\"repeat\":true,\"cooldown\":3"));
            w3.Run(1.5); Flip(w3, src3, true); w3.Run(12);
            Assert.Equal((1, TriggerState.Cooldown), (TrgActions.Calls.Count, Phase(w3, t3)));
        }
    }
}
