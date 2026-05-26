using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Systems;

namespace Hrot.MuscleCharacter.Animation
{
    /// <summary>
    /// ECS module registering all Muscle-character animation systems in the mandatory
    /// phase order defined by DD-1 §17.
    ///
    /// Simulation (early to late):
    ///   1. AnimationCapabilityChangeReactorSystem  -- before dispatchers see capability state
    ///   2. AnimationDispatcherSystem               -- routes AnimationChannel commands
    ///   3. LookAtDispatcherSystem                  -- routes LookAtChannel commands
    ///   4. MontageQueueAdvanceSystem               -- advances queue before bridge applies slots
    ///   5. AnimationRuntimeBridgeSystem            -- calls backend with staged intents
    ///
    /// PostSimulation (early to late):
    ///   6. NotifyEventEmitterSystem                -- drains backend notifies after bridge.Tick
    ///   7. AnimationStateReporterSystem            -- synthesizes completion events and Status
    ///   8. AnimationBackendCleanupSystem           -- unregisters destroyed entities (late)
    /// </summary>
    public sealed class AnimationMuscleModule : IEcsModule
    {
        public string Name => "AnimationMuscle";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly AnimationCapabilityChangeReactorSystem _capabilityReactor;
        private readonly AnimationDispatcherSystem _dispatcher;
        private readonly LookAtDispatcherSystem _lookAtDispatcher;
        private readonly MontageQueueAdvanceSystem _montageQueueAdvance;
        private readonly AnimationRuntimeBridgeSystem _bridge;
        private readonly NotifyEventEmitterSystem _notifyEmitter;
        private readonly AnimationStateReporterSystem _stateReporter;
        private readonly AnimationBackendCleanupSystem _cleanup;

        public AnimationMuscleModule(IAnimationBackend backend, BakedAnimationCache cache)
        {
            _capabilityReactor = new AnimationCapabilityChangeReactorSystem(backend);
            _dispatcher = new AnimationDispatcherSystem(backend, cache);
            _lookAtDispatcher = new LookAtDispatcherSystem(backend);
            _montageQueueAdvance = new MontageQueueAdvanceSystem(backend, cache);
            _bridge = new AnimationRuntimeBridgeSystem(backend, cache);
            _notifyEmitter = new NotifyEventEmitterSystem(backend);
            _stateReporter = new AnimationStateReporterSystem(backend);
            _cleanup = new AnimationBackendCleanupSystem(backend);
        }

        /// <summary>
        /// Registers all 8 animation systems in the correct phase order (DD-1 §17).
        /// Order within each phase is guaranteed by the registration sequence.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // Simulation phase (1–5)
            registry.RegisterSystem(_capabilityReactor);
            registry.RegisterSystem(_dispatcher);
            registry.RegisterSystem(_lookAtDispatcher);
            registry.RegisterSystem(_montageQueueAdvance);
            registry.RegisterSystem(_bridge);

            // PostSimulation phase (6–8)
            registry.RegisterSystem(_notifyEmitter);
            registry.RegisterSystem(_stateReporter);
            registry.RegisterSystem(_cleanup);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
