using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Modules;
using Fdp.Toolkit.Physics.Systems;
using Moq;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    public class PhysicsQueryModuleTests
    {
        [Fact]
        public void PhysicsQueryModule_RegistersMaterializationSystem()
        {
            var module       = new PhysicsQueryModule();
            var mockRegistry = new Mock<ISystemRegistry>();

            module.RegisterSystems(mockRegistry.Object);

            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<RaycastResultMaterializationSystem>()), Times.Once);
        }
    }
}
