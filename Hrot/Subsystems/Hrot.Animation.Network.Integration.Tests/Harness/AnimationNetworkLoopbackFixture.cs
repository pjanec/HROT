using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication;
using Hrot.Animation.Replication.Translators.Channels;
using Hrot.Animation.Replication.Translators.Descriptors;
using Hrot.Animation.Replication.Translators.Events;
using Hrot.Animation.Replication.Translators.SideBuffers;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Systems;

namespace Hrot.Animation.Network.Integration.Tests;

/// <summary>
/// Two-node loopback harness for networked animation integration tests (ANC-P8-04).
///
/// Implements the Brain Brain->Muscle->Brain replication round-trip in-process
/// without a live DDS participant. Two separate EntityRepository instances
/// (BrainWorld and MuscleWorld) communicate via the actual animation replication
/// translators using internal CapturingWriter + ProcessSample seams.
///
/// Tick ordering per DD-2 SS8:
///   1. Brain egress: ScanAndPublish captures intent messages.
///   2. Route Brain->Muscle: intent ingress ProcessSample applies to MuscleWorld.
///   3. Muscle animation systems run (same eight systems as stage-1 fixture).
///   4. Muscle egress: ScanAndPublish captures status messages.
///   5. Muscle event egress: EncodeForTest captures event DDS messages.
///   6. Route Muscle->Brain: status/event ingress ProcessSample and Publish
///      apply to BrainWorld.
///   7. Swap both event buses.
///
/// Round-trip latency: ~2 ticks (one tick Brain->Muscle intent arrival,
/// one tick Muscle->Brain status/event arrival), matching DD-2 SS8.
/// Tests use extra PumpUntil budget (4-6 extra frames) to absorb this.
/// </summary>
public sealed class AnimationNetworkLoopbackFixture : IDisposable
{
    // ── Worlds ────────────────────────────────────────────────────────────────

    /// <summary>Brain EntityRepository (intent-author side).</summary>
    public EntityRepository BrainWorld { get; }

    /// <summary>Muscle EntityRepository (execution side).</summary>
    public EntityRepository MuscleWorld { get; }

    // ── Entity maps (one per world, same network IDs) ─────────────────────────

    private readonly NetworkEntityMap _brainEntityMap;
    private readonly NetworkEntityMap _muscleEntityMap;

    // ── Backend + baked cache (Muscle side only) ─────────────────────────────

    private readonly FakeAnimationBackend _backend;
    private readonly BakedAnimationCache _cache;

    // ── Muscle animation systems ──────────────────────────────────────────────

    private readonly AnimationCapabilityChangeReactorSystem _capabilityReactor;
    private readonly AnimationDispatcherSystem _dispatcher;
    private readonly LookAtDispatcherSystem _lookAtDispatcher;
    private readonly StanceTransitionSystem _stanceTransition;
    private readonly MontageQueueAdvanceSystem _queueAdvance;
    private readonly AnimationRuntimeBridgeSystem _bridge;
    private readonly NotifyEventEmitterSystem _notifyEmitter;
    private readonly AnimationStateReporterSystem _stateReporter;
    private readonly AnimationBackendCleanupSystem _backendCleanup;

    // ── Brain egress translators (CapturingWriter, reads BrainWorld) ──────────

    private readonly CapturingWriter<DdsAnimationChannelIntent> _brainChannelIntentCapture;
    private readonly AnimationChannelIntentEgressTranslator _brainChannelIntentEgress;

    private readonly CapturingWriter<DdsLookAtChannelIntent> _brainLookAtIntentCapture;
    private readonly LookAtChannelIntentEgressTranslator _brainLookAtIntentEgress;

    private readonly CapturingWriter<DdsStanceIntent> _brainStanceIntentCapture;
    private readonly StanceIntentEgressTranslator _brainStanceIntentEgress;

    private readonly CapturingWriter<DdsMontageQueue> _brainQueueCapture;
    private readonly AnimationMontageQueueEgressTranslator _brainQueueEgress;

    // ── Muscle ingress translators (null participant, reads from capture buffers) ──

