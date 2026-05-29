using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Utility;
using Fdp.Toolkit.Tests.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Integration tests for the Utility AI StarterPack decisions and systems.
    /// Covers CombatPostureDecision, ThreatRankingDecision, WeaponSelectionDecision,
    /// LeaderAssignmentDecision, and ThreatMatrixAssignmentSystem.
    /// All tests run against a real EntityRepository and real StandardInputs readers.
    /// </summary>
    public sealed class StarterPackIntegrationTests : IDisposable
    {
        private readonly UtilityTestWorld _world;

        public StarterPackIntegrationTests()
        {
            _world = new UtilityTestWorld();
            StandardInputs.RegisterAll();
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _world.Dispose();
        }

        // ── CombatPostureDecision ────────────────────────────────────────────────

        // SC-SP-01: Full health + live visual contact → AdvanceAndAttack.
        // AdvanceAndAttack product drives toward ~0.99; Hold WeightedSum ~0.38.
        [Fact]
        public void CombatPosture_FullHealth_WithVisualContact_SelectsAdvanceAndAttack()
        {
            var enemy = _world.Repo.CreateEntity();
            var agent = _world.SpawnAgent(1.0f, 1.0f);
            _world.SeedContact(agent, enemy, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, agent, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.AdvanceAndAttack, posture);
        }

        // SC-SP-02: No contacts → HaveLiveTarget=0 → Step gate kills AdvanceAndAttack.
        // TakeCover/Flee require EQS (not present). Suppress requires AllyAdvancingNearby (Phase-2 stub=0).
        // Only Hold (WeightedSum with Constant baseline) produces a non-zero score.
        [Fact]
        public void CombatPosture_NoContacts_SelectsHold()
        {
            var agent = _world.SpawnAgent(0.8f, 1.0f);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, agent, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.Hold, posture);
        }

        // SC-SP-03: AmmoFraction=0 → Threshold curve output=0 → WeightedProduct kills AdvanceAndAttack.
        // Visual contact is present, so HaveLiveTarget=1, but ammo gate overrides.
        // No EQS sensors → TakeCover/Flee=0. Hold wins as the only positive scorer.
        [Fact]
        public void CombatPosture_EmptyAmmo_WithVisualContact_SelectsHold()
        {
            var enemy = _world.Repo.CreateEntity();
            var agent = _world.SpawnAgent(0.8f, 0.0f);
            _world.SeedContact(agent, enemy, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, agent, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.Hold, posture);
        }

        // SC-SP-04: Hysteresis prevents a 1% health-drop from flipping posture.
        // Step 1 — Evaluate at health=0.08 (AdvanceAndAttack beats Hold by ~0.019).
        // Step 2 — Drop health to 0.07 (Hold would win by ~0.007 without the bonus).
        // Step 3 — SelectPosture reads the previous winner, applies +0.08 hysteresis, AA still wins.
        [Fact]
        public void CombatPosture_Hysteresis_SmallHealthDrop_DoesNotFlipPosture()
        {
            var enemy = _world.Repo.CreateEntity();
            var agent = _world.SpawnAgent(0.08f, 1.0f);
            _world.SeedContact(agent, enemy, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);

            // Prime buffer: at health=0.08, AdvanceAndAttack wins (score ~0.191 vs Hold ~0.172).
            _world.Scorer.Evaluate(_world.Repo, agent, CombatPostureDecision.Id);

            // Confirm AdvanceAndAttack was the winner before the health drop.
            byte primePosture = _world.Repo.GetComponentRO<UtilityResultBuffer>(agent).GetSpanRO()[0].WinningPostureId;
            Assert.Equal((byte)Posture.AdvanceAndAttack, primePosture);

            // Drop health by 1% absolute — without hysteresis Hold would win (~0.163 vs ~0.170).
            ref var h = ref _world.Repo.GetComponentRW<Health>(agent);
            h.Current = 7f; // 0.07 * 100

            // Hysteresis bonus +0.08 is applied to the previous winner: 0.163+0.08=0.243 > Hold 0.170.
            byte posture = _world.Scorer.SelectPosture(_world.Repo, agent, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.AdvanceAndAttack, posture);
        }

        // SC-P1-08-2: Trace records per-consideration breakdown for winner
        [Fact]
        public void Trace_Records_PerConsideration_Breakdown_For_Winner()
        {
            var enemy = _world.Repo.CreateEntity();
            var self = _world.SpawnAgent(health01: 0.35f, ammo01: 1.0f);
            _world.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 1.3f);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("CoverQuery"), topScore: 0.85f, count: 3, instanceId: 0);

            _world.Scorer.SelectPosture(_world.Repo, self, CombatPostureDecision.Id);

            ref readonly var trace = ref _world.Repo.GetComponentRO<UtilityTraceWorkingMemory1024>(self);
            var winner = trace.LatestSelected();
            Assert.True(winner.ConsiderationCount > 0);
            Assert.True(winner.RunnerUpMargin >= 0f);
            // The EqsTopScore consideration (cover query) was a decisive factor.
            var coverConsideration = winner.ConsiderationByInput(StandardInputIds.EqsTopScore);
            Assert.True(coverConsideration.CurveOutput > 0.5f,
                "EqsTopScore consideration for TakeCover branch should be > 0.5");
        }

        // SC-P1-07-3 / SC-P1-08-4: Hurt with cover available takes cover
        [Fact]
        public void Hurt_With_Cover_Available_Takes_Cover()
        {
            var enemy = _world.Repo.CreateEntity();
            var self = _world.SpawnAgent(health01: 0.35f, ammo01: 0.8f);
            _world.SeedContact(self, enemy, 90f, 0.8f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 1.3f);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("CoverQuery"),   topScore: 0.85f, count: 3, instanceId: 0);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("RetreatQuery"), topScore: 0.20f, count: 1, instanceId: 1);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, self, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.TakeCover, posture);
        }

        // SC-P1-07-3 / SC-P1-08-4: Near death with escape flees
        [Fact]
        public void NearDeath_With_Escape_Flees()
        {
            var enemy = _world.Repo.CreateEntity();
            var self = _world.SpawnAgent(health01: 0.12f, ammo01: 0.3f);
            _world.SeedContact(self, enemy, 70f, 0.9f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 2.5f);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("CoverQuery"),   topScore: 0.30f, count: 1, instanceId: 0);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("RetreatQuery"), topScore: 0.75f, count: 2, instanceId: 1);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, self, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.Flee, posture);
        }

        // SC-P1-07-3 / SC-P1-08-4: Near death with no escape and no cover does not flee into nothing
        [Fact]
        public void NearDeath_With_No_Escape_And_No_Cover_Does_Not_Flee_Into_Nothing()
        {
            var enemy = _world.Repo.CreateEntity();
            var self = _world.SpawnAgent(health01: 0.12f, ammo01: 0.6f);
            _world.SeedContact(self, enemy, 50f, 0.9f, 1f, hasLos: true);
            _world.SetEnemyStrengthRatio(self, 2.5f);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("CoverQuery"),   topScore: 0.05f, count: 0, instanceId: 0);
            _world.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("RetreatQuery"), topScore: 0.05f, count: 0, instanceId: 1);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, self, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.Hold, posture);
        }

        // ── ThreatRankingDecision ────────────────────────────────────────────────

        // SC-SP-05: Visual contact → HasLineOfSight=1 → Step(1)=1 → option scores > 0.
        [Fact]
        public void ThreatRanking_VisualContact_ReturnsPositiveScore()
        {
            var agent = _world.SpawnAgent(1.0f, 1.0f);
            var enemy = _world.Repo.CreateEntity();
            _world.SeedContact(agent, enemy, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);

            _world.Scorer.Evaluate(_world.Repo, agent, ThreatRankingDecision.Id);

            ref readonly var buf = ref _world.Repo.GetComponentRO<UtilityResultBuffer>(agent);
            Assert.True(buf.Count > 0);
            Assert.True(buf.GetSpanRO()[0].Score > 0f);
        }

        // SC-SP-06: Acoustic-only contact → HasLineOfSight=0 → Step(0)=0 → WeightedProduct collapses to 0.
        [Fact]
        public void ThreatRanking_AcousticContactOnly_ScoresZero()
        {
            var agent = _world.SpawnAgent(1.0f, 1.0f);
            var enemy = _world.Repo.CreateEntity();
            _world.SeedContact(agent, enemy, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: false);

            _world.Scorer.Evaluate(_world.Repo, agent, ThreatRankingDecision.Id);

            ref readonly var buf = ref _world.Repo.GetComponentRO<UtilityResultBuffer>(agent);
            Assert.True(buf.Count > 0);
            Assert.Equal(0f, buf.GetSpanRO()[0].Score);
        }

        // SC-SP-07: Closer visual contact ranks above farther one.
        // DistanceToContext is mapped through Curve.Linear, so close→higher input→higher score.
        // Agent at (0,0,0). ContactA at 100m (close), ContactB at 800m (far). Same threat and health.
        [Fact]
        public void ThreatRanking_CloserContactRanksFirst()
        {
            var agent   = _world.SpawnAgent(1.0f, 1.0f);
            var enemyA  = _world.Repo.CreateEntity(); // close
            var enemyB  = _world.Repo.CreateEntity(); // far
            _world.SeedContact(agent, enemyA, 100f, threatBoost: 0.5f, contactHealth01: 0.5f, hasLos: true);
            _world.SeedContact(agent, enemyB, 800f, threatBoost: 0.5f, contactHealth01: 0.5f, hasLos: true);

            _world.Scorer.Evaluate(_world.Repo, agent, ThreatRankingDecision.Id);

            ref readonly var buf = ref _world.Repo.GetComponentRO<UtilityResultBuffer>(agent);
            Assert.True(buf.Count >= 2);
            // Winner entity handle should match the closer contact.
            long winnerHandle = buf.GetSpanRO()[0].CandidateHandle;
            Assert.Equal((long)enemyA.PackedValue, winnerHandle);
        }

        // SC-P1-06-5: Assigned target bias promotes leader choice
        [Fact]
        public void Assigned_Target_Bias_Promotes_Leader_Choice()
        {
            var leader = _world.SpawnLeader();
            var self   = _world.SpawnSquadMember(leader, 1.0f, 1.0f);
            var a      = _world.SpawnTarget();
            var b      = _world.SpawnTarget();

            // Seed both targets into self's TargetMemory.
            // 'a' has slightly higher threat so it would win WITHOUT assignment bias.
            // health=0.5f ensures ContactHealthFraction consideration is non-zero.
            _world.SeedContact(self, a, 100f, threatBoost: 0.52f, contactHealth01: 0.5f, hasLos: true);
            _world.SeedContact(self, b, 100f, threatBoost: 0.50f, contactHealth01: 0.5f, hasLos: true);

            // Seed only 'b' into the leader's TargetMemory — the assignment system
            // has no other candidate and therefore assigns 'b' to 'self'.
            _world.SeedContact(leader, b, 100f, threatBoost: 0.8f, contactHealth01: 0.5f, hasLos: true);

            // Run the assignment system — self gets assigned to b.
            var assignSys = new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id);
            assignSys.Run(_world.Repo, leader);

            // Confirm the system wrote the assignment correctly.
            Assert.Equal((long)b.PackedValue, _world.AssignmentFor(leader, self));

            // ThreatRankingDecision includes IsAssignedTarget (weight 0.9).
            // IsAssignedTarget returns 0 for 'a' (collapses its score) and 1 for 'b'.
            // Therefore 'b' must rank first despite 'a' having higher natural threat.
            _world.Scorer.Evaluate(_world.Repo, self, ThreatRankingDecision.Id);

            Assert.Equal(b.PackedValue, _world.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().Candidate);
        }

        // ── WeaponSelectionDecision ──────────────────────────────────────────────

        // SC-SP-08: Mount whose effective range matches the engagement distance scores via Bell(1.0)=1.0.
        // Mount at 500m effective range scores Bell(100/500)=Bell(0.2)<<1. Mount at 100m wins.
        [Fact]
        public void WeaponSelection_MountAtEffectiveRange_RanksFirst()
        {
            var agent  = _world.SpawnAgent(1.0f, 1.0f);
            var target = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(target, new Position { Value = new Vector3(100f, 0f, 0f) });

            var mount100  = _world.SpawnWeaponMount(agent, mountIndex: 1, weaponGuid: 0xAAAA_0001UL,
                effRange: 100f,  ammo01: 1.0f, initialAmmunition: 30);
            var mount500  = _world.SpawnWeaponMount(agent, mountIndex: 2, weaponGuid: 0xAAAA_0002UL,
                effRange: 500f,  ammo01: 1.0f, initialAmmunition: 30);

            _world.Scorer.Evaluate(_world.Repo, agent, WeaponSelectionDecision.Id, target);

            ref readonly var buf = ref _world.Repo.GetComponentRO<UtilityResultBuffer>(agent);
            Assert.True(buf.Count >= 2);
            Assert.True(buf.GetSpanRO()[0].Score > buf.GetSpanRO()[1].Score);
            // The winner is the mount at 100m effective range.
            long winnerHandle = buf.GetSpanRO()[0].CandidateHandle;
            Assert.Equal((long)mount100.PackedValue, winnerHandle);
        }

        // SC-SP-09: A mount with zero ammo → WeaponHasAmmo=0 → Step(0)=0 → score=0.
        // The mount with ammo should win; the empty mount should score 0.
        [Fact]
        public void WeaponSelection_EmptyMount_ScoresZero()
        {
            var agent  = _world.SpawnAgent(1.0f, 1.0f);
            var target = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(target, new Position { Value = new Vector3(50f, 0f, 0f) });

            var mountFull  = _world.SpawnWeaponMount(agent, mountIndex: 1, weaponGuid: 0xBBBB_0001UL,
                effRange: 50f, ammo01: 1.0f, initialAmmunition: 30);
            var mountEmpty = _world.SpawnWeaponMount(agent, mountIndex: 2, weaponGuid: 0xBBBB_0002UL,
                effRange: 50f, ammo01: 0.0f, initialAmmunition: 30);

            _world.Scorer.Evaluate(_world.Repo, agent, WeaponSelectionDecision.Id, target);

            ref readonly var buf = ref _world.Repo.GetComponentRO<UtilityResultBuffer>(agent);
            Assert.True(buf.Count >= 2);

            // Find the empty mount's entry and verify score = 0.
            var span = buf.GetSpanRO();
            float emptyScore = -1f;
            for (int i = 0; i < buf.Count; i++)
            {
                if (span[i].CandidateHandle == (long)mountEmpty.PackedValue)
                {
                    emptyScore = span[i].Score;
                    break;
                }
            }
            Assert.Equal(0f, emptyScore);

            // The full mount should rank first.
            Assert.Equal((long)mountFull.PackedValue, span[0].CandidateHandle);
        }

        // ── ThreatMatrixAssignmentSystem / LeaderAssignmentDecision ─────────────

        // SC-SP-10: Single member with visual LOS to target → gets assigned after Run().
        [Fact]
        public void Assignment_SingleMember_VisualContact_GetsAssigned()
        {
            var leader = _world.SpawnLeader();
            var member = _world.SpawnSquadMember(leader, health01: 1.0f, ammo01: 1.0f);
            var target = _world.Repo.CreateEntity();

            // Seed target into both leader's and member's TargetMemory.
            _world.SeedContact(leader, target, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);
            _world.SeedContact(member, target, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);

            var sys = new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id, maxFocusFireCount: 2);
            sys.Run(_world.Repo, leader);

            long assigned = _world.AssignmentFor(leader, member);
            Assert.Equal((long)target.PackedValue, assigned);
        }

        // SC-SP-11: Single member with acoustic-only contact → HasLineOfSight=0 → Step gate kills score.
        // Score == 0 → ThreatMatrixAssignmentSystem skips assignment (bestScore > 0f is required).
        [Fact]
        public void Assignment_SingleMember_AcousticOnly_NotAssigned()
        {
            var leader = _world.SpawnLeader();
            var member = _world.SpawnSquadMember(leader, health01: 1.0f, ammo01: 1.0f);
            var target = _world.Repo.CreateEntity();

            _world.SeedContact(leader, target, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: true);
            // Member only has acoustic contact — LOS flag not set.
            _world.SeedContact(member, target, 100f, threatBoost: 0.5f, contactHealth01: 0.8f, hasLos: false);

            var sys = new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id, maxFocusFireCount: 2);
            sys.Run(_world.Repo, leader);

            long assigned = _world.AssignmentFor(leader, member);
            // Expect no assignment (handle = 0 from state default).
            Assert.Equal(0L, assigned);
        }

        // SC-SP-12: Focus-fire cap=2 with 3 members and 2 targets.
        // Members 0 and 1 are both assigned to T1 (the higher-threat target).
        // When cap is reached, member 2 is forced onto T2 (the only remaining target under cap).
        [Fact]
        public void Assignment_FocusFireCap_ThirdMemberAssignedToSecondTarget()
        {
            var leader  = _world.SpawnLeader();
            var member0 = _world.SpawnSquadMember(leader, health01: 1.0f, ammo01: 1.0f);
            var member1 = _world.SpawnSquadMember(leader, health01: 1.0f, ammo01: 1.0f);
            var member2 = _world.SpawnSquadMember(leader, health01: 1.0f, ammo01: 1.0f);

            var target1 = _world.Repo.CreateEntity(); // high-threat target
            var target2 = _world.Repo.CreateEntity(); // lower-threat target

            // Seed both targets into leader's TargetMemory.
            _world.SeedContact(leader, target1, 100f, threatBoost: 0.8f, contactHealth01: 0.8f, hasLos: true);
            _world.SeedContact(leader, target2, 200f, threatBoost: 0.3f, contactHealth01: 0.8f, hasLos: true);

            // Seed both targets into every member's TargetMemory with visual LOS.
            foreach (var member in new[] { member0, member1, member2 })
            {
                _world.SeedContact(member, target1, 100f, threatBoost: 0.8f, contactHealth01: 0.8f, hasLos: true);
                _world.SeedContact(member, target2, 200f, threatBoost: 0.3f, contactHealth01: 0.8f, hasLos: true);
            }

            var sys = new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id, maxFocusFireCount: 2);
            sys.Run(_world.Repo, leader);

            // Members 0 and 1 take the higher-scoring target1 (up to focusCap=2).
            Assert.Equal((long)target1.PackedValue, _world.AssignmentFor(leader, member0));
            Assert.Equal((long)target1.PackedValue, _world.AssignmentFor(leader, member1));

            // Member 2 spills over to target2.
            Assert.Equal((long)target2.PackedValue, _world.AssignmentFor(leader, member2));
        }

        // SC-P1-07-3 / SC-P1-08-4: Wounded member vetoes assignment and breaks off
        [Fact]
        public void Wounded_Member_Vetoes_Assignment_And_Breaks_Off()
        {
            var leader = _world.SpawnLeader();
            var m1     = _world.SpawnSquadMember(leader, health01: 0.08f, ammo01: 1.0f);
            var t1     = _world.SpawnTarget();

            // Seed target into leader and member.
            _world.SeedContact(leader, t1, 100f, threatBoost: 0.7f, contactHealth01: 1f, hasLos: true);
            _world.SeedContact(m1, t1, 100f, threatBoost: 0.7f, contactHealth01: 1f, hasLos: true);

            // Run assignment system to confirm m1 is assigned to t1.
            var sys = new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id, maxFocusFireCount: 2);
            sys.Run(_world.Repo, leader);
            Assert.Equal((long)t1.PackedValue, _world.AssignmentFor(leader, m1));

            // Add retreat EQS sensor so Flee is not gated.
            _world.SpawnEqsSensor(m1, UtilityTestWorld.Fnv1a32("RetreatQuery"), topScore: 0.7f, count: 1, instanceId: 1);

            byte posture = _world.Scorer.SelectPosture(_world.Repo, m1, CombatPostureDecision.Id);

            Assert.Equal((byte)Posture.Flee, posture);
        }
    }
}
