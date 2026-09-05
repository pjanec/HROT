using System.Numerics;
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
            using var module = new GroundKinematicsModule();
            Assert.NotNull(module.TrajectoryPool);
            Assert.NotNull(module.FormationTemplates);
        }

        // ── B3: the pool is a RESOURCE with an owner, not a field ─────────────

        /// <summary>
        /// <b>The rail the split exists for.</b> A node selecting both <c>MuscleGround</c> and
        /// <c>NavigationSolver</c> must share ONE trajectory pool: <c>PathfindingSolverSystem</c> writes
        /// resolved routes into it and <c>FormationTargetSystem</c>/<c>CarKinematicsSystem</c> read them
        /// back by handle. Two pools mean routes resolve and vehicles never follow them — silently.
        /// </summary>
        [Fact]
        public void BothCapabilitiesHandedOnePool_ShareIt_AndNeitherOwnsIt()
        {
            using var pool = new TrajectoryPoolManager();

            var muscleGround     = new GroundKinematicsModule(default, pool, null);
            var navigationSolver = new Fdp.Toolkit.Navigation.Modules.NavigationSolverModule(default, pool);

            Assert.Same(pool, muscleGround.TrajectoryPool);
            Assert.Same(pool, navigationSolver.TrajectoryPool);

            // The route the SOLVER resolves must be readable by the KINEMATICS side — same pool, one handle.
            var route = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0) };
            navigationSolver.TrajectoryPool.RegisterTrajectoryWithKey(route, key: 77);
            Assert.True(muscleGround.TrajectoryPool.TryGetTrajectory(77, out _));

            // Borrowed, so the borrower must not free it — the half that CORRUPTS rather than merely leaks.
            Assert.False(muscleGround.OwnsTrajectoryPool);
            muscleGround.Dispose();
            Assert.True(pool.TryGetTrajectory(77, out _));   // still live after the borrower disposed
        }

        /// <summary>
        /// <c>NavigationSolverModule</c> used to read <c>trajectoryPool ?? new TrajectoryPoolManager()</c>.
        /// It must now REFUSE rather than quietly hand itself a private pool — the silent-default pattern,
        /// caught before role composition switches this module on (it has no production caller today).
        /// </summary>
        [Fact]
        public void ANavigationSolverWithNoPoolIsRefused_NotSilentlyGivenItsOwn()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new Fdp.Toolkit.Navigation.Modules.NavigationSolverModule(default, null!));
        }

        /// <summary>A module that allocated its own pool owns it, and Dispose is idempotent.</summary>
        [Fact]
        public void AModuleWithNoPoolOwnsAndFreesItsOwn()
        {
            var module = new GroundKinematicsModule();
            Assert.True(module.OwnsTrajectoryPool);
            Assert.True(module.OwnsFormationTemplates);

            module.Dispose();
            module.Dispose();   // a double free would corrupt the allocator
        }
    }
}
