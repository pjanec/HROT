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
            // Arrange — minimal world; OnCreate for these systems does not require
            // component registrations (SpatialHashSystem creates its grid lazily,
            // CarKinematicsSystem OnCreate only initialises a stopwatch).
            var repo   = new EntityRepository();
            var module = new GroundKinematicsModule();

            var group = new SystemGroup();
            group.Create(repo);

            // Act
            module.RegisterSystems(group);

            // Assert — 6 systems expected (LinearKinematicsSystem was added in CT-MOD1-F)
            var systems = group.GetSystems();
            Assert.Equal(6, systems.Count);
            Assert.Contains(systems, s => s is SpatialHashSystem);
            Assert.Contains(systems, s => s is FormationTargetSystem);
            Assert.Contains(systems, s => s is VehicleCommandSystem);
            Assert.Contains(systems, s => s is CarKinematicsSystem);
            Assert.Contains(systems, s => s is NavigationExecutionSystem);
            Assert.Contains(systems, s => s is LinearKinematicsSystem);

            group.Dispose();
            repo.Dispose();
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
