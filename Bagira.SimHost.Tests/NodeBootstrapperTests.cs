using System.Linq;
using Bagira.SimHost.Modules;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.CarKinem.Modules;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Modules;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NodeBootstrapper"/> role-based module composition (MOD1-P3T3).
    /// Tests verify which sub-modules are included or excluded for each <see cref="NodeRole"/>.
    /// </summary>
    public class NodeBootstrapperTests
    {
        private static DoctrineRegistry CreateRegistry() => new DoctrineRegistry();
        private static NetworkEntityMap CreateEntityMap() => new NetworkEntityMap();

        // ── AllInOne role ─────────────────────────────────────────────────────

        [Fact]
        public void NodeBootstrapper_AllInOne_RegistersAllModuleClasses()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.AllInOne,
                CreateRegistry(),
                CreateEntityMap());

            var modules = bootstrapper.RegisteredModules;

            Assert.Contains(modules, m => m is MissionControlModule);
            Assert.Contains(modules, m => m is CognitiveRuntimeModule);
            Assert.Contains(modules, m => m is ActionDispatchModule);
            Assert.Contains(modules, m => m is GroundKinematicsModule);
            Assert.Contains(modules, m => m is CombatModule);
            Assert.Contains(modules, m => m is DamageAssessmentModule);
        }

        [Fact]
        public void NodeBootstrapper_AllInOne_RegistersSixModules()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.AllInOne,
                CreateRegistry(),
                CreateEntityMap());

            Assert.Equal(6, bootstrapper.RegisteredModules.Count);
        }

        // ── Brain role ────────────────────────────────────────────────────────

        [Fact]
        public void NodeBootstrapper_Brain_DoesNotRegisterKinematicModule()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.Brain,
                CreateRegistry(),
                CreateEntityMap());

            Assert.DoesNotContain(bootstrapper.RegisteredModules, m => m is GroundKinematicsModule);
        }

        [Fact]
        public void NodeBootstrapper_Brain_RegistersMissionAndCognitiveModules()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.Brain,
                CreateRegistry(),
                CreateEntityMap());

            Assert.Contains(bootstrapper.RegisteredModules, m => m is MissionControlModule);
            Assert.Contains(bootstrapper.RegisteredModules, m => m is CognitiveRuntimeModule);
        }

        /// <summary>BS1-T016: Brain must NOT carry the Combat module.</summary>
        [Fact]
        public void NodeBootstrapper_Brain_DoesNotRegisterCombatModule()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.Brain,
                CreateRegistry(),
                CreateEntityMap());

            Assert.DoesNotContain(bootstrapper.RegisteredModules, m => m is CombatModule);
        }

        // ── MuscleGround role ─────────────────────────────────────────────────

        [Fact]
        public void NodeBootstrapper_MuscleGround_DoesNotRegisterCognitiveModules()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.MuscleGround,
                CreateRegistry(),
                CreateEntityMap());

            Assert.DoesNotContain(bootstrapper.RegisteredModules, m => m is MissionControlModule);
            Assert.DoesNotContain(bootstrapper.RegisteredModules, m => m is CognitiveRuntimeModule);
        }

        [Fact]
        public void NodeBootstrapper_MuscleGround_RegistersKinematicModule()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.MuscleGround,
                CreateRegistry(),
                CreateEntityMap());

            Assert.Contains(bootstrapper.RegisteredModules, m => m is GroundKinematicsModule);
        }

        /// <summary>BS1-T016: MuscleGround must carry DamageAssessmentModule.</summary>
        [Fact]
        public void NodeBootstrapper_MuscleGround_RegistersDamageAssessmentModule()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.MuscleGround,
                CreateRegistry(),
                CreateEntityMap());

            Assert.Contains(bootstrapper.RegisteredModules, m => m is DamageAssessmentModule);
            Assert.DoesNotContain(bootstrapper.RegisteredModules, m => m is MissionControlModule);
        }

        // ── ImageGenerator role ───────────────────────────────────────────────

        [Fact]
        public void NodeBootstrapper_ImageGenerator_RegistersNoSimulationModules()
        {
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.ImageGenerator,
                CreateRegistry(),
                CreateEntityMap());

            Assert.Empty(bootstrapper.RegisteredModules);
        }

        // ── BuildSimulationLogic returns SimulationLogicModule ─────────────────

        [Fact]
        public void NodeBootstrapper_BuildSimulationLogic_ReturnsNonNull()
        {
            var bootstrapper = new NodeBootstrapper();
            var simLogic = bootstrapper.BuildSimulationLogic(
                NodeRole.AllInOne,
                CreateRegistry(),
                CreateEntityMap());

            Assert.NotNull(simLogic);
        }

        // ── RegisteredModules ordering ────────────────────────────────────────

        [Fact]
        public void NodeBootstrapper_AllInOne_ModulesInDependencyOrder()
        {
            // MissionControl and Cognitive must come before ActionDispatch which comes before GroundKinematics.
            var bootstrapper = new NodeBootstrapper();
            bootstrapper.BuildSimulationLogic(
                NodeRole.AllInOne,
                CreateRegistry(),
                CreateEntityMap());

            var modules = bootstrapper.RegisteredModules.ToList();
            int mcIdx   = modules.FindIndex(m => m is MissionControlModule);
            int crIdx   = modules.FindIndex(m => m is CognitiveRuntimeModule);
            int adIdx   = modules.FindIndex(m => m is ActionDispatchModule);
            int gkIdx   = modules.FindIndex(m => m is GroundKinematicsModule);

            Assert.True(mcIdx < adIdx, "MissionControl must precede ActionDispatch");
            Assert.True(crIdx < adIdx, "CognitiveRuntime must precede ActionDispatch");
            Assert.True(adIdx < gkIdx, "ActionDispatch must precede GroundKinematics");
        }
    }
}
