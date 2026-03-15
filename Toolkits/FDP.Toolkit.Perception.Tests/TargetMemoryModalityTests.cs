using FDP.Toolkit.Perception.Components;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
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
            // Arrange — fill table with 4 entries detected via Radar
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, 1L, 0f, 0f, 10f, 0u, SensorModality.Radar);
            TargetMemory.AddOrUpdateTarget(ref mem, 2L, 0f, 0f, 20f, 0u, SensorModality.Radar);
            TargetMemory.AddOrUpdateTarget(ref mem, 3L, 0f, 0f, 30f, 0u, SensorModality.Radar);
            TargetMemory.AddOrUpdateTarget(ref mem, 4L, 0f, 0f, 40f, 0u, SensorModality.Radar);
            Assert.Equal(PerceptionConstants.MaxTrackedTargets, mem.Count);

            // Act — add a new entity with Thermal that surpasses the lowest score (10)
            TargetMemory.AddOrUpdateTarget(ref mem, 5L, 1f, 1f, 25f, 1u, SensorModality.Thermal);

            // Assert — entity 5 is in the table; its slot carries only Thermal modality
            bool found = false;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == 5L)
                {
                    found = true;
                    Assert.Equal((byte)SensorModality.Thermal, mem.Modalities[i]);
                    break;
                }
            }
            Assert.True(found, "Entity 5 (score 25) should be in the table after evicting entity 1 (score 10).");

            // Entity 1 must have been evicted
            bool entity1Present = false;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == 1L) entity1Present = true;
            Assert.False(entity1Present, "Entity 1 (lowest score 10) should have been evicted.");
        }
    }
}
