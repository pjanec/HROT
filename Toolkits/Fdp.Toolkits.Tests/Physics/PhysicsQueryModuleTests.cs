using Fdp.Kernel;
using Fdp.Toolkit.Physics.Modules;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    public class PhysicsQueryModuleTests
    {
        [Fact]
        public void PhysicsQueryModule_RegistersRaycastAndHitSystems()
        {
            // Arrange – SystemGroup.AddSystem requires an initialised world.
            var world = new EntityRepository();
            var group = new SystemGroup();
            group.Create(world);

            var module = new PhysicsQueryModule();

            // Act
            module.RegisterSystems(group);

            // Assert – exactly two ComponentSystems were added to the group.
            Assert.Equal(2, group.SystemCount);
        }
    }
}
