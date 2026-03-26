using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.Map.Common.Helpers;
using FDP.Toolkit.Behavior.Components;
using Xunit;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Bagira.Map.Common.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MissionTriggerHelper.ResolveTrigger"/> (BUG2-DEBT-01).
    /// Migrated from per-class tests to verify the consolidated shared utility.
    /// </summary>
    public class EntityMissionIngressTranslatorTests
    {
        // ── BUG2-M001 – New trigger cases ─────────────────────────────────────

        [Fact]
        public void ResolveTrigger_DoctrineFinished_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "DoctrineFinished", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            Assert.Equal(EcsMissionTrigger.DoctrineFinished, trigger);
            Assert.Equal(0f, param);
        }

        [Fact]
        public void ResolveTrigger_UnderAttack_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "UnderAttack", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

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

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            Assert.Equal(EcsMissionTrigger.TimerElapsed, trigger);
            Assert.Equal(5.5f, param, precision: 2);
        }

        [Fact]
        public void ResolveTrigger_ReachedDestination_MapsToDoctrineFinished()
        {
            // "ReachedDestination" is the legacy wire string; per BS1-T022 it maps to
            // DoctrineFinished at ingress (EcsMissionTrigger.ReachedDestination is [Obsolete]).
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "ReachedDestination", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            Assert.Equal(EcsMissionTrigger.DoctrineFinished, trigger);
            Assert.Equal(0f, param);
        }

        [Fact]
        public void ResolveTrigger_HealthCritical_ReturnsCorrectEnum()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "HealthCritical", Params = "0.25" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            Assert.Equal(EcsMissionTrigger.HealthCritical, trigger);
            Assert.Equal(0.25f, param, precision: 2);
        }

        [Fact]
        public void ResolveTrigger_UnknownType_FallsBackToTimerElapsedZero()
        {
            var triggers = new List<DdsMissionTrigger>
            {
                new DdsMissionTrigger { Type = "SomeUnknownTrigger", Params = "" }
            };

            var (trigger, param) = MissionTriggerHelper.ResolveTrigger(triggers);

            // Unknown trigger strings fall back to TimerElapsed(0f) as safe observable failure mode.
            Assert.Equal(EcsMissionTrigger.TimerElapsed, trigger);
            Assert.Equal(0f, param);
        }
    }
}