    private readonly AnimationChannelIntentIngressTranslator _muscleChannelIntentIngress;
    private readonly LookAtChannelIntentIngressTranslator _muscleLookAtIntentIngress;
    private readonly StanceIntentIngressTranslator _muscleStanceIntentIngress;
    private readonly AnimationMontageQueueIngressTranslator _muscleQueueIngress;

    // ── Muscle egress translators (CapturingWriter, reads MuscleWorld) ────────

    private readonly CapturingWriter<DdsAnimationChannelStatus> _muscleChannelStatusCapture;
    private readonly AnimationChannelStatusEgressTranslator _muscleChannelStatusEgress;

    private readonly CapturingWriter<DdsLookAtChannelStatus> _muscleLookAtStatusCapture;
    private readonly LookAtChannelStatusEgressTranslator _muscleLookAtStatusEgress;

    private readonly CapturingWriter<DdsStanceStatus> _muscleStanceStatusCapture;
    private readonly StanceStatusEgressTranslator _muscleStanceStatusEgress;

    // ── Brain ingress translators (null participant, writes to BrainWorld) ────

    private readonly AnimationChannelStatusIngressTranslator _brainChannelStatusIngress;
    private readonly LookAtChannelStatusIngressTranslator _brainLookAtStatusIngress;
    private readonly StanceStatusIngressTranslator _brainStanceStatusIngress;

    // ── Event translators (encode on Muscle side, decode on Brain side) ───────

    private readonly MontageStartedEventTranslator _muscleStartedEgress;
    private readonly MontageStartedEventTranslator _brainStartedIngress;

    private readonly MontageEndedEventTranslator _muscleEndedEgress;
    private readonly MontageEndedEventTranslator _brainEndedIngress;

    private readonly StanceChangedEventTranslator _muscleStanceChangedEgress;
    private readonly StanceChangedEventTranslator _brainStanceChangedIngress;

