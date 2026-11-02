using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Helpers;
using Fdp.Toolkit.Behavior.Components;
using Xunit;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;
using EcsMissionTrigger = Fdp.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MissionTriggerHelper.ResolveTrigger"/> (BUG2-DEBT-01).
    /// Migrated from per-class tests to verify the consolidated shared utility.
    /// </summary>
    public class EntityMissionIngressTranslatorTests
    {
        // ── BUG2-M001 – New trigger cases ─────────────────────────────────────

        [Fact]
        public void ResolveTrigger_BehaviorFinished_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "BehaviorFinished", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers, "task-A");

            Assert.Equal(EcsMissionTrigger.BehaviorFinished, trigger);
            Assert.Equal(0f, param);
        }

        [Fact]
        public void ResolveTrigger_UnderAttack_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "UnderAttack", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers, "task-A");

            Assert.Equal(EcsMissionTrigger.UnderAttack, trigger);
            Assert.Equal(0f, param);
        }

        // Existing cases should still work

        [Fact]
        public void ResolveTrigger_TimerElapsed_ReturnsTimerElapsedEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "TimerElapsed", Params = "5.5" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers, "task-A");

            Assert.Equal(EcsMissionTrigger.TimerElapsed, trigger);
            Assert.Equal(5.5f, param, precision: 2);
        }

        [Fact]
        public void ResolveTrigger_ReachedDestination_MapsToBehaviorFinished()
        {
            // "ReachedDestination" is the legacy wire string; per BS1-T022 it maps to
            // BehaviorFinished at ingress (EcsMissionTrigger.ReachedDestination is [Obsolete]).
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "ReachedDestination", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers, "task-A");

            Assert.Equal(EcsMissionTrigger.BehaviorFinished, trigger);
            Assert.Equal(0f, param);
        }

        [Fact]
        public void ResolveTrigger_HealthCritical_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "HealthCritical", Params = "0.25" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers, "task-A");

            Assert.Equal(EcsMissionTrigger.HealthCritical, trigger);
            Assert.Equal(0.25f, param, precision: 2);
        }

        /// <summary>⭐ CE-4157 — an unknown trigger type is a LOUD error naming the task and the known types (it used to be a timer at 0).
        /// ✅ Red-proof: the default arm back to a fallback ⇒ red.</summary>
        [Fact]
        public void ResolveTrigger_UnknownType_ThrowsNamingTheTask()
        {
            var triggers = new List<DdsMissionTrigger> { new DdsMissionTrigger { Type = "SomeUnknownTrigger", Params = "" } };
            var ex = Assert.Throws<Hrot.Core.Mission.MissionPlanException>(() => MissionTriggerHelper.ResolveTrigger(triggers, "task-A"));
            Assert.Contains("task-A", ex.Message);
            Assert.Contains("SomeUnknownTrigger", ex.Message);
            Assert.Contains("BehaviorFinished", ex.Message);
        }

        /// <summary>⭐ CE-4157 — a phase holds ONE trigger; a second is refused, never dropped. ✅ Red-proof: the Count &gt; 1 test removed ⇒ red.</summary>
        [Fact]
        public void ResolveTrigger_TwoTriggers_Throws()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "BehaviorFinished", Params = "" },
                new DdsMissionTrigger { Type = "TimerElapsed", Params = "5" },
            };
            var ex = Assert.Throws<Hrot.Core.Mission.MissionPlanException>(() => MissionTriggerHelper.ResolveTrigger(triggers, "task-B"));
            Assert.Contains("task-B", ex.Message);
        }

        /// <summary>⭐ CE-4157 — a NON-empty parameter that does not parse is loud; an empty one stays 0.</summary>
        [Theory]
        [InlineData("TimerElapsed")]
        [InlineData("HealthCritical")]
        public void ResolveTrigger_ABadParam_Throws_AnEmptyOneIsZero(string type)
        {
            Assert.Throws<Hrot.Core.Mission.MissionPlanException>(
                () => MissionTriggerHelper.ResolveTrigger(new List<DdsMissionTrigger> { new DdsMissionTrigger { Type = type, Params = "abc" } }, "t"));
            var (_, param) = MissionTriggerHelper.ResolveTrigger(new List<DdsMissionTrigger> { new DdsMissionTrigger { Type = type, Params = "" } }, "t");
            Assert.Equal(0f, param);
        }

        [Fact]
        public void ResolveTrigger_EmptyBehaviorFinished_IsFine_AndNoTriggerHoldsForever()
        {
            var (t, p) = MissionTriggerHelper.ResolveTrigger(new List<DdsMissionTrigger> { new DdsMissionTrigger { Type = "BehaviorFinished", Params = "" } }, "t");
            Assert.Equal((EcsMissionTrigger.BehaviorFinished, 0f), (t, p));
            Assert.Equal((EcsMissionTrigger.TimerElapsed, float.MaxValue), MissionTriggerHelper.ResolveTrigger(null, "t"));
        }
    }
}
