using System;
using Fdp.Toolkit.Perception.Components;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TargetMemory"/> struct and
    /// <see cref="PerceptionConstants"/> invariants.
    /// These tests have no ECS dependency — they exercise the data-model layer only.
    /// </summary>
    public class PerceptionComponentTests
    {
        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void TargetMemory_IsUnmanagedValueType()
        {
            // TargetMemory must be a value-type (unmanaged) so it can be stored in
            // NativeChunkTable and copied without GC involvement.
            Assert.True(typeof(TargetMemory).IsValueType,
                "TargetMemory must be a value type (struct) for ECS storage.");
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void MaxTrackedTargets_ConstantValueIsSixteen()
        {
            // P0.03: raised from 4 to 16 for Utility AI Phase-1.
            // The constant drives all fixed-array sizes; if it ever changes, tests will catch the drift.
            Assert.Equal(16, PerceptionConstants.MaxTrackedTargets);
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void AddOrUpdateTarget_FirstEntry_SetsCountToOneAndRecordsEntityId()
        {
            // Arrange
            var mem = new TargetMemory();
            const long expectedEntityId = 42L;

            // Act
            TargetMemory.AddOrUpdateTarget(ref mem,
                entityId:   expectedEntityId,
                posX:       10f,
                posY:       20f,
                scoreBoost: 30f,
                tick:       1u);

            // Assert
            Assert.Equal(1, mem.Count);
            Assert.Equal(expectedEntityId, mem.EntityIds[0]);
            Assert.Equal(30f, mem.ThreatScores[0]);
        }

        // ── Test 4 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void AddOrUpdateTarget_SameEntityTwice_AccumulatesScoreCountStaysOne()
        {
            // Arrange
            var mem = new TargetMemory();
            const long entityId = 7L;

            // Act — add the same entity twice with different boosts
            TargetMemory.AddOrUpdateTarget(ref mem, entityId, 0f, 0f, 40f, 1u);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId, 0f, 0f, 25f, 2u);

            // Assert — scores are accumulated, not replaced; count remains 1
            Assert.Equal(1, mem.Count);
            Assert.Equal(65f, mem.ThreatScores[0]); // 40 + 25
        }

        // ── Test 5 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void AddOrUpdateTarget_WhenTableFull_EvictsLowestScoringSlot()
        {
            // Arrange — fill all 16 slots in ascending score order (score = entityId * 10)
            var mem = new TargetMemory();
            for (int i = 1; i <= PerceptionConstants.MaxTrackedTargets; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)i, 0f, 0f, (float)(i * 10), 0u);
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count); // table full

            // Act — add a 17th entry whose score (25) exceeds the current lowest (10, entity 1)
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 17L, 0f, 0f, 25f, 0u);

            // Assert — count stays at MaxTrackedTargets and entity 1 (score 10) was evicted
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count);
            bool entity1Present = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 1L) entity1Present = true;
            Assert.False(entity1Present, "Entity 1 (lowest score 10) should have been evicted.");

            // Verify the new entry is present
            bool entity17Present = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 17L) entity17Present = true;
            Assert.True(entity17Present, "Entity 17 (score 25) should be in the table after eviction.");
        }

        // ── Test 6 (SC-P0-03-3): Fill 16 contacts; Count==16, sorted descending ────────

        [Fact]
        public unsafe void AddOrUpdateTarget_Fill16_CountIs16AndSortedDescending()
        {
            var mem = new TargetMemory();
            for (int i = 1; i <= 16; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)i, 0f, 0f, (float)i, (uint)i);

            Assert.Equal(16, mem.Count);
            // Highest score should be at index 0, second-highest at index 1
            Assert.True(mem.ThreatScores[0] >= mem.ThreatScores[1],
                "Slot 0 must have score >= slot 1 (descending sort).");
        }

        // ── Test 7 (SC-P0-03-4): 17th with higher score evicts the lowest ─────────────

        [Fact]
        public unsafe void AddOrUpdateTarget_17thWithHigherScore_EvictsLowest_CountStays16()
        {
            var mem = new TargetMemory();
            for (int i = 1; i <= 16; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)i, 0f, 0f, (float)(i * 5), (uint)i);
            // Lowest score is entity 1 with score 5.

            // Add entity 17 with score higher than the lowest (5)
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 17L, 0f, 0f, 50f, 17u);

            Assert.Equal(16, mem.Count);

            // Entity 1 (score 5) should be evicted
            bool entity1Found = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 1L) entity1Found = true;
            Assert.False(entity1Found, "Entity 1 should be evicted after 17th high-score entry.");

            // Entity 17 should be present
            bool entity17Found = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 17L) entity17Found = true;
            Assert.True(entity17Found, "Entity 17 should be in the table.");
        }

        // ── Test 8 (SC-P0-03-5): 17th with lower score than all existing → rejected ───

        [Fact]
        public unsafe void AddOrUpdateTarget_17thWithLowerScore_IsRejected_TableUnchanged()
        {
            var mem = new TargetMemory();
            for (int i = 1; i <= 16; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)i, 0f, 0f, 100f + i, (uint)i);
            // All scores are >= 101. Add entity 17 with score 1 (lower than all).

            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 17L, 0f, 0f, 1f, 17u);

            Assert.Equal(16, mem.Count);

            // Entity 17 should NOT be present
            bool entity17Found = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 17L) entity17Found = true;
            Assert.False(entity17Found, "Entity 17 (score too low) should be rejected.");
        }

        // ── Test 6 (P3D-206): PositionsZ moves in lockstep through eviction + sort ──

        [Fact]
        public unsafe void AddOrUpdateTarget_PositionZ_MovesInLockstepWithXY()
        {
            // Fill the table (MaxTrackedTargets entries) with a distinct Z per entity, where
            // Z == score (easy invariant to check) and X == entityId.
            // Score = entityId * 10 for entities 1..MaxTrackedTargets.
            int max = PerceptionConstants.MaxTrackedTargets;
            var mem = new TargetMemory();
            for (int i = 1; i <= max; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, entityId: i, posX: i, posY: i,
                    scoreBoost: i * 10f, tick: 0u, posZ: i * 10f);

            // After filling: table is full, Z == score for all slots.
            Assert.Equal(max, mem.Count);
            for (int i = 0; i < mem.Count; i++)
            {
                Assert.Equal(mem.ThreatScores[i], mem.PositionsZ[i]); // Z == score (lockstep)
                Assert.Equal((float)mem.EntityIds[i], mem.PositionsX[i]); // X == entityId (lockstep)
            }

            // Add one more entry (entity max+1, score 55, Z 55) which evicts entity 1 (score 10, lowest).
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: max + 1, posX: max + 1f, posY: max + 1f,
                scoreBoost: 55f, tick: 0u, posZ: 55f);

            Assert.Equal(max, mem.Count);

            // Lockstep invariant must hold after eviction+re-sort.
            for (int i = 0; i < mem.Count; i++)
                Assert.Equal(mem.ThreatScores[i], mem.PositionsZ[i]);

            // Entity 1 (score 10, lowest) evicted; entity max+1 (score 55) present with Z=55.
            bool entity1Found = false;
            bool entityNewFound = false;
            float entityNewZ = 0f;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == 1L) entity1Found = true;
                if (mem.EntityIds[i] == (long)(max + 1)) { entityNewFound = true; entityNewZ = mem.PositionsZ[i]; }
            }
            Assert.False(entity1Found, "Entity 1 (score 10, lowest) should be evicted.");
            Assert.True(entityNewFound, "New entity (score 55) should be present after eviction.");
            Assert.Equal(55f, entityNewZ);

            // Spot-check top 2: highest scores are max*10 (entity max) and (max-1)*10 (entity max-1).
            Assert.Equal(max * 10f, mem.PositionsZ[0]);
            Assert.Equal((max - 1) * 10f, mem.PositionsZ[1]);
        }
    }
}
