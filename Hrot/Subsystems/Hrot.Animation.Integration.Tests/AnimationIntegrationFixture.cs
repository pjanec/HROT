using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Time.Controllers;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Systems;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Animation.Integration.Tests;

/// <summary>
/// Shared fixture for animation integration scenarios (ANC-P7-03).
/// Implements IPumpableHarness to support PumpUntil frame-budgeted execution.
/// Implements IDisposable for xUnit IClassFixture lifecycle management.
///
/// The fixture creates a real EntityRepository, FakeAnimationBackend, BakedAnimationCache,
/// and all eight AnimationMuscleModule systems. Each scenario calls SpawnHumanoid() and
/// ResetWorld() to get a clean entity with deterministic state.
///
/// Systems run in correct order per DD-1 SS17:
///   Simulation    : CapabilityChangeReactor, Dispatcher, LookAtDispatcher,
///                   MontageQueueAdvance, RuntimeBridge
///   PostSimulation: NotifyEventEmitter, StateReporter, BackendCleanup
/// </summary>
public sealed class AnimationIntegrationFixture : IPumpableHarness, IDisposable
{
    // ── Backend + cache ─────────────────────────────────────────────────────

    private readonly FakeAnimationBackend _backend;
    private readonly BakedAnimationCache _cache;

    // ── ECS world ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public EntityRepository World { get; }

    /// <inheritdoc/>
    public FdpEventBus EventBus => World.Bus;

    /// <inheritdoc/>
    public SteppingTimeController Time { get; }

    // ── Systems (in execution order) ─────────────────────────────────────────

    private readonly AnimationCapabilityChangeReactorSystem _capabilityReactor;
    private readonly AnimationDispatcherSystem _dispatcher;
    private readonly LookAtDispatcherSystem _lookAtDispatcher;
    private readonly StanceTransitionSystem _stanceTransition;
    private readonly MontageQueueAdvanceSystem _queueAdvance;
    private readonly AnimationRuntimeBridgeSystem _bridge;
    private readonly NotifyEventEmitterSystem _notifyEmitter;
    private readonly AnimationStateReporterSystem _stateReporter;
    private readonly AnimationBackendCleanupSystem _backendCleanup;

    // ── Constructor ──────────────────────────────────────────────────────────

    public AnimationIntegrationFixture()
    {
        // Bake test data into the backend's class dictionary and cache
        var dto = TestData.CreateCharacterDef();
        var baked = BakingUtils.BakeDef(dto);
        var classData = new Dictionary<long, CharacterAnimationBakedData>
        {
            [TestData.ClassId] = baked,
        };

        _backend = new FakeAnimationBackend(classData);

        _cache = new BakedAnimationCache(null);
        _cache.GetOrBake(TestData.ClassId, dto);

        // Set up the ECS world and register all component types used by the eight systems
        World = new EntityRepository();

        World.RegisterComponent<AnimationChannel>();
        World.RegisterComponent<LookAtChannel>();
        World.RegisterComponent<StanceStatus>();
        World.RegisterComponent<StanceIntent>();
        World.RegisterComponent<AnimationMontageQueue>();
        World.RegisterComponent<AnimationMontageQueueState>();
        World.RegisterComponent<CharacterAnimationDefRuntime>();
        World.RegisterComponent<AnimationExecutorState>();
        World.RegisterComponent<LookAtExecutorState>();
        World.RegisterComponent<ActorCapabilityState>();
        World.RegisterComponent<PreviousCapabilities>();

        // Events read by dispatchers and published by state reporter
        World.RegisterEvent<DestructionOrder>();
        World.RegisterEvent<MontageEndedEvent>();
        World.RegisterEvent<StanceChangedEvent>();
        World.RegisterEvent<AnimNotifyEvent>();

        // Create all eight systems
        _capabilityReactor = new AnimationCapabilityChangeReactorSystem(_backend);
        _dispatcher = new AnimationDispatcherSystem(_backend, _cache);
        _lookAtDispatcher = new LookAtDispatcherSystem(_backend);
        _stanceTransition = new StanceTransitionSystem(_backend);
        _queueAdvance = new MontageQueueAdvanceSystem(_backend, _cache);
        _bridge = new AnimationRuntimeBridgeSystem(_backend, _cache);
        _notifyEmitter = new NotifyEventEmitterSystem(_backend);
        _stateReporter = new AnimationStateReporterSystem(_backend);
        _backendCleanup = new AnimationBackendCleanupSystem(_backend);

        Time = new SteppingTimeController(new GlobalTime());
    }

