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
        public void MaxTrackedTargets_ConstantValueIsFour()
        {
            // The constant drives all fixed-array sizes; if it ever changes, tests will catch the drift.
            Assert.Equal(4, PerceptionConstants.MaxTrackedTargets);
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
            // Arrange — fill with four entries in ascending score order
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 1L, 0f, 0f, 10f, 0u);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 2L, 0f, 0f, 20f, 0u);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 3L, 0f, 0f, 30f, 0u);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 4L, 0f, 0f, 40f, 0u);
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count); // table full

            // Act — add a fifth entry whose score (25) exceeds the current lowest (10)
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 5L, 0f, 0f, 25f, 0u);

            // Assert — count stays at MaxTrackedTargets and entity 1 (score 10) was evicted
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count);
            bool entity1Present = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 1L) entity1Present = true;
            Assert.False(entity1Present, "Entity 1 (lowest score 10) should have been evicted.");

            // Verify the new entry is present
            bool entity5Present = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 5L) entity5Present = true;
            Assert.True(entity5Present, "Entity 5 (score 25) should be in the table after eviction.");
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