    private readonly AnimNotifyEventTranslator _muscleNotifyEgress;
    private readonly AnimNotifyEventTranslator _brainNotifyIngress;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AnimationNetworkLoopbackFixture()
    {
        // Bake test data into backend
        var dto = TestData.CreateCharacterDef();
        var baked = BakingUtils.BakeDef(dto);
        var classData = new Dictionary<long, CharacterAnimationBakedData>
        {
            [TestData.ClassId] = baked,
        };
        _backend = new FakeAnimationBackend(classData);
        _cache = new BakedAnimationCache(null);
        _cache.GetOrBake(TestData.ClassId, dto);

        // Brain world
        BrainWorld = new EntityRepository();
        BrainWorld.RegisterComponent<AnimationChannel>();
        BrainWorld.RegisterComponent<LookAtChannel>();
        BrainWorld.RegisterComponent<StanceIntent>();
        BrainWorld.RegisterComponent<StanceStatus>();
        BrainWorld.RegisterComponent<AnimationMontageQueue>();
        BrainWorld.RegisterComponent<NetworkIdentity>();
        BrainWorld.RegisterEvent<MontageStartedEvent>();
        BrainWorld.RegisterEvent<MontageEndedEvent>();
        BrainWorld.RegisterEvent<StanceChangedEvent>();
        BrainWorld.RegisterEvent<AnimNotifyEvent>();

        _brainEntityMap = new NetworkEntityMap();

        // Muscle world
        MuscleWorld = new EntityRepository();
        MuscleWorld.RegisterComponent<AnimationChannel>();
        MuscleWorld.RegisterComponent<LookAtChannel>();
        MuscleWorld.RegisterComponent<StanceStatus>();
        MuscleWorld.RegisterComponent<StanceIntent>();
        MuscleWorld.RegisterComponent<AnimationMontageQueue>();
        MuscleWorld.RegisterComponent<AnimationMontageQueueState>();
        MuscleWorld.RegisterComponent<CharacterAnimationDefRuntime>();
        MuscleWorld.RegisterComponent<AnimationExecutorState>();
        MuscleWorld.RegisterComponent<LookAtExecutorState>();
        MuscleWorld.RegisterComponent<ActorCapabilityState>();
        MuscleWorld.RegisterComponent<PreviousCapabilities>();
        MuscleWorld.RegisterComponent<NetworkIdentity>();
        MuscleWorld.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
        MuscleWorld.RegisterEvent<MontageStartedEvent>();
        MuscleWorld.RegisterEvent<MontageEndedEvent>();
        MuscleWorld.RegisterEvent<StanceChangedEvent>();
        MuscleWorld.RegisterEvent<AnimNotifyEvent>();

        _muscleEntityMap = new NetworkEntityMap();

        // Muscle animation systems
        _capabilityReactor = new AnimationCapabilityChangeReactorSystem(_backend);
        _dispatcher = new AnimationDispatcherSystem(_backend, _cache);
        _lookAtDispatcher = new LookAtDispatcherSystem(_backend);
        _stanceTransition = new StanceTransitionSystem(_backend);
        _queueAdvance = new MontageQueueAdvanceSystem(_backend, _cache);
        _bridge = new AnimationRuntimeBridgeSystem(_backend, _cache);
        _notifyEmitter = new NotifyEventEmitterSystem(_backend);
        _stateReporter = new AnimationStateReporterSystem(_backend);
        _backendCleanup = new AnimationBackendCleanupSystem(_backend);

        // Brain egress translators (CapturingWriter)
        _brainChannelIntentCapture = new CapturingWriter<DdsAnimationChannelIntent>();
        _brainChannelIntentEgress = new AnimationChannelIntentEgressTranslator(
            _brainChannelIntentCapture, _brainEntityMap);

        _brainLookAtIntentCapture = new CapturingWriter<DdsLookAtChannelIntent>();
        _brainLookAtIntentEgress = new LookAtChannelIntentEgressTranslator(
            _brainLookAtIntentCapture, _brainEntityMap);

        _brainStanceIntentCapture = new CapturingWriter<DdsStanceIntent>();
        _brainStanceIntentEgress = new StanceIntentEgressTranslator(
            _brainStanceIntentCapture, _brainEntityMap);

        _brainQueueCapture = new CapturingWriter<DdsMontageQueue>();
        _brainQueueEgress = new AnimationMontageQueueEgressTranslator(
            _brainQueueCapture, _brainEntityMap);

        // Muscle ingress translators (null participant = test-only ProcessSample path)
        _muscleChannelIntentIngress = new AnimationChannelIntentIngressTranslator(
            participant: null, _muscleEntityMap);
        _muscleLookAtIntentIngress = new LookAtChannelIntentIngressTranslator(
            participant: null, _muscleEntityMap);
        _muscleStanceIntentIngress = new StanceIntentIngressTranslator(
            participant: null, _muscleEntityMap);
        _muscleQueueIngress = new AnimationMontageQueueIngressTranslator(
            participant: null, _muscleEntityMap);

        // Muscle egress translators (CapturingWriter)
        _muscleChannelStatusCapture = new CapturingWriter<DdsAnimationChannelStatus>();
        _muscleChannelStatusEgress = new AnimationChannelStatusEgressTranslator(
            _muscleChannelStatusCapture, _muscleEntityMap);

        _muscleLookAtStatusCapture = new CapturingWriter<DdsLookAtChannelStatus>();
        _muscleLookAtStatusEgress = new LookAtChannelStatusEgressTranslator(
            _muscleLookAtStatusCapture, _muscleEntityMap);

        _muscleStanceStatusCapture = new CapturingWriter<DdsStanceStatus>();
        _muscleStanceStatusEgress = new StanceStatusEgressTranslator(
            _muscleStanceStatusCapture, _muscleEntityMap);

        // Brain ingress translators (null participant)
        _brainChannelStatusIngress = new AnimationChannelStatusIngressTranslator(
            participant: null, _brainEntityMap);
        _brainLookAtStatusIngress = new LookAtChannelStatusIngressTranslator(
            participant: null, _brainEntityMap);
        _brainStanceStatusIngress = new StanceStatusIngressTranslator(
            participant: null, _brainEntityMap);

        // Event translators
        _muscleStartedEgress = new MontageStartedEventTranslator(
            participant: null, _muscleEntityMap, TranslatorDirection.Egress);
        _brainStartedIngress = new MontageStartedEventTranslator(
            participant: null, _brainEntityMap, TranslatorDirection.Ingress);

        _muscleEndedEgress = new MontageEndedEventTranslator(
            participant: null, _muscleEntityMap, TranslatorDirection.Egress);
        _brainEndedIngress = new MontageEndedEventTranslator(
            participant: null, _brainEntityMap, TranslatorDirection.Ingress);

        _muscleStanceChangedEgress = new StanceChangedEventTranslator(
            participant: null, _muscleEntityMap, TranslatorDirection.Egress);
        _brainStanceChangedIngress = new StanceChangedEventTranslator(
            participant: null, _brainEntityMap, TranslatorDirection.Ingress);

        _muscleNotifyEgress = new AnimNotifyEventTranslator(
            participant: null, _muscleEntityMap, TranslatorDirection.Egress);
        _brainNotifyIngress = new AnimNotifyEventTranslator(
            participant: null, _brainEntityMap, TranslatorDirection.Ingress);
    }