    // ── IPumpableHarness ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void PumpFrame(float dt = 1f / 60f)
    {
        // Simulation phase (systems 1-5)
        _capabilityReactor.Execute(World, dt);
        _dispatcher.Execute(World, dt);
        _lookAtDispatcher.Execute(World, dt);
        _stanceTransition.Execute(World, dt);
        _queueAdvance.Execute(World, dt);
        _bridge.Execute(World, dt);

        // PostSimulation phase (systems 6-8)
        _notifyEmitter.Execute(World, dt);
        _stateReporter.Execute(World, dt);
        _backendCleanup.Execute(World, dt);

        // Swap event buffers so events published this frame are readable next frame
        World.Bus.SwapBuffers();
    }

    /// <inheritdoc/>
    public void PumpFrames(int count, float dt = 1f / 60f)
    {
        for (int i = 0; i < count; i++)
            PumpFrame(dt);
    }

    /// <inheritdoc/>
    public void PumpUntil(
        Func<bool> condition,
        int maxFrames,
        string conditionName,
        Func<string>? diagnosticDump = null,
        float dt = 1f / 60f)
    {
        this.PumpUntilImpl(condition, maxFrames, conditionName, diagnosticDump, dt);
    }

    // ── Entity lifecycle ─────────────────────────────────────────────────────

    /// <summary>
    /// Spawn a humanoid entity with all animation components initialized.
    /// BackendHandle starts as ClassId (registration deferred to first bridge tick).
    /// </summary>
    public Entity SpawnHumanoid(
        ActorCapabilities capabilities = ActorCapabilities.CanPlayAnimations
                                         | ActorCapabilities.CanChangeStance
                                         | ActorCapabilities.CanAim)
    {
        var entity = World.CreateEntity();

        World.AddComponent(entity, new AnimationChannel { Status = NodeStatus.Failure });
        World.AddComponent(entity, new LookAtChannel { Status = NodeStatus.Failure });
        World.AddComponent(entity, new StanceStatus
        {
            CurrentStance = StanceId.Standing,
            Phase = StanceTransitionPhase.Idle,
        });
        World.AddComponent(entity, new StanceIntent
        {
            TargetStance = StanceId.Standing,
            BlendTime = 0.3f,
            Version = 0,
        });
        World.AddComponent(entity, new AnimationMontageQueue { Count = 0, QueueVersion = 0 });
        World.AddComponent(entity, new AnimationMontageQueueState());
        World.AddComponent(entity, new CharacterAnimationDefRuntime
        {
            BackendHandle = TestData.ClassId,
            StanceCount = 2,
            SlotCount = 2,
        });
        World.AddComponent(entity, new AnimationExecutorState());
        World.AddComponent(entity, new LookAtExecutorState());
        World.AddComponent(entity, new ActorCapabilityState { Capabilities = capabilities });

        return entity;
    }

    /// <summary>
    /// Destroy all entities and drain the event bus.
    /// Call between scenarios to reset world state.
    /// </summary>
    public void ResetWorld()
    {
        // Collect and destroy all live entities
        var all = World.Query().Build();
        foreach (var entity in all)
            World.DestroyEntity(entity);

        // Drain both event buffers so no stale events cross scenario boundaries
        World.Bus.SwapBuffers();
        World.Bus.SwapBuffers();
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        World.Dispose();
    }
}
