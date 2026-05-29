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
            // Fill the table (MaxTrackedTargets+1 entries) with a distinct Z per entity, where
            // Z is derived from the score so we can verify the Z slot tracks its owning entry
            // through the descending insertion-sort and the lowest-score eviction.
            var mem = new TargetMemory();
            // entityId, score, and Z chosen so Z == score (easy invariant to check).
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 1L, posX: 1f, posY: 1f, scoreBoost: 10f, tick: 0u, posZ: 10f);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 2L, posX: 2f, posY: 2f, scoreBoost: 20f, tick: 0u, posZ: 20f);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 3L, posX: 3f, posY: 3f, scoreBoost: 30f, tick: 0u, posZ: 30f);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 4L, posX: 4f, posY: 4f, scoreBoost: 40f, tick: 0u, posZ: 40f);

            // Fifth entry (score 25, Z 25) evicts the lowest (entity 1, score 10).
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 5L, posX: 5f, posY: 5f, scoreBoost: 25f, tick: 0u, posZ: 25f);

            // Sorted descending by score: [40, 30, 25, 20]. We seeded Z == score and X == entityId,
            // so for every surviving slot Z must equal its score (proving Z rode the sort/eviction
            // in lockstep) and X must equal its owning entity id.
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count);
            for (int i = 0; i < mem.Count; i++)
            {
                Assert.Equal(mem.ThreatScores[i], mem.PositionsZ[i]); // Z == score (lockstep)
                Assert.Equal((float)mem.EntityIds[i], mem.PositionsX[i]); // X == entityId (lockstep)
            }

            // Spot-check the descending ordering: entity 1 (Z=10) evicted, entity 5 (Z=25) present.
            Assert.Equal(40f, mem.PositionsZ[0]); // entity 4
            Assert.Equal(30f, mem.PositionsZ[1]); // entity 3
            Assert.Equal(25f, mem.PositionsZ[2]); // entity 5
            Assert.Equal(20f, mem.PositionsZ[3]); // entity 2
        }
    }
}
