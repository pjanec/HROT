using Hrot.Common.Scenario;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HrotScenarioEnvelope"/> (TASK-D07).
    /// </summary>
    public sealed class HrotScenarioEnvelopeTests
    {
        [Fact]
        public void PeekSubsystemType_ReturnsCorrectType()
        {
            const string json = "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\",\"SchemaVersion\":1},\"Entities\":{}}";
            Assert.Equal("Hrot.SimHost", HrotScenarioEnvelope.PeekSubsystemType(json));
        }

        [Fact]
        public void PeekSubsystemType_ReturnsNullForInvalidJson()
        {
            Assert.Null(HrotScenarioEnvelope.PeekSubsystemType("not json"));
        }

        [Fact]
        public void PeekSubsystemType_ReturnsNullWhenHeaderAbsent()
        {
            Assert.Null(HrotScenarioEnvelope.PeekSubsystemType("{\"Entities\":{}}"));
        }

        [Fact]
        public void IsMatchingSubsystem_TrueForExactMatch()
        {
            Assert.True(HrotScenarioEnvelope.IsMatchingSubsystem("Hrot.SimHost", "Hrot.SimHost"));
        }

        [Fact]
        public void IsMatchingSubsystem_FalseForCaseMismatch()
        {
            Assert.False(HrotScenarioEnvelope.IsMatchingSubsystem("hrot.simhost", "Hrot.SimHost"));
        }

        [Fact]
        public void IsMatchingSubsystem_FalseForNull()
        {
            Assert.False(HrotScenarioEnvelope.IsMatchingSubsystem(null, "Hrot.SimHost"));
        }
    }
}
