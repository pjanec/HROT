using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ CE-4614 — the <c>Strength</c> and <c>QuietArea</c> objectives (DESIGN_Scenario_Rules §10.2): the command-chain battalion's end conditions.</summary>
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>Strength below 30 % of a 4-man squad: 2 down ⇒ 50 %, Active; 3 down ⇒ 25 %, Achieved. ✅ Red-proof: <c>Met</c> compares with
        /// <c>&lt;=</c> on the authorized count instead of the fraction ⇒ red.</summary>
        [Fact]
        public void CE4614_Strength_AchievesWhenTheUnitFallsBelowTheThreshold()
        {
            var w = new ObjWorld();
            var men = new[] { w.Man(Outside, ForceId.Hostile, 10f), w.Man(Outside, ForceId.Hostile, 10f), w.Man(Outside, ForceId.Hostile, 10f), w.Man(Outside, ForceId.Hostile, 10f) };
            var squad = w.Squad(ForceId.Hostile, men);
            var below = w.Objective("Strength", $"{{{Common(w, squad)},\"threshold\":0.3}}");
            var kept = w.Objective("Strength", $"{{{Common(w, squad)},\"threshold\":0.5,\"above\":true}}");
            w.Run(1);
            Assert.Equal((ObjectiveState.Active, "100 % of 4 men, needs below 30 %"), (w.S(below).State, w.S(below).Reason.ToString()));
            Assert.Equal(ObjectiveState.Achieved, w.S(kept).State);   // at least 50 % present
            w.Kill(men[0], Entity.Null); w.Kill(men[1], Entity.Null);
            w.Run(1);
            Assert.Equal((ObjectiveState.Active, 50), (w.S(below).State, w.S(below).ProgressCur));
            w.Kill(men[2], Entity.Null);
            w.Run(1);
            Assert.Equal((ObjectiveState.Achieved, 25), (w.S(below).State, w.S(below).ProgressCur));
        }

        /// <summary>Strength with a deadline and nobody hurt ⇒ Failed after it. ✅ Red-proof: the deadline check removed ⇒ red.</summary>
        [Fact]
        public void CE4614_Strength_FailsAtTheDeadline()
        {
            var w = new ObjWorld();
            var squad = w.Squad(ForceId.Hostile, w.Man(Outside, ForceId.Hostile, 10f), w.Man(Outside, ForceId.Hostile, 10f));
            var o = w.Objective("Strength", $"{{{Common(w, squad, ",\"deadlineSeconds\":3")},\"threshold\":0.3}}");
            w.Run(2); Assert.Equal(ObjectiveState.Active, w.S(o).State);
            w.Run(2); Assert.Equal(ObjectiveState.Failed, w.S(o).State);
        }

        /// <summary>QuietArea 3 s: a shot from inside, a hit on a man inside and a burst landing inside each restart the count; a shot from OUTSIDE does
        /// not; 3 s with none ⇒ Achieved. ✅ Red-proof: the burst branch removed ⇒ red (and the shooter-position test dropped ⇒ the outside shot resets ⇒ red).</summary>
        [Fact]
        public void CE4614_QuietArea_EveryKindOfFightingInsideRestartsTheCount_AndQuietAchieves()
        {
            var w = new ObjWorld();
            if (!w.Repo.Bus.IsRegistered<WeaponFireIntent>()) w.Repo.Bus.Register<WeaponFireIntent>();
            var inside = w.Man(Inside, ForceId.Friend, 10f);
            var outside = w.Man(Outside, ForceId.Hostile, 10f);
            var squad = w.Squad(ForceId.Friend, inside);
            var q = w.Objective("QuietArea", $"{{{Common(w, squad)},\"area\":{Circle0},\"forSeconds\":3}}");
            w.Run(2);
            Assert.Equal(ObjectiveState.Active, w.S(q).State); Assert.True(w.S(q).ProgressCur >= 1);   // the first tick begins it (CE-4568)

            w.Repo.Bus.Publish(new WeaponFireIntent { Shooter = inside }); w.Tick();
            Assert.Equal(0, w.S(q).ProgressCur);   // a shot from inside
            w.Run(2);
            w.Repo.Bus.Publish(new WeaponFireIntent { Shooter = outside }); w.Tick();
            Assert.True(w.S(q).ProgressCur >= 1, w.S(q).Reason.ToString());   // a shot from outside is not fighting INSIDE

            w.Harm(inside, outside); w.Tick();
            Assert.Equal(0, w.S(q).ProgressCur);   // a hit on a man inside
            w.Run(2);
            RecentDetonations.Add(w.Repo, new Vector3(1, 1, 0), w.Time, outside, 0); w.Tick();
            Assert.Equal(0, w.S(q).ProgressCur);   // a burst landing inside
            RecentDetonations.Add(w.Repo, Outside, w.Time, outside, 0);
            w.Run(3.5);
            Assert.Equal(ObjectiveState.Achieved, w.S(q).State);
        }
    }
}
