using System;
using Fdp.Kernel;

namespace Fdp.Examples.UrbanCombat
{
    /// <summary>
    /// Orchestrator for the headless "Urban Ambush" demo simulation.
    /// <para>
    /// Call <see cref="Initialize"/> once to register all components and build the system
    /// pipeline, then call <see cref="Run"/> to execute the 600-frame (10-second at 60 Hz)
    /// simulation loop.
    /// </para>
    /// <para>
    /// For BATCH-14 this is a correctly-structured stub — system registration and actor
    /// spawning via <c>ScenarioDirector</c> will be wired in BCS-P7-T7/T8.
    /// TelemetryReporterSystem (BCS-P7-T8) will emit structured telemetry to <c>Console.Out</c>.
    /// </para>
    /// </summary>
    public class HeadlessDemoApp : IDisposable
    {
        // ── Constants ────────────────────────────────────────────────────────────────

        /// <summary>Simulation timestep: 60 Hz → ~16.67 ms per frame.</summary>
        private const float Dt = 1f / 60f;

        /// <summary>Total frames for the 10-second Urban Ambush scenario.</summary>
        private const int TotalFrames = 600;

        // ── State ────────────────────────────────────────────────────────────────────

        /// <summary>The ECS world that owns all entity data and system execution.</summary>
        public EntityRepository World { get; private set; }

        private bool _initialized;
        private bool _disposed;

        // ── Lifecycle ────────────────────────────────────────────────────────────────

        public HeadlessDemoApp()
        {
            World = new EntityRepository();
        }

        /// <summary>
        /// Registers all ECS component types and builds the system pipeline.
        /// Must be called exactly once before <see cref="Run"/>.
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
                throw new InvalidOperationException("HeadlessDemoApp.Initialize() called more than once.");

            // 1. Register all component types used by the demo.
            RegisterComponents();

            // 2. Register and configure systems (stubs pointing to correct system types).
            //    Full wiring in BCS-P7-T4 (TrafficBrainSystem) through BCS-P7-T8 (Telemetry).
            RegisterSystems();

            _initialized = true;
        }

        /// <summary>
        /// Runs the 600-frame Urban Ambush scenario.
        /// Prints a completion message when the simulation finishes.
        /// </summary>
        public void Run()
        {
            if (!_initialized)
                throw new InvalidOperationException("HeadlessDemoApp.Initialize() must be called before Run().");

            // Simulation loop: 600 frames at 60 Hz = 10 seconds real-time equivalent.
            for (int frame = 0; frame < TotalFrames; frame++)
            {
                World.SetSimulationTime(frame * Dt);
                World.Tick();
            }

            Console.WriteLine("[UrbanAmbush] Simulation complete.");
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        private void RegisterComponents()
        {
            // Fdp.Kernel universal spatial primitives
            World.RegisterComponent<SimTransform>();
            World.RegisterComponent<SimVelocity>();
            World.RegisterComponent<HealthData>();

            // FDP.Toolkit.Behavior
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.DoctrineState>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.SimTier>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainBlackboard>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainBTreeState>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm128>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm64>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.ActorCapabilityState>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.PreviousCapabilities>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.WeaponChannel>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.InteractionChannel>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.PassengerBuffer>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.IsEmbarkedTag>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.MissionPlanQueue>();

            // FDP.Toolkit.Perception
            World.RegisterComponent<FDP.Toolkit.Perception.Components.Faction>();
            World.RegisterComponent<FDP.Toolkit.Perception.Components.PerceptionReceptor>();
            World.RegisterComponent<FDP.Toolkit.Perception.Components.TargetMemory>();

            // FDP.Toolkit.Physics
            World.RegisterComponent<FDP.Toolkit.Physics.Components.PhysicsCollider>();

            // FDP.Toolkit.Combat
            World.RegisterComponent<FDP.Toolkit.Combat.Components.WeaponState>();
            World.RegisterComponent<FDP.Toolkit.Combat.Components.Health>();
            World.RegisterComponent<FDP.Toolkit.Combat.Components.BallisticProjectile>();

            // FDP.Toolkit.CarKinem
            World.RegisterComponent<CarKinem.Core.VehicleState>();
            World.RegisterComponent<CarKinem.Core.VehicleParams>();
            World.RegisterComponent<CarKinem.Core.NavState>();
        }

        private void RegisterSystems()
        {
            // Systems will be registered here in their correct pipeline order as they are
            // implemented in BCS-P7-T4 through BCS-P7-T8. For this batch (BCS-P7-T1) the
            // pipeline is intentionally empty — only the component registrations above are
            // required to prove the project scaffold is architecturally correct.
            //
            // Expected future order (see DESIGN.md §10):
            //   [Input]:       RaycastSolverSystem, HitResolutionSystem
            //   [BeforeSync]:  DoctrineIngressSystem, LosRequestBatchingSystem
            //   [Simulation]:  DamageSystem, AudioPerceptionSystem, MissionDirectorSystem,
            //                  ChannelArbitrationSystem, HsmDamageBridgeSystem,
            //                  TrafficBrainSystem, BTreeTickSystem, HsmTickSystems,
            //                  InteractionDispatcher, LocomotionDispatcher, WeaponDispatcher,
            //                  FireProcessingSystem
            //   [PostSim]:     BallisticsSystem, LinearKinematicsSystem, CarKinematicsSystem,
            //                  SpatialHashSystem
            //   [Export]:      TelemetryReporterSystem
        }

        // ── IDisposable ───────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                World?.Dispose();
                _disposed = true;
            }
        }
    }
}
