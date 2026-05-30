using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Xunit;

namespace Fdp.Toolkit.Squad.Primitives.Tests
{
    /// <summary>
    /// P1-02: Tests for <see cref="TacticalFeatureHandles"/>.
    /// Covers SC-P1-02-1 through SC-P1-02-3.
    /// </summary>
    public class TacticalFeatureHandlesTests
    {
        [Fact]
        public void Acquire_WritesActiveFeatureId()
        {
            // SC-P1-02-1
            SquadCognitiveState state = default;

            TacticalFeatureHandles.Acquire(ref state, 42u);
            Assert.Equal(42u, state.ActiveFeatureId);

            // Idempotent — calling again with same id must not change state.
            TacticalFeatureHandles.Acquire(ref state, 42u);
            Assert.Equal(42u, state.ActiveFeatureId);
        }

        [Fact]
        public void TryRefresh_MatchingDescriptor_ReturnsTrue()
        {
            // SC-P1-02-2
            SquadCognitiveState state = default;
            var descriptors = new DangerAreaDescriptor[]
            {
                new DangerAreaDescriptor { FeatureId = 10u },
                new DangerAreaDescriptor { FeatureId = 20u },
                new DangerAreaDescriptor { FeatureId = 30u },
            };

            TacticalFeatureHandles.Acquire(ref state, 20u);
            bool found = TacticalFeatureHandles.TryRefresh(ref state, descriptors, out var descriptor);
            Assert.True(found);
            Assert.Equal(20u, descriptor.FeatureId);

            // featureId=99 not acquired — TryRefresh on a different state where it's not set.
            SquadCognitiveState state2 = default;
            TacticalFeatureHandles.Acquire(ref state2, 99u);
            bool notFound = TacticalFeatureHandles.TryRefresh(ref state2, descriptors, out _);
            Assert.False(notFound);
        }

        [Fact]
        public void TryRefresh_EvictedDescriptor_ReturnsFalse_ActiveUnchanged()
        {
            // SC-P1-02-3
            SquadCognitiveState state = default;
            var descriptors = new DangerAreaDescriptor[]
            {
                new DangerAreaDescriptor { FeatureId = 10u },
                new DangerAreaDescriptor { FeatureId = 20u },
                new DangerAreaDescriptor { FeatureId = 30u },
            };

            TacticalFeatureHandles.Acquire(ref state, 20u);
            bool firstRefresh = TacticalFeatureHandles.TryRefresh(ref state, descriptors, out _);
            Assert.True(firstRefresh);

            // New span without featureId=20.
            var evictedDescriptors = new DangerAreaDescriptor[]
            {
                new DangerAreaDescriptor { FeatureId = 10u },
                new DangerAreaDescriptor { FeatureId = 30u },
            };
            bool secondRefresh = TacticalFeatureHandles.TryRefresh(ref state, evictedDescriptors, out _);
            Assert.False(secondRefresh);

            // ActiveFeatureId unchanged by failure.
            Assert.Equal(20u, state.ActiveFeatureId);
        }
    }
}
