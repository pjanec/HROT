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
        public void AutonomousPerceptionModule_RegistersAllPerceptionSystems()
        {
            // Arrange
            using var module = new AutonomousPerceptionModule();
            var mockRegistry = new Mock<ISystemRegistry>();

            // Act
            module.RegisterSystems(mockRegistry.Object);

            // Assert – all four perception systems are registered via the registry.
            // LosRequestBatchingSystem implements IModuleSystem only (no ComponentSystem base),
            // so it runs exclusively on the background thread inside Tick().
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<LocalGridBuilderSystem>()),   Times.Once);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<VisionBroadphaseSystem>()),   Times.Once);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<LosRequestBatchingSystem>()), Times.Once);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<ThreatEvaluationSystem>()),   Times.Once);
        }
    }
}