    // ── Entity lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a paired entity: one authority entity on the Brain world and one ghost on the
    /// Muscle world. Both share the same <paramref name="networkId"/>. The Brain entity has all
    /// intent components; the Muscle entity has all animation-pipeline components.
    /// Returns the Brain entity (the one callers write intents to).
    /// </summary>
    public Entity SpawnPairedHumanoid(long networkId)
    {
        // Brain entity: intent components + NetworkIdentity
        var brainEntity = BrainWorld.CreateEntity();
        BrainWorld.AddComponent(brainEntity, new NetworkIdentity(networkId));
        BrainWorld.AddComponent(brainEntity, new AnimationChannel { Status = NodeStatus.Failure });
        BrainWorld.AddComponent(brainEntity, new LookAtChannel { Status = NodeStatus.Failure });
        BrainWorld.AddComponent(brainEntity, new StanceIntent
        {
            TargetStance = StanceId.Standing,
            BlendTime = 0.3f,
            Version = 0,
        });
        BrainWorld.AddComponent(brainEntity, new StanceStatus
        {
            CurrentStance = StanceId.Standing,
            Phase = StanceTransitionPhase.Idle,
        });
        BrainWorld.AddComponent(brainEntity, new AnimationMontageQueue { Count = 0, QueueVersion = 0 });
        _brainEntityMap.Register(networkId, brainEntity);

        // Muscle entity: full animation pipeline components + NetworkIdentity
        var muscleEntity = MuscleWorld.CreateEntity();
        MuscleWorld.AddComponent(muscleEntity, new NetworkIdentity(networkId));
        MuscleWorld.AddComponent(muscleEntity, new AnimationChannel { Status = NodeStatus.Failure });
        MuscleWorld.AddComponent(muscleEntity, new LookAtChannel { Status = NodeStatus.Failure });
        MuscleWorld.AddComponent(muscleEntity, new StanceStatus
        {
            CurrentStance = StanceId.Standing,
            Phase = StanceTransitionPhase.Idle,
        });
        MuscleWorld.AddComponent(muscleEntity, new StanceIntent
        {
            TargetStance = StanceId.Standing,
            BlendTime = 0.3f,
            Version = 0,
        });
        MuscleWorld.AddComponent(muscleEntity, new AnimationMontageQueue { Count = 0, QueueVersion = 0 });
        MuscleWorld.AddComponent(muscleEntity, new AnimationMontageQueueState());
        MuscleWorld.AddComponent(muscleEntity, new CharacterAnimationDefRuntime
        {
            BackendHandle = TestData.ClassId,
            StanceCount = 2,
            SlotCount = 2,
        });
        MuscleWorld.AddComponent(muscleEntity, new AnimationExecutorState());
        MuscleWorld.AddComponent(muscleEntity, new LookAtExecutorState());
        MuscleWorld.AddComponent(muscleEntity, new ActorCapabilityState
        {
            Capabilities = ActorCapabilities.CanPlayAnimations
                         | ActorCapabilities.CanChangeStance
                         | ActorCapabilities.CanAim,
        });
        MuscleWorld.AddComponent(muscleEntity, new PreviousCapabilities());
        _muscleEntityMap.Register(networkId, muscleEntity);

        return brainEntity;
    }

