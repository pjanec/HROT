using Moq;
using FDP.Toolkit.Perception.Modules;
using FDP.Toolkit.Perception.Systems;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    public class AutonomousPerceptionModuleTests
    {
        [Fact]
        public void AutonomousPerceptionModule_RegisterSystems_DoesNotRegisterSystems()
        {
            // Arrange
            // AutonomousPerceptionModule uses the direct-execution Tick() pattern (same as
            // PerceptionModule): all four systems are called inside Tick() rather than
            // delegated to the kernel system scheduler. RegisterSystems() must be empty so
            // the kernel does NOT try to schedule them via [UpdateInPhase].
            using var module = new AutonomousPerceptionModule();
            var mockRegistry = new Mock<ISystemRegistry>();

            // Act
            module.RegisterSystems(mockRegistry.Object);

            // Assert — zero registrations (systems run directly via Tick, not via scheduler).
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<LocalGridBuilderSystem>()),   Times.Never);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<VisionBroadphaseSystem>()),   Times.Never);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<LosRequestBatchingSystem>()), Times.Never);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<ThreatEvaluationSystem>()),   Times.Never);
        }
    }
}
