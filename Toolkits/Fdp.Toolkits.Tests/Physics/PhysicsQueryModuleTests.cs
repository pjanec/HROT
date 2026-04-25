using Fdp.Toolkit.Physics.Modules;
using Fdp.Toolkit.Physics.Systems;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    public class PhysicsQueryModuleTests
    {
        [Fact]
        public void PhysicsQueryModule_RegistersRaycastAndHitSystems()
        {
            var module = new PhysicsQueryModule();

            // Assert -- exactly two IEcsModuleSystem instances are exposed.
            Assert.Equal(2, module.InputSystems.Count);
            Assert.Contains(module.InputSystems, s => s is RaycastSolverSystem);
            Assert.Contains(module.InputSystems, s => s is HitResolutionSystem);
        }
    }
}