    // ── Pump API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Advance simulation by one network-loopback frame.
    ///
    /// Round-trip sequence:
    ///   1. Brain egress captures intent changes.
    ///   2. Intent messages routed to Muscle via ProcessSample.
    ///   3. Muscle animation systems execute.
    ///   4. Muscle egress captures status changes.
    ///   5. Muscle events captured and encoded.
    ///   6. Status + events routed to Brain via ProcessSample / decoded publish.
    ///   7. Swap both event buses.
    /// </summary>
    public void PumpFrame(float dt = 1f / 60f)
    {
        // --- 1. Brain egress: capture intent ---
        _brainChannelIntentCapture.Written.Clear();
        _brainLookAtIntentCapture.Written.Clear();
        _brainStanceIntentCapture.Written.Clear();
        _brainQueueCapture.Written.Clear();

        _brainChannelIntentEgress.ScanAndPublish(BrainWorld);
        _brainLookAtIntentEgress.ScanAndPublish(BrainWorld);
        _brainStanceIntentEgress.ScanAndPublish(BrainWorld);
        _brainQueueEgress.ScanAndPublish(BrainWorld);

        // --- 2. Route Brain->Muscle (intent ingress) ---
        var muscleView = (ISimulationView)MuscleWorld;
        var muscleCmd = new EntityCommandBuffer();

        foreach (var msg in _brainChannelIntentCapture.Written)
            _muscleChannelIntentIngress.ProcessSample(msg, muscleCmd, muscleView);
        foreach (var msg in _brainLookAtIntentCapture.Written)
            _muscleLookAtIntentIngress.ProcessSample(msg, muscleCmd, muscleView);
        foreach (var msg in _brainStanceIntentCapture.Written)
            _muscleStanceIntentIngress.ProcessSample(msg, muscleCmd, muscleView);
        foreach (var msg in _brainQueueCapture.Written)
            _muscleQueueIngress.ProcessSample(msg, muscleCmd, muscleView);

        muscleCmd.Playback(MuscleWorld);

        // --- 3. Muscle animation systems execute ---
        _capabilityReactor.Execute(MuscleWorld, dt);
        _dispatcher.Execute(MuscleWorld, dt);
        _lookAtDispatcher.Execute(MuscleWorld, dt);
        _stanceTransition.Execute(MuscleWorld, dt);
        _queueAdvance.Execute(MuscleWorld, dt);
        _bridge.Execute(MuscleWorld, dt);
        _notifyEmitter.Execute(MuscleWorld, dt);
        _stateReporter.Execute(MuscleWorld, dt);
        _backendCleanup.Execute(MuscleWorld, dt);

        // --- 4. Muscle egress: capture status ---
        _muscleChannelStatusCapture.Written.Clear();
        _muscleLookAtStatusCapture.Written.Clear();
        _muscleStanceStatusCapture.Written.Clear();

        _muscleChannelStatusEgress.ScanAndPublish(MuscleWorld);
        _muscleLookAtStatusEgress.ScanAndPublish(MuscleWorld);
        _muscleStanceStatusEgress.ScanAndPublish(MuscleWorld);

        // --- 5. Capture Muscle events from this tick's read buffer ---
        var muscleStarted = MuscleWorld.Bus.Read<MontageStartedEvent>();
        var muscleEnded = MuscleWorld.Bus.Read<MontageEndedEvent>();
        var muscleStanceChanged = MuscleWorld.Bus.Read<StanceChangedEvent>();
        var muscleNotify = MuscleWorld.Bus.Read<AnimNotifyEvent>();

        // --- 6. Route Muscle->Brain (status + events) ---
        var brainView = (ISimulationView)BrainWorld;
        var brainCmd = new EntityCommandBuffer();

        foreach (var msg in _muscleChannelStatusCapture.Written)
            _brainChannelStatusIngress.ProcessSample(msg, brainCmd, brainView);
        foreach (var msg in _muscleLookAtStatusCapture.Written)
            _brainLookAtStatusIngress.ProcessSample(msg, brainCmd, brainView);
        foreach (var msg in _muscleStanceStatusCapture.Written)
            _brainStanceStatusIngress.ProcessSample(msg, brainCmd, brainView);

        // Route events: encode (muscleEntityMap) -> decode (brainEntityMap) -> publish on Brain bus
        foreach (ref readonly var evt in muscleStarted)
        {
            if (_muscleStartedEgress.EncodeForTest(evt, out var dds)
                && _brainStartedIngress.DecodeForTest(dds, out var brainEvt))
            {
                BrainWorld.Bus.Publish(brainEvt);
            }
        }
        foreach (ref readonly var evt in muscleEnded)
        {
            if (_muscleEndedEgress.EncodeForTest(evt, out var dds)
                && _brainEndedIngress.DecodeForTest(dds, out var brainEvt))
            {
                BrainWorld.Bus.Publish(brainEvt);
            }
        }
        foreach (ref readonly var evt in muscleStanceChanged)
        {
            if (_muscleStanceChangedEgress.EncodeForTest(evt, out var dds)
                && _brainStanceChangedIngress.DecodeForTest(dds, out var brainEvt))
            {
                BrainWorld.Bus.Publish(brainEvt);
            }
        }
        foreach (ref readonly var evt in muscleNotify)
        {
            if (_muscleNotifyEgress.EncodeForTest(evt, out var dds)
                && _brainNotifyIngress.DecodeForTest(dds, out var brainEvt))
            {
                BrainWorld.Bus.Publish(brainEvt);
            }
        }

        brainCmd.Playback(BrainWorld);

        // --- 7. Swap both event buses ---
        MuscleWorld.Bus.SwapBuffers();
        BrainWorld.Bus.SwapBuffers();
    }

