using System;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Fdp.Toolkit.Utility.Integration;
using Fdp.Toolkit.Tests.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Tests for <see cref="UtilitySelectorNode"/> BTree integration helper.
    /// Success condition: SC-P1-09-1.
    /// </summary>
    public sealed class UtilitySelectorNodeTests : IDisposable
    {
        private readonly UtilityTestWorld _world;

        public UtilitySelectorNodeTests()
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
        public void SelectBranch_Returns_HighestScoringOption_Index()
        {
            var self = _world.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
            var enemy = _world.Repo.CreateEntity();
            _world.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 0.4f);     // outnumbering -> AdvanceAndAttack scores high

            var node = new UtilitySelectorNode(
                _world.Scorer,
                CombatPostureDecision.Id,
                new byte[] {
                    (byte)Posture.AdvanceAndAttack,
                    (byte)Posture.TakeCover,
                    (byte)Posture.Flee,
                });

            int branch = node.SelectBranch(_world.Repo, self);
            Assert.Equal(0, branch);    // AdvanceAndAttack is index 0
        }

        [Fact]
        public void Hysteresis_Suppresses_Switch_On_Marginal_Score_Change()
        {
            var self = _world.SpawnAgent(health01: 0.55f, ammo01: 0.9f);
            var enemy = _world.Repo.CreateEntity();
            _world.SeedContact(self, enemy, 90f, 0.6f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 1.0f);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("CoverQuery"), topScore: 0.55f,
                                 count: 2, instanceId: 0);

            var node = new UtilitySelectorNode(
                _world.Scorer,
                CombatPostureDecision.Id,
                new byte[] {
                    (byte)Posture.AdvanceAndAttack,
                    (byte)Posture.TakeCover,
                });

            int first = node.SelectBranch(_world.Repo, self);

            // 1% health nudge -- without hysteresis this could flip branches
            _world.SetHealth(self, 0.54f);
            int second = node.SelectBranch(_world.Repo, self);

            Assert.Equal(first, second);    // hysteresis holds the selection
        }
    }
}
