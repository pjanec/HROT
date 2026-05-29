using System;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Fdp.Toolkit.Utility.Integration;
using Fdp.Toolkit.Tests.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Tests for <see cref="UtilityTransitionArbiter"/> HSM integration guard.
    /// Success condition: SC-P1-09-2.
    /// </summary>
    public sealed class UtilityTransitionArbiterTests : IDisposable
    {
        private readonly UtilityTestWorld _world;

        public UtilityTransitionArbiterTests()
        {
            _world = new UtilityTestWorld();
            StandardInputs.RegisterAll();
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _world.Dispose();
        }

        [Fact]
        public void Evaluate_ReturnsTrue_ForWinningOption()
        {
            var self = _world.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
            var enemy = _world.Repo.CreateEntity();
            _world.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 0.4f);   // AdvanceAndAttack should win

            _world.Scorer.SelectPosture(_world.Repo, self, CombatPostureDecision.Id);

            byte winner = _world.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().WinningPostureId;
            Assert.True(UtilityTransitionArbiter.Evaluate(_world.Repo, self, winner));
        }

        [Fact]
        public void Evaluate_ReturnsFalse_ForLosingOption()
        {
            var self = _world.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
            var enemy = _world.Repo.CreateEntity();
            _world.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 0.4f);

            _world.Scorer.SelectPosture(_world.Repo, self, CombatPostureDecision.Id);

            byte winner = _world.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().WinningPostureId;
            byte loser = winner == (byte)Posture.AdvanceAndAttack
                ? (byte)Posture.Flee
                : (byte)Posture.AdvanceAndAttack;

            Assert.False(UtilityTransitionArbiter.Evaluate(_world.Repo, self, loser));
        }

        [Fact]
        public void Evaluate_ReturnsFalse_WhenNoResultBuffer()
        {
            var bare = _world.Repo.CreateEntity();   // no components
            Assert.False(UtilityTransitionArbiter.Evaluate(_world.Repo, bare, 1));
        }
    }
}