    /// <summary>
    /// Advance simulation by multiple loopback frames.
    /// </summary>
    public void PumpFrames(int count, float dt = 1f / 60f)
    {
        for (int i = 0; i < count; i++)
            PumpFrame(dt);
    }

    /// <summary>
    /// Advance simulation until a Brain-side condition is true, or timeout.
    /// Uses <paramref name="maxFrames"/> as the frame budget.
    /// </summary>
    public void PumpUntil(
        Func<bool> condition,
        int maxFrames,
        string conditionName,
        float dt = 1f / 60f)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) return;
            PumpFrame(dt);
        }
        if (condition()) return;
        throw new TimeoutException(
            $"PumpUntil({conditionName}) did not become true within {maxFrames} frames " +
            $"({maxFrames * dt:F2}s).");
    }

    /// <summary>
    /// Destroy all test entities in both worlds and drain both event buses.
    /// Unregisters network IDs so they can be reused by the next test.
    /// </summary>
    public void ResetWorlds()
    {
        // Collect registered network IDs before destroying entities
        var netIds = new System.Collections.Generic.List<long>(
            _brainEntityMap.Entries.Keys);

        foreach (var e in BrainWorld.Query().Build())
            BrainWorld.DestroyEntity(e);
        foreach (var e in MuscleWorld.Query().Build())
            MuscleWorld.DestroyEntity(e);

        // Unregister all network IDs so they can be re-registered by the next test
        foreach (var netId in netIds)
        {
            _brainEntityMap.Unregister(netId, currentFrame: 0);
            _muscleEntityMap.Unregister(netId, currentFrame: 0);
        }

        // Drain both event buses
        BrainWorld.Bus.SwapBuffers();
        BrainWorld.Bus.SwapBuffers();
        MuscleWorld.Bus.SwapBuffers();
        MuscleWorld.Bus.SwapBuffers();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        BrainWorld.Dispose();
        MuscleWorld.Dispose();
    }

    // ── CapturingWriter (same pattern as Hrot.Animation.Replication.Tests) ───

    /// <summary>
    /// Test double that captures DDS write calls for in-process loopback routing.
    /// </summary>
    internal sealed class CapturingWriter<T> : IAnimDdsWriter<T>
    {
        public List<T> Written { get; } = new();
        public void Write(T sample) => Written.Add(sample);
    }
}
