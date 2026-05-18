using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.CarKinem.Modules;
using Fdp.Toolkit.CarKinem.Systems;
using Xunit;

namespace CarKinem.Tests.Modules
{
    /// <summary>
    /// Verifies that <see cref="GroundKinematicsModule"/> registers exactly the expected
    /// ground-kinematics systems into a <see cref="SystemGroup"/> (MOD1-P2T4 success condition).
    /// </summary>
    public class GroundKinematicsModuleTests
    {
        [Fact]
        public void GroundKinematicsModule_RegistersAllKinematicSystems()
        {
            // Arrange
            var module = new GroundKinematicsModule();

            // Assert — 4 simulation + 2 post-simulation systems
            Assert.Equal(4, module.SimulationSystems.Count);
            Assert.Equal(2, module.PostSimulationSystems.Count);
            Assert.IsType<SpatialHashSystem>(module.SimulationSystems[0]);
            Assert.IsType<FormationTargetSystem>(module.SimulationSystems[1]);
            Assert.IsType<VehicleCommandSystem>(module.SimulationSystems[2]);
            Assert.IsType<NavigationExecutionSystem>(module.SimulationSystems[3]);
            Assert.IsType<CarKinematicsSystem>(module.PostSimulationSystems[0]);
            Assert.IsType<LinearKinematicsSystem>(module.PostSimulationSystems[1]);
        }

        [Fact]
        public void GroundKinematicsModule_ExposesTrajectoryPoolAndFormationTemplates()
        {
            var pool      = new TrajectoryPoolManager();
            var templates = new FormationTemplateManager();
            var module    = new GroundKinematicsModule(
                roadNetwork:        default,
                trajectoryPool:     pool,
                formationTemplates: templates);

            Assert.Same(pool,      module.TrajectoryPool);
            Assert.Same(templates, module.FormationTemplates);
        }

        [Fact]
        public void GroundKinematicsModule_CreatesDefaultPoolAndTemplatesWhenNull()
        {
            var module = new GroundKinematicsModule();
            Assert.NotNull(module.TrajectoryPool);
            Assert.NotNull(module.FormationTemplates);
        }
    }
}
