using Fdp.Toolkit.Perception.Components;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SensorModality"/> bitmask fusion and eviction
    /// behaviour in <see cref="TargetMemory"/> (MOD1-P6T1).
    /// </summary>
    public class TargetMemoryModalityTests
    {
        // ── Test 1: Modality fusion ────────────────────────────────────────────

        /// <summary>
        /// Adding the same entity with <c>Visual</c> and then with <c>Radar</c> must
        /// OR the two modality bits together so that both are recorded.
        /// </summary>
        [Fact]
        public unsafe void TargetMemory_ModalityFusion_OrsModalities()
        {
            // Arrange
            var mem = new TargetMemory();
            const long entityId = 1L;

            // Act — first observation via visual
            TargetMemory.AddOrUpdateTarget(ref mem, entityId, 0f, 0f, 10f, 1u, SensorModality.Visual);
            // Second observation via radar — must OR the modalities
            TargetMemory.AddOrUpdateTarget(ref mem, entityId, 0f, 0f, 5f,  2u, SensorModality.Radar);

            // Assert — both modalities recorded in slot 0
            var expected = (byte)(SensorModality.Visual | SensorModality.Radar);
            Assert.Equal(expected, mem.Modalities[0]);
        }

        // ── Test 2: Eviction resets modality ──────────────────────────────────

        /// <summary>
        /// When a slot is evicted (table full, new entry with higher score replaces
        /// lowest-score slot), the evicted slot's modality must be reset to the new
        /// entry's modality (not carry over the old one).
        /// </summary>
        [Fact]
        public unsafe void TargetMemory_Eviction_ResetsModality()
        {
            // Arrange — fill all 16 slots detected via Radar (P0.03: MaxTrackedTargets = 16).
            // Entity 1 gets the lowest score (10) and will be the eviction candidate.
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, 1L, 0f, 0f, 10f, 0u, SensorModality.Radar);
            for (int i = 2; i <= PerceptionConstants.MaxTrackedTargets; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, (long)i, 0f, 0f, (float)(i * 10), 0u, SensorModality.Radar);
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count);

            // Act — add a new entity (ID 100) with Thermal modality that surpasses the lowest score (10, entity 1)
            TargetMemory.AddOrUpdateTarget(ref mem, 100L, 1f, 1f, 25f, 1u, SensorModality.Thermal);

            // Assert — entity 100 is in the table; its slot carries only Thermal modality
            bool found = false;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == 100L)
                {
                    found = true;
                    Assert.Equal((byte)SensorModality.Thermal, mem.Modalities[i]);
                    break;
                }
            }
            Assert.True(found, "Entity 100 (score 25) should be in the table after evicting entity 1 (score 10).");

            // Entity 1 (lowest score 10) must have been evicted
            bool entity1Present = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 1L) entity1Present = true;
            Assert.False(entity1Present, "Entity 1 (lowest score 10) should have been evicted.");
        }
    }
}
