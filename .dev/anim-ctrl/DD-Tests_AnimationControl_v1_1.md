# DD-Tests — Animation Control Test Strategy — Detailed Design (v1.1)

> **Status:** Architect-approved detailed design for the test pyramid
> validating the animation control mechanism end to end. Companion to
> the approved animation control design documents (mini design v0.3,
> DD-1 through DD-5, DD-Fake). Designed for **autonomous AI-agent-
> driven implementation**.
> **Changes from v1.0:** All four §11 open questions resolved per
> architect review. §11 converted to Resolutions Summary for
> traceability. §7 updated to reflect the `PumpUntil` / diagnostics
> split: `PumpUntil` promoted to shared integration-test
> infrastructure; `DumpAnimationDiagnostics` stays in the animation
> tests project. All design decisions locked in.
> **Audience:** AI implementation agent (primary), Muscle Character
> implementation team, AI editor team, engine architect (sign-off on
> test layering strategy).
> **Scope:** Three test layers — unit tests for `FakeAnimationBackend`,
> system tests for individual Muscle ECS systems, and integration
> tests for the full networkless pipeline. Eight integration test
> scenarios exercising every code path through the pipeline. Explicit
> `PumpUntil` helper with mandatory frame timeout. Future stage-2
> networked variant briefly described but explicitly deferred.
> **Out of scope:** Stride backend tests (the fake is the test
> oracle). UI integration tests for the diagnostic window
> (manual/screenshot tests, not automated). Performance / load tests.
> Real montage asset import (tests use inline hand-rolled TKB data).
> **Reads alongside:** DD-1 §§3, 5-11, 18 (the systems being
> tested), DD-Fake (the backend under test), DD-3 §3-5 (the events
> verified to fire), DD-4 §2 (the schema for inline test TKB
> construction), `Hrot.SimHost.Integration.Tests` existing patterns
> (testing convention reference).

---

## Table of contents

1. Why three test layers
2. Test infrastructure conventions
3. Layer 1 — Unit tests for `FakeAnimationBackend`
4. Layer 2 — System tests for individual Muscle systems
5. Layer 3 — Integration tests (networkless)
6. The eight integration scenarios
7. Helper utilities — `PumpUntil` and friends
8. Inline TKB test data
9. Failure-mode policy for AI agents
10. Stage-2 networked variant (deferred)
11. Resolutions summary (from v1.0 review)

---

## 1. Why three test layers

A single end-to-end integration test would be insufficient for
agent-driven development. When the integration test fails, the agent
needs to know whether the bug is in the fake backend, in a single
Muscle system, or in the wiring between them. A layered test pyramid
answers that question mechanically:

- **Layer 1** (unit, fast) — fails first when the bug is in
  `FakeAnimationBackend` itself.
- **Layer 2** (system, medium) — fails when an individual Muscle
  system (`AnimationDispatcherSystem`, `MontageQueueAdvanceSystem`,
  etc.) has a bug given a correctly-implemented backend.
- **Layer 3** (integration, slow) — fails when the wiring between
  systems is wrong, even though every system in isolation behaves
  correctly.

If only layer 3 exists, an agent staring at a failing test has no
information about whether to inspect the backend, a system, or the
wiring. With all three layers, the lowest-layer failure points
directly at the source.

Expected test counts per layer:

| Layer | Test count | Per-test runtime | Total runtime |
|---|---|---|---|
| 1 — Unit | ~15-20 | <10 ms | <0.5 s |
| 2 — System | ~10-12 | ~20-50 ms | <1 s |
| 3 — Integration | 8 scenarios | ~200-500 ms | <5 s |

Whole suite under 10 seconds; suitable for `dotnet test` in the
agent's edit loop.

## 2. Test infrastructure conventions

### 2.1 Test projects

Following the existing engine convention:

- **`Hrot.MuscleCharacter.Animation.Fake.Tests`** — layer 1 unit
  tests for `FakeAnimationBackend`. Shares assembly with
  `Hrot.MuscleCharacter.Animation` via `[InternalsVisibleTo]` for
  access to internal helpers if needed.

- **`Hrot.MuscleCharacter.Animation.Tests`** — layer 2 system tests
  for individual Muscle systems (`AnimationDispatcherSystem`,
  `MontageQueueAdvanceSystem`, `AnimationStateReporterSystem`,
  etc.). Tests one system per test fixture.

- **`Hrot.Animation.Integration.Tests`** — layer 3 integration
  tests, the eight scenarios from §6. New project. Networkless
  via `null` `INetworkFactory` (the `Hrot.SimHost.Integration.Tests`
  pattern).

### 2.2 Testing framework

xUnit, matching the engine convention. Per project docs,
`Hrot.SimHost.Integration.Tests` uses xUnit with `[Fact]` and
`[Theory]`. Same conventions throughout.

### 2.3 Bootstrap pattern for layer 3

The layer-3 integration tests use the existing
`SimHostNodeBootstrapper` with `networkFactory: null` pattern (per
project docs Example 2). This gives a real ECS world with all real
systems registered, but no DDS allocation.

Bootstrap is expensive (hundreds of ms). All integration scenarios
share one bootstrap via an xUnit `IClassFixture<AnimationTestFixture>`.
Each test resets world state by destroying test entities and resetting
the time controller, but keeps the systems and component registrations.

### 2.4 Time control

`SteppingTimeController` is used everywhere — every test advances time
deterministically via explicit `Step(dt)` calls, never via wall-clock.
This is essential for the fake backend's determinism guarantees to
matter at the test level.

Standard `dt` for tests: `1f/60f` (matches the engine's 60 Hz
simulation rate). Tests advancing larger time-steps for speed (e.g.,
to fast-forward through a long montage) use `dt = 0.1f` or similar,
documented inline in the test.

## 3. Layer 1 — Unit tests for `FakeAnimationBackend`

Pure unit tests against `FakeAnimationBackend` with a hand-constructed
`EntityRepository` containing only the components the backend touches.
No Muscle systems registered.

### 3.1 Test fixture setup

```csharp
public class FakeAnimationBackendUnitTests
{
    private readonly EntityRepository _repo;
    private readonly FakeAnimationBackend _backend;
    private readonly Entity _testEntity;
    private readonly AnimationBackendHandle _handle;

    public FakeAnimationBackendUnitTests()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponentType<FakeAnimBackendState>();
        _repo.RegisterComponentType<CharacterAnimationDefRuntime>();

        _backend = new FakeAnimationBackend();
        _backend.Initialize(new AnimationBackendConfig { EntityRepository = _repo });

        _testEntity = _repo.CreateEntity();
        var def = TestData.MinimalCharacterDef();   // see §8
        _repo.AddComponent(_testEntity, def);

        _handle = _backend.RegisterEntity(_testEntity, in def);
    }
}
```

### 3.2 Test cases — required for layer 1

Each test follows the arrange-act-assert shape and is independently
runnable. All tests prefixed with the area under test for grouping in
the test runner.

**PlayMontage tests:**

```
[Fact] PlayMontage_SetsSlotActive
    Act: backend.PlayMontageOnSlot(handle, slot=1, montage=Reload, blendIn=0.1, rate=1.0, section=0)
    Assert: state.Slots[1].IsActive == 1
    Assert: state.Slots[1].ActiveMontage == Reload
    Assert: state.Slots[1].ElapsedSeconds == 0
    Assert: state.Slots[1].TotalDurationSeconds == TestData.ReloadDuration
    Assert: state.Slots[1].FiredNotifyMask == 0
    On failure: dump full FakeAnimBackendState as JSON

[Fact] PlayMontage_OverwritesPreviousMontageInSameSlot
    Arrange: PlayMontage(slot=1, Reload); Tick(0.5)
    Act: PlayMontage(slot=1, Vault, blendIn=0.05)
    Assert: state.Slots[1].ActiveMontage == Vault  (not Reload)
    Assert: state.Slots[1].ElapsedSeconds == 0
    Assert: state.Slots[1].FiredNotifyMask == 0

[Fact] PlayMontage_UnknownMontage_NoOps
    Act: PlayMontageOnSlot(handle, slot=1, montage=99999, ...)
    Assert: state.Slots[1].IsActive == 0
```

**Tick advancement tests:**

```
[Fact] Tick_AdvancesElapsedTimeByDeltaTimesPlayRate
    Arrange: PlayMontage(slot=1, Reload, playRate=2.0)
    Act: Tick(0.5)
    Assert: state.Slots[1].ElapsedSeconds == 1.0  (0.5 * 2.0)

[Fact] Tick_DoesNotAdvanceInactiveSlots
    Arrange: PlayMontage(slot=1, Reload); state.Slots[1].IsActive = 0  (force)
    Act: Tick(0.5)
    Assert: state.Slots[1].ElapsedSeconds == 0  (unchanged because IsActive==0)

[Fact] Tick_DeactivatesSlotOnNaturalCompletion
    Arrange: PlayMontage(slot=1, Reload, duration=1.0)
    Act: Tick(1.5)  (advance past duration)
    Assert: state.Slots[1].IsActive == 0
    Assert: state.Slots[1].ActiveMontage == default
```

**Notify firing tests:**

```
[Fact] Tick_FiresNotifyWhenElapsedCrossesTimeSeconds
    Arrange: PlayMontage with notify at TimeSeconds=0.5
    Act: Tick(0.6)  (crosses 0.5)
    Assert: state.PendingNotifyCount == 1
    Assert: state.PendingNotifies[0].MarkerHash == notifyHash
    Assert: state.Slots[1].FiredNotifyMask == 0b1  (bit 0 set)

[Fact] Notify_FiresExactlyOncePerPlay
    Arrange: PlayMontage with notify at 0.5
    Act: Tick(1.0); state.PendingNotifyCount = 0  (manual drain); Tick(0.1)
    Assert: state.PendingNotifyCount == 0  (already fired, bit set)

[Fact] PlayMontage_ResetsFiredNotifyMask
    Arrange: PlayMontage; Tick to fire notify 0; ManualDrain
    Act: PlayMontage same slot, same montage
    Assert: state.Slots[1].FiredNotifyMask == 0
    Tick(0.6) again
    Assert: notify fires again
```

**Footstep cadence tests:**

```
[Fact] Footstep_EmitsAtStrideDistance
    Arrange: UpdateLocomotionInputs(handle, hv=(2,0), vv=0, grounded=true)
    Act: Tick(0.9 / 2.0)  (0.9m / 2 m/s = 0.45s = exactly one stride)
    Assert: state.PendingNotifyCount == 1
    Assert: state.PendingNotifies[0].Kind == AnimNotifyCategory.Footstep
    Assert: state.PendingNotifies[0].PayloadByte == 0  (left foot)

[Fact] Footstep_AlternatesFeet
    Arrange: hv=(2,0), grounded=true
    Act: Tick(0.45); Drain(); Tick(0.45)
    Assert: PendingNotifies[0].PayloadByte == 1  (right foot)

[Fact] Footstep_NoEmissionWhenStill
    Arrange: hv=(0.1, 0), grounded=true  (below MinFootstepSpeed=0.3)
    Act: Tick(5.0)
    Assert: state.PendingNotifyCount == 0

[Fact] Footstep_NoEmissionWhenAirborne
    Arrange: hv=(5, 0), grounded=false
    Act: Tick(5.0)
    Assert: state.PendingNotifyCount == 0
```

**Aim tests:**

```
[Fact] SetAimTarget_ActivatesAimWithBlendInWeight
    Arrange: aim inactive
    Act: SetAimTarget(handle, worldAim=(10,0,0), blendIn=1.0, priority=0)
    Assert: state.Aim.IsActive == 1
    Assert: state.Aim.TargetWorldAimPoint == (10,0,0)
    Assert: state.Aim.WorldAimPoint == (10,0,0)  (first acquire snaps)
    Assert: state.Aim.BlendWeight == 0  (will ramp on Tick)

[Fact] Tick_RampsAimBlendWeight
    Arrange: SetAimTarget(..., blendIn=0.5)
    Act: Tick(0.25)
    Assert: state.Aim.BlendWeight ≈ 0.5

[Fact] ReleaseAim_RampsDownAndDeactivates
    Arrange: SetAimTarget(..., blendIn=0.1); Tick(0.2)  (fully acquired)
    Act: ReleaseAim(handle, blendOut=0.3); Tick(0.4)
    Assert: state.Aim.IsActive == 0
    Assert: state.Aim.BlendWeight == 0
```

**Stance tests:**

```
[Fact] RequestStanceChange_StartsTransition
    Act: RequestStanceChange(handle, from=Standing, to=Crouched, blendTime=0.5)
    Assert: state.Stance.TargetStance == Crouched
    Assert: state.Stance.IsTransitioning == 1
    Assert: state.Stance.TransitionProgress == 0

[Fact] Tick_AdvancesTransitionProgress
    Arrange: RequestStanceChange(..., blendTime=1.0)
    Act: Tick(0.5)
    Assert: state.Stance.TransitionProgress ≈ 0.5
    Assert: state.Stance.IsTransitioning == 1  (still transitioning)

[Fact] Tick_CompletesStanceTransition
    Arrange: RequestStanceChange(..., blendTime=0.5)
    Act: Tick(0.6)  (overshoot)
    Assert: state.Stance.IsTransitioning == 0
    Assert: state.Stance.CurrentStance == Crouched
    Assert: state.Stance.TransitionProgress == 1.0
```

**Hard-assert tests:**

```
[Fact] EmitNotify_OverflowThrowsInvalidOperationException
    Arrange: PlayMontage; fill PendingNotifies to capacity 16 manually
    Act: backend triggers one more notify emission (force via crafted state)
    Assert: throws InvalidOperationException with message containing
            "FakeAnimationBackend notify buffer overflow"
```

**Drain tests:**

```
[Fact] DrainNotifies_TransfersAllPendingToDest
    Arrange: 3 notifies in PendingNotifies
    Act: var span = stackalloc RawNotifyEvent[16]; var n = DrainNotifies(handle, span)
    Assert: n == 3
    Assert: state.PendingNotifyCount == 0

[Fact] DrainNotifies_HandlesSmallerDestBuffer
    Arrange: 5 notifies pending
    Act: var span = stackalloc RawNotifyEvent[3]; var n = DrainNotifies(handle, span)
    Assert: n == 3
    Assert: state.PendingNotifyCount == 2  (remaining shifted to front)
```

### 3.3 What layer 1 does NOT test

- The dispatcher → backend wiring (that's layer 2).
- Event publication to `FdpEventBus` (that's layer 2).
- Status replication or `MontageEndedEvent` synthesis (those are layer 3).
- The diagnostic UI window (manual testing).
- Performance (out of scope).

## 4. Layer 2 — System tests for individual Muscle systems

Each test instantiates the specific system under test against a
minimal world and verifies the system's input-to-output behavior.

The systems to test:

| System | Test class | Inputs (read) | Outputs (write) |
|---|---|---|---|
| `AnimationDispatcherSystem` | `AnimationDispatcherSystemTests` | `AnimationChannel`, `ActorCapabilityState`, `CharacterAnimationDefRuntime` | `AnimationExecutorState`, `AnimationChannel.Status`, `DispatchedInstanceId` |
| `MontageQueueAdvanceSystem` | `MontageQueueAdvanceSystemTests` | `AnimationMontageQueue`, `AnimationExecutorState`, fake backend state | `AnimationMontageQueueState`, fake backend (via bridge ref) |
| `LookAtDispatcherSystem` | `LookAtDispatcherSystemTests` | `LookAtChannel`, `ActorCapabilityState` | `LookAtExecutorState`, `LookAtChannel.Status` |
| `StanceTransitionSystem` | `StanceTransitionSystemTests` | `StanceIntent`, `StanceStatus`, `ActorCapabilityState` | `StanceStatus` |
| `AnimationRuntimeBridgeSystem` | `AnimationRuntimeBridgeSystemTests` | `AnimationExecutorState`, `LookAtExecutorState`, `SimTransform`, `SimVelocity` | fake backend (via method calls) |
| `NotifyEventEmitterSystem` | `NotifyEventEmitterSystemTests` | fake backend pending notifies, `SimTransform` for enrichment | `FdpEventBus` typed events |
| `AnimationStateReporterSystem` | `AnimationStateReporterSystemTests` | fake backend state, `AnimationExecutorState` | `AnimationChannel.Status`, synthesized `MontageStartedEvent` / `MontageEndedEvent` |

### 4.1 Test fixture pattern

Each system test class instantiates a real `EntityRepository`,
registers the components the system reads/writes, instantiates the
system, and ticks it directly without going through the full
simulation kernel:

```csharp
public class AnimationDispatcherSystemTests
{
    private readonly EntityRepository _repo;
    private readonly FakeAnimationBackend _backend;
    private readonly AnimationDispatcherSystem _system;
    private readonly Entity _entity;

    public AnimationDispatcherSystemTests()
    {
        _repo = new EntityRepository();
        // Register the contractual components from DD-1 §5.
        _repo.RegisterComponentType<AnimationChannel>();
        _repo.RegisterComponentType<AnimationExecutorState>();
        _repo.RegisterComponentType<ActorCapabilityState>();
        _repo.RegisterComponentType<CharacterAnimationDefRuntime>();
        _repo.RegisterComponentType<FakeAnimBackendState>();

        _backend = new FakeAnimationBackend();
        _backend.Initialize(new AnimationBackendConfig { EntityRepository = _repo });

        _system = new AnimationDispatcherSystem(_backend);

        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, default(AnimationChannel));
        _repo.AddComponent(_entity, default(AnimationExecutorState));
        _repo.AddComponent(_entity, new ActorCapabilityState { Capabilities = ActorCapabilities.CanPlayAnimations });
        _repo.AddComponent(_entity, TestData.MinimalCharacterDef());
    }
}
```

### 4.2 Test cases — required for layer 2

Per system, ~2-4 tests covering the system's main responsibilities.

**`AnimationDispatcherSystemTests`:**

```
[Fact] PlayMontageCommand_TriggersBackendPlay
    Arrange: write ActionIdPlayMontage to AnimationChannel, bump ActionInstanceId
    Act: _system.Tick()
    Assert: AnimationChannel.DispatchedInstanceId == ActionInstanceId
    Assert: AnimationChannel.Status == Running
    Assert: backend state shows slot active with the requested montage

[Fact] PlayMontageCommand_NoCapability_FailsImmediately
    Arrange: ActorCapabilityState.Capabilities = 0  (no CanPlayAnimations)
    Write ActionIdPlayMontage, bump InstanceId
    Act: _system.Tick()
    Assert: AnimationChannel.Status == Failure
    Assert: backend state shows no active slot

[Fact] PlayMontageCommand_UnknownMontage_FailsImmediately
    Arrange: PlayMontageParams with MontageId = unknownHash
    Act: _system.Tick()
    Assert: AnimationChannel.Status == Failure

[Fact] SameInstanceId_NoActionTaken
    Arrange: ActiveAction set, ActionInstanceId == DispatchedInstanceId
    Act: _system.Tick()
    Assert: no change to executor state or backend state
```

**`AnimationStateReporterSystemTests`:**

```
[Fact] OnNaturalCompletion_WritesStatusSuccess
    Arrange: AnimationExecutorState shows slot was active; backend reports slot now inactive
    Act: _system.Tick()
    Assert: AnimationChannel.Status == Success

[Fact] OnNaturalCompletion_PublishesMontageEndedEvent
    Arrange: as above
    Act: _system.Tick()
    Assert: FdpEventBus.ReadEvents<MontageEndedEvent>() contains entry with
            Target == entity, EndReason == NaturalEnd

[Fact] OnInterruption_PublishesEventWithReasonInterrupted
    Arrange: AnimationExecutorState shows pending-stop flag was set;
            backend shows slot inactive
    Act: _system.Tick()
    Assert: MontageEndedEvent.EndReason == Interrupted
```

(Similar patterns for each remaining system. Estimated ~10-12 tests
total across layer 2.)

### 4.3 What layer 2 does NOT test

- Full pipeline integration (that's layer 3).
- Cross-system interactions through full ECS tick ordering (layer 3).
- The Brain side (intent authoring, status observation) — these are
  ECS writes/reads independent of any system; not worth dedicated
  layer-2 tests.

## 5. Layer 3 — Integration tests (networkless)

The architect's outline maps directly to layer 3. Each test scenario
runs a full Muscle pipeline against the fake backend in a networkless
configuration, and verifies end-to-end behavior.

### 5.1 Test fixture

```csharp
public sealed class AnimationIntegrationFixture : IPumpableHarness, IDisposable
{
    public EntityRepository World { get; }
    public FdpEventBus EventBus { get; }
    public FakeAnimationBackend Backend { get; }
    public SteppingTimeController Time { get; }
    public SimHostNodeBootstrapper Bootstrapper { get; }
    public ISimulationKernel Kernel { get; }

    public AnimationIntegrationFixture()
    {
        var hrotConfig = new HrotNodeConfig
        {
            SubsystemName = "AnimationIntegrationTest",
            NodeId = 1,
        };

        Bootstrapper = new SimHostNodeBootstrapper(
            networkFactory: null,              // networkless
            role: NodeRole.MuscleGround | NodeRole.Perception,
            localTempRoot: Path.GetTempPath(),
            eventHistoryService: null,
            hrotConfig: hrotConfig,
            simulationRateHz: 60.0f);

        // Register the animation systems we're testing.
        Bootstrapper.ApplicationSystemsRegistrar = ctx =>
        {
            Backend = new FakeAnimationBackend();
            ctx.RegisterSystem(new AnimationDispatcherSystem(Backend));
            ctx.RegisterSystem(new MontageQueueAdvanceSystem(Backend));
            ctx.RegisterSystem(new LookAtDispatcherSystem(Backend));
            ctx.RegisterSystem(new StanceTransitionSystem(Backend));
            ctx.RegisterSystem(new AnimationRuntimeBridgeSystem(Backend));
            ctx.RegisterSystem(new NotifyEventEmitterSystem(Backend));
            ctx.RegisterSystem(new AnimationStateReporterSystem(Backend));
            // Also: the engine's existing capability-change reactor,
            // assuming it's already registered by the bootstrapper.
        };

        Bootstrapper.BootstrapNode(hrotConfig, Bootstrapper.GetBehaviorRegistry());

        World = Bootstrapper.CoreLogicPack.World;
        EventBus = Bootstrapper.CoreLogicPack.EventBus;
        Kernel = Bootstrapper.CoreLogicPack.Kernel;
        Time = (SteppingTimeController)Bootstrapper.CoreLogicPack.TimeController;
    }

    public Entity SpawnHumanoid(CharacterAnimationDefRuntime def)
    {
        var entity = World.CreateEntity();
        // Add all the contractual components per DD-1 §5.1 + DD-Fake §3.2.
        World.AddComponent(entity, default(AnimationChannel));
        World.AddComponent(entity, default(LookAtChannel));
        World.AddComponent(entity, new StanceIntent
        {
            TargetStance = def.SupportedStances[0],
            Version = 0,
        });
        World.AddComponent(entity, new StanceStatus
        {
            CurrentStance = def.SupportedStances[0],
            Phase = StanceTransitionPhase.Completed,
        });
        World.AddComponent(entity, default(AnimationMontageQueue));
        World.AddComponent(entity, new AnimationMontageQueueState
        {
            CurrentEntryIndex = 0xFF,
        });
        World.AddComponent(entity, def);
        World.AddComponent(entity, default(AnimationExecutorState));
        World.AddComponent(entity, default(LookAtExecutorState));
        World.AddComponent(entity, new ActorCapabilityState
        {
            Capabilities = ActorCapabilities.CanPlayAnimations |
                           ActorCapabilities.CanAim |
                           ActorCapabilities.CanChangeStance,
        });
        World.AddComponent(entity, default(SimTransform));
        World.AddComponent(entity, default(SimVelocity));

        // Backend handle initialization happens on first AnimationRuntimeBridgeSystem tick
        // when it observes BackendHandle.Generation == 0 (per DD-1 §10 / §14).
        return entity;
    }

    public void ResetWorld()
    {
        // Destroy all test entities.
        var query = World.Query().With<AnimationChannel>().Build();
        foreach (var e in query.ToList()) World.DestroyEntity(e);
        // Drain event bus.
        EventBus.SwapBuffers();
        EventBus.SwapBuffers();
    }

    public void Dispose()
    {
        Bootstrapper?.Dispose();
    }
}
```

All eight scenarios share this fixture via xUnit's `IClassFixture`:

```csharp
public class AnimationIntegrationTests : IClassFixture<AnimationIntegrationFixture>
{
    private readonly AnimationIntegrationFixture _fix;

    public AnimationIntegrationTests(AnimationIntegrationFixture fix)
    {
        _fix = fix;
        _fix.ResetWorld();   // each test starts clean
    }

    // ... [Fact]s for the 8 scenarios
}
```

### 5.2 The `PumpUntil` helper

Every layer-3 test needs to advance simulation until some condition
becomes true. A loose `while (!condition) Tick()` would hang the test
if the condition never becomes true. The `PumpUntil` helper enforces
a frame budget and fails the test explicitly on timeout.

Per architect ruling on §11.3, `PumpUntil` is promoted to shared
integration-test infrastructure; the animation-specific diagnostic
dump is animation-test-local. Full details and code in §7.1 / §7.2.

Usage from animation integration tests:

```csharp
_fix.PumpUntil(
    condition: () => _fix.World.GetComponentRO<AnimationChannel>(entity).Status == NodeStatus.Success,
    maxFrames: 250,
    conditionName: "Montage reports Success",
    diagnosticDump: _fix.DumpAnimationDiagnostics);
```

On timeout, the diagnostic dump is invoked and its output appears in
the `TimeoutException` message — giving the AI agent the full
animation state at the moment the test gave up.

## 6. The eight integration scenarios

Each scenario is one `[Fact]`. Scenario names follow the pattern
`<Action>_<ExpectedOutcome>` for searchability.

### Scenario 1: Happy path single montage

Validates the canonical Brain-commands-Muscle-executes-Brain-sees-Success
path.

```csharp
[Fact]
public void PlayMontage_RunsToCompletionAndReportsSuccess()
{
    // Arrange
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);   // let the bridge register the entity with backend

    // Act — write the PlayMontage intent directly (simulating the AI primitive)
    ref var channel = ref _fix.World.GetComponentRW<AnimationChannel>(entity);
    channel.ActiveAction = AnimationActionIds.PlayMontage;
    WriteParams(channel.Params, new PlayMontageParams
    {
        MontageId = TestData.ReloadMontageId,
        BlendInTime = -1f,    // use TKB default
        BlendOutTime = -1f,
        PlayRate = 1.0f,
        StartSectionIndex = 0,
        LoopCount = 1,
        Priority = 0,
        Flags = 0,
    });
    channel.ActionInstanceId++;

    // Pump until the dispatcher acks the command (first tick).
    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<AnimationChannel>(entity).DispatchedInstanceId
              == channel.ActionInstanceId,
        maxFrames: 5,
        conditionName: "Dispatcher acks command");

    // Status should now be Running.
    Assert.Equal(NodeStatus.Running,
        _fix.World.GetComponentRO<AnimationChannel>(entity).Status);

    // Pump until montage natural completion (TestData.ReloadDuration = 3.4s, plus blend-out).
    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<AnimationChannel>(entity).Status == NodeStatus.Success,
        maxFrames: 250,
        conditionName: "Montage reports Success");

    // Verify MontageEndedEvent was published.
    var endedEvents = _fix.EventBus.ReadEvents<MontageEndedEvent>().ToList();
    var matchingEnd = endedEvents.FirstOrDefault(e => e.Target == entity);
    Assert.NotEqual(default, matchingEnd);
    Assert.Equal(MontageEndReason.NaturalEnd, matchingEnd.EndReason);
    Assert.Equal(TestData.ReloadMontageId, matchingEnd.MontageId);
}
```

### Scenario 2: Notify firing at keyframe

```csharp
[Fact]
public void PlayMontage_NotifyFiresAtAuthoredKeyframe()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);

    // Reload montage has authored notify "MagOut" at TimeSeconds=0.8.
    IssuePlayMontage(entity, TestData.ReloadMontageId);

    // Pump to just past the notify time.
    _fix.PumpUntil(
        () => _fix.EventBus.ReadEvents<AnimNotifyEvent>()
            .Any(e => e.Target == entity && e.MarkerHash == TestData.MagOutMarkerHash),
        maxFrames: 60,
        conditionName: "AnimNotifyEvent for MagOut received");
    // (Implicit assertion: PumpUntil throws TimeoutException if not received.)
}
```

### Scenario 3: Stop mid-play produces Interrupted

```csharp
[Fact]
public void StopMontage_MidPlayInterruptsAndPublishesInterruptedEvent()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);
    IssuePlayMontage(entity, TestData.ReloadMontageId);
    _fix.PumpFrames(30);   // ~0.5s in

    // Issue Stop.
    ref var channel = ref _fix.World.GetComponentRW<AnimationChannel>(entity);
    channel.ActiveAction = AnimationActionIds.StopMontage;
    WriteParams(channel.Params, new StopMontageParams { BlendOutTime = 0.2f, Reason = 0 });
    channel.ActionInstanceId++;

    // Wait for completion via blend-out.
    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<AnimationChannel>(entity).Status != NodeStatus.Running,
        maxFrames: 30,
        conditionName: "Status leaves Running");

    var ended = _fix.EventBus.ReadEvents<MontageEndedEvent>()
        .Single(e => e.Target == entity);
    Assert.Equal(MontageEndReason.Interrupted, ended.EndReason);
}
```

### Scenario 4: Stance transition

```csharp
[Fact]
public void StanceIntent_DrivesTransitionAndPublishesStanceChangedEvent()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);

    ref var intent = ref _fix.World.GetComponentRW<StanceIntent>(entity);
    intent.TargetStance = TestData.CrouchedStance;
    intent.BlendTime = 0.3f;
    intent.Version++;

    // Transition should complete in ~0.3s.
    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<StanceStatus>(entity).CurrentStance
              == TestData.CrouchedStance,
        maxFrames: 30,
        conditionName: "Stance transitioned to Crouched");

    var stanceChanges = _fix.EventBus.ReadEvents<StanceChangedEvent>()
        .Where(e => e.Target == entity).ToList();
    Assert.Single(stanceChanges);
    Assert.Equal(TestData.StandingStance, stanceChanges[0].PreviousStance);
    Assert.Equal(TestData.CrouchedStance, stanceChanges[0].NewStance);
}
```

### Scenario 5: Montage chain via queue

```csharp
[Fact]
public void PlayMontageQueue_ThreeEntriesPlaysInOrderAndReportsOneSuccess()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);

    // Write the queue side-buffer.
    ref var queue = ref _fix.World.GetComponentRW<AnimationMontageQueue>(entity);
    Span<MontageQueueEntry> entries = queue.Entries;
    entries[0] = new MontageQueueEntry { MontageId = TestData.MontageA_Id, BlendIntoTime = 0.1f, PlayRate = 1f };
    entries[1] = new MontageQueueEntry { MontageId = TestData.MontageB_Id, BlendIntoTime = 0.1f, PlayRate = 1f };
    entries[2] = new MontageQueueEntry { MontageId = TestData.MontageC_Id, BlendIntoTime = 0.1f, PlayRate = 1f };
    queue.Count = 3;
    queue.QueueVersion++;

    // Issue the PlayMontageQueue command.
    ref var channel = ref _fix.World.GetComponentRW<AnimationChannel>(entity);
    channel.ActiveAction = AnimationActionIds.PlayMontageQueue;
    WriteParams(channel.Params, new PlayMontageQueueParams
    {
        InitialBlendInTime = 0.1f,
        Priority = 0,
    });
    channel.ActionInstanceId++;

    // Track which entries we've seen start.
    var startedEntries = new HashSet<byte>();
    _fix.PumpUntil(
        () =>
        {
            foreach (var e in _fix.EventBus.ReadEvents<MontageStartedEvent>())
                if (e.Target == entity) startedEntries.Add(e.QueueIndex);
            return startedEntries.Count == 3;
        },
        maxFrames: 600,   // ~10s — montages can be a few seconds each
        conditionName: "All 3 chain entries started");

    Assert.Contains((byte)0, startedEntries);
    Assert.Contains((byte)1, startedEntries);
    Assert.Contains((byte)2, startedEntries);

    // Status flips to Success only after the last entry blends out.
    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<AnimationChannel>(entity).Status == NodeStatus.Success,
        maxFrames: 60,
        conditionName: "Chain reports Success");
}
```

### Scenario 6: Enqueue mid-play extends chain

```csharp
[Fact]
public void EnqueueMontage_DuringActiveQueueAppendsAndPlays()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);

    // Start a queue with 1 entry.
    InitializeQueue(entity, TestData.MontageA_Id);
    IssuePlayMontageQueue(entity);

    // Wait for first montage to start.
    _fix.PumpUntil(
        () => _fix.EventBus.ReadEvents<MontageStartedEvent>().Any(e => e.Target == entity),
        maxFrames: 30,
        conditionName: "First chain entry started");

    // Mid-play: append a second entry.
    ref var queue = ref _fix.World.GetComponentRW<AnimationMontageQueue>(entity);
    Span<MontageQueueEntry> entries = queue.Entries;
    entries[1] = new MontageQueueEntry { MontageId = TestData.MontageB_Id, BlendIntoTime = 0.1f, PlayRate = 1f };
    queue.Count = 2;
    queue.QueueVersion++;
    // NO ActionInstanceId bump (per DD-1 §6.4 — side-buffer mutation only).

    // Verify second entry plays.
    _fix.PumpUntil(
        () => _fix.EventBus.ReadEvents<MontageStartedEvent>()
            .Any(e => e.Target == entity && e.QueueIndex == 1),
        maxFrames: 300,
        conditionName: "Appended entry started");
}
```

### Scenario 7: Footstep cadence

```csharp
[Fact]
public void Locomotion_DrivesFootstepEventsAtCorrectCadence()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);

    // Pretend the entity is moving at 2 m/s.
    ref var velocity = ref _fix.World.GetComponentRW<SimVelocity>(entity);
    velocity.Linear = new Vector3(2, 0, 0);

    // Bridge converts this to UpdateLocomotionInputs each tick.
    // FakeBackendConstants.FootstepStrideMeters = 0.9, MinFootstepSpeed = 0.3.
    // Expected: ~2.22 footsteps per second.
    _fix.PumpFrames(180);   // 3 seconds

    var footsteps = _fix.EventBus.ReadEvents<FootstepEvent>()
        .Where(e => e.Target == entity).ToList();
    Assert.InRange(footsteps.Count, 5, 8);   // 3s × 2.22 ≈ 6.6, allow ±2 for blend timing
    Assert.True(footsteps.All(e => e.FootIndex == 0 || e.FootIndex == 1));
    // Verify feet alternate.
    for (int i = 1; i < footsteps.Count; i++)
        Assert.NotEqual(footsteps[i-1].FootIndex, footsteps[i].FootIndex);
}
```

### Scenario 8: LookAt acquire and release

```csharp
[Fact]
public void LookAtPoint_AcquiresAndReleasesAimWithStatusTransitions()
{
    var entity = _fix.SpawnHumanoid(TestData.MinimalCharacterDef());
    _fix.PumpFrames(1);

    // Acquire.
    ref var look = ref _fix.World.GetComponentRW<LookAtChannel>(entity);
    look.ActiveAction = LookAtActionIds.LookAtPoint;
    WriteParams(look.Params, new LookAtPointParams
    {
        WorldPoint = new Vector3(10, 0, 0),
        BlendInTime = 0.1f,
        Priority = 0,
    });
    look.ActionInstanceId++;

    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<LookAtChannel>(entity).Status == NodeStatus.Running,
        maxFrames: 5,
        conditionName: "LookAt enters Running");

    // Release.
    look = ref _fix.World.GetComponentRW<LookAtChannel>(entity);
    look.ActiveAction = LookAtActionIds.ReleaseLook;
    WriteParams(look.Params, new ReleaseLookParams { BlendOutTime = 0.2f });
    look.ActionInstanceId++;

    _fix.PumpUntil(
        () => _fix.World.GetComponentRO<LookAtChannel>(entity).Status == NodeStatus.Success,
        maxFrames: 30,
        conditionName: "LookAt reports Success after release");
}
```

## 7. Helper utilities — `PumpUntil` and friends

The integration tests rely on a small set of helpers. Each is small,
deterministic, and has explicit failure modes.

**Per architect ruling on §11.3, the helpers split across two
locations:**

- **`PumpUntil`** is a universal pattern used across the engine's
  headless tests (e.g., `HrotRunnerHarness`). It is promoted to the
  shared integration-test infrastructure project, alongside other
  cross-cutting test helpers.
- **`DumpAnimationDiagnostics`** is animation-specific (it knows how
  to serialize `AnimationChannel`, `AnimationMontageQueue`,
  `FakeAnimBackendState`, etc.) and lives in
  `Hrot.Animation.Integration.Tests`.

When `PumpUntil` times out, it doesn't itself know how to dump
animation state — it accepts an optional `Func<string>?
diagnosticDump` parameter that the caller provides. The animation
test fixture passes `_fix.DumpAnimationDiagnostics` as that
parameter.

### 7.1 `PumpUntil` — shared infrastructure

Lives in the engine's shared test utilities (exact project per the
shared infrastructure team's convention; likely
`Fdp.Testing.Integration` or similar):

```csharp
public static class PumpExtensions
{
    public const int DefaultMaxFrames = 300;   // 5 seconds at 60 Hz
    public const float DefaultDt = 1f / 60f;

    public static void PumpUntil(
        this IPumpableHarness harness,
        Func<bool> condition,
        int maxFrames = DefaultMaxFrames,
        float dt = DefaultDt,
        string conditionName = "(unnamed condition)",
        Func<string>? diagnosticDump = null)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) return;
            harness.Time.Step(dt);
            harness.Kernel.Tick(dt);
        }

        var diagnostic = diagnosticDump?.Invoke()
            ?? "(no diagnostic dump provided)";
        throw new TimeoutException(
            $"PumpUntil({conditionName}) did not become true within " +
            $"{maxFrames} frames ({maxFrames * dt:F2}s). " +
            $"Diagnostic snapshot:\n{diagnostic}");
    }

    public static void PumpFrames(
        this IPumpableHarness harness,
        int frames,
        float dt = DefaultDt)
    {
        for (int i = 0; i < frames; i++)
        {
            harness.Time.Step(dt);
            harness.Kernel.Tick(dt);
        }
    }
}

/// <summary>
/// Minimal interface a test harness must implement to be usable with
/// the shared PumpExtensions helpers. AnimationIntegrationFixture
/// (and any future integration fixtures) implement this.
/// </summary>
public interface IPumpableHarness
{
    SteppingTimeController Time { get; }
    ISimulationKernel Kernel { get; }
}
```

The shared location lets the replication integration tests (stage 2,
§10), TKB descriptor tests, Blueprint primitive tests, and any
future integration suite reuse the same pump primitive without
duplication.

### 7.2 `DumpAnimationDiagnostics` — animation-specific

Lives in `Hrot.Animation.Integration.Tests`. Reuses the
`FakeAnimBackendState` JSON snapshot serializer from DD-Fake §8:

```csharp
public static class AnimationDiagnostics
{
    public static string DumpAnimationDiagnostics(this AnimationIntegrationFixture fix)
    {
        var sb = new StringBuilder();
        var humanoidQuery = fix.World.Query()
            .With<FakeAnimBackendState>()
            .Build();
        foreach (var entity in humanoidQuery)
        {
            sb.AppendLine($"== Entity {entity.Index} ==");
            sb.AppendLine(FakeAnimBackendSnapshotJson.Serialize(fix.World, entity));
        }
        return sb.ToString();
    }
}
```

The animation test fixture passes this as the `diagnosticDump`
argument to `PumpUntil`:

```csharp
_fix.PumpUntil(
    condition: () => ...,
    maxFrames: 300,
    conditionName: "Montage reports Success",
    diagnosticDump: _fix.DumpAnimationDiagnostics);
```

This gives the AI agent the full animation state on every timeout
failure while keeping `PumpUntil` itself domain-agnostic for reuse
elsewhere.

### 7.3 `WriteParams<T>` — unsafe blob write helper

```csharp
public static unsafe void WriteParams<T>(byte[] paramsBlob, T value) where T : unmanaged
{
    if (sizeof(T) > paramsBlob.Length)
        throw new ArgumentException(
            $"WriteParams<{typeof(T).Name}>: size {sizeof(T)} exceeds Params blob {paramsBlob.Length}");
    fixed (byte* dst = paramsBlob)
    {
        *(T*)dst = value;
    }
}
```

Wraps the standard unsafe-cast pattern from DD-5 §3.1 codegen. All
test-side intent authoring uses this helper. Lives alongside other
animation test helpers in `Hrot.Animation.Integration.Tests`.

### 7.4 `IssuePlayMontage` — common command helper

```csharp
private void IssuePlayMontage(Entity entity, int montageId)
{
    ref var ch = ref _fix.World.GetComponentRW<AnimationChannel>(entity);
    ch.ActiveAction = AnimationActionIds.PlayMontage;
    WriteParams(ch.Params, new PlayMontageParams
    {
        MontageId = montageId,
        BlendInTime = -1f,
        BlendOutTime = -1f,
        PlayRate = 1f,
        StartSectionIndex = 0,
        LoopCount = 1,
        Priority = 0,
        Flags = 0,
    });
    ch.ActionInstanceId++;
}
```

Repeated boilerplate across scenarios; lives in a test helper class
inside `Hrot.Animation.Integration.Tests`.

## 8. Inline TKB test data

Tests use hand-rolled `CharacterAnimationDefDto` values, not loaded
JSON. This keeps tests self-contained and explicit about what they're
testing against.

```csharp
public static class TestData
{
    // Stable IDs computed at test-class init via FNV-1a hash (matching DD-4 §3.1).
    public static readonly int ReloadMontageId = HashName("Reload_Rifle");
    public static readonly int MontageA_Id = HashName("TestMontage_A");
    public static readonly int MontageB_Id = HashName("TestMontage_B");
    public static readonly int MontageC_Id = HashName("TestMontage_C");

    public static readonly uint MagOutMarkerHash = HashName("MagOut");

    public const float ReloadDuration = 3.4f;

    public static readonly StanceId StandingStance = new(0);
    public static readonly StanceId CrouchedStance = new(1);

    /// <summary>
    /// Minimal character def covering all test scenarios. One full-body slot
    /// (slot 100), a few test montages, two stances, an aim config, and a couple
    /// of notify markers.
    /// </summary>
    public static CharacterAnimationDefRuntime MinimalCharacterDef()
    {
        var dto = new CharacterAnimationDefDto
        {
            Slots = new[]
            {
                new SlotDefDto { SlotId = 100, Name = "FullBody",
                                 BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override,
                                 Priority = 100 }
            },
            Montages = new[]
            {
                new MontageDefDto { Name = "Reload_Rifle", AssetRef = "test", Slot = 100,
                                    DefaultBlendInTime = 0.1f, DefaultBlendOutTime = 0.2f,
                                    DurationSeconds = 3.4f, Sections = new[] { "Start", "Insert", "Close" },
                                    Notifies = new[]
                                    {
                                        new MontageNotifyRefDto { MarkerName = "MagOut", TimeSeconds = 0.8f }
                                    }},
                new MontageDefDto { Name = "TestMontage_A", AssetRef = "test", Slot = 100,
                                    DefaultBlendInTime = 0.1f, DefaultBlendOutTime = 0.1f,
                                    DurationSeconds = 1.0f, Sections = Array.Empty<string>(),
                                    Notifies = Array.Empty<MontageNotifyRefDto>() },
                new MontageDefDto { Name = "TestMontage_B", AssetRef = "test", Slot = 100,
                                    DefaultBlendInTime = 0.1f, DefaultBlendOutTime = 0.1f,
                                    DurationSeconds = 1.0f, Sections = Array.Empty<string>(),
                                    Notifies = Array.Empty<MontageNotifyRefDto>() },
                new MontageDefDto { Name = "TestMontage_C", AssetRef = "test", Slot = 100,
                                    DefaultBlendInTime = 0.1f, DefaultBlendOutTime = 0.1f,
                                    DurationSeconds = 1.0f, Sections = Array.Empty<string>(),
                                    Notifies = Array.Empty<MontageNotifyRefDto>() },
                new MontageDefDto { Name = "Trans_StandToCrouch", AssetRef = "test", Slot = 100,
                                    DefaultBlendInTime = 0.05f, DefaultBlendOutTime = 0.05f,
                                    DurationSeconds = 0.3f, Sections = Array.Empty<string>(),
                                    Notifies = Array.Empty<MontageNotifyRefDto>(),
                                    IsStanceTransition = true },
                new MontageDefDto { Name = "Trans_CrouchToStand", AssetRef = "test", Slot = 100,
                                    DefaultBlendInTime = 0.05f, DefaultBlendOutTime = 0.05f,
                                    DurationSeconds = 0.3f, Sections = Array.Empty<string>(),
                                    Notifies = Array.Empty<MontageNotifyRefDto>(),
                                    IsStanceTransition = true },
            },
            SupportedStances = new[] { StandingStance, CrouchedStance },
            StanceTransitions = new[]
            {
                new StanceTransitionDto { From = StandingStance, To = CrouchedStance,
                                          TransitionMontageName = "Trans_StandToCrouch",
                                          DefaultBlendTime = 0.3f },
                new StanceTransitionDto { From = CrouchedStance, To = StandingStance,
                                          TransitionMontageName = "Trans_CrouchToStand",
                                          DefaultBlendTime = 0.3f },
            },
            AimConfig = new AimConfigDto
            {
                MaxYawDegrees = 90,
                MaxPitchDegrees = 70,
                AimSourceBone = "head",
            },
            NotifyMarkers = new[]
            {
                new NotifyMarkerDefDto { Name = "MagOut", Hash = MagOutMarkerHash,
                                         Kind = AnimNotifyCategory.Generic },
            },
        };

        // Bake DTO to runtime (same path as AnimationTkbTranslator §4.1).
        return CharacterAnimationDefRuntime.BakeForTest(dto);
    }

    private static int HashName(string name)
        => unchecked((int)(Fnv1a64(name) & 0x7FFFFFFF));
}
```

`CharacterAnimationDefRuntime.BakeForTest` is a test-only convenience
that wraps the same baking logic the production translator uses,
exposed via `[InternalsVisibleTo("Hrot.Animation.Integration.Tests")]`.

## 9. Failure-mode policy for AI agents

This section is specifically for the AI agent driving the
implementation. Read this when a test fails.

### 9.1 Layer-1 failure (`*UnitTests`)

Bug is in `FakeAnimationBackend` itself. Don't touch ECS systems
yet — fix the backend, re-run layer-1 tests until green, then
re-run higher layers.

### 9.2 Layer-2 failure (`*SystemTests`)

Bug is in the named system. Check:
1. Is the system reading the right components? Compare its inputs
   against DD-1's table for that system.
2. Is the system writing the right outputs? Same check on writes.
3. Is the system calling the backend correctly? The test fixture
   shows what backend methods should have been called.

Don't change `FakeAnimationBackend` — its layer-1 tests are green,
so it's not the issue (or layer-1 tests are insufficient — file a
new layer-1 test before fixing here).

### 9.3 Layer-3 failure (`AnimationIntegrationTests`)

If layer-1 and layer-2 are green but layer-3 fails, the bug is in
the wiring: system registration order, system tick phase
assignment, missing component registration, missing translator
call, etc.

The diagnostic JSON dump from `PumpUntil` shows the world state at
failure. Common failure patterns:

- **`AnimationChannel.Status` stuck at `Running`** — likely the
  `AnimationStateReporterSystem` isn't observing backend completion.
  Check phase ordering (DD-1 §17): reporter must run AFTER bridge.
- **No `MontageEndedEvent` despite Status flipping** — reporter
  flipped status but didn't publish event. Check the synthesis logic
  in `AnimationStateReporterSystem` §18.
- **`AnimationMontageQueueState.CurrentEntryIndex` stuck at 0** —
  `MontageQueueAdvanceSystem` isn't advancing. Check that the
  fake backend's `QuerySlotState` returns `InBlendOutWindow = true`
  at the right time.

### 9.4 Timeout in `PumpUntil`

Read the diagnostic JSON output carefully. The named condition
tells the agent what was expected. The state shows what is.

Don't increase `maxFrames` to make the test pass. The timeouts are
generously sized (5+ seconds typical); hitting them means the
pipeline is genuinely broken, not slow.

## 10. Stage-2 networked variant (deferred)

Per your iteration plan, the stage-1 networkless integration tests
in this DD validate the ECS pipeline. Stage 2 adds a parallel test
suite `Hrot.Animation.Network.Integration.Tests` that uses the
`HrotRunnerHarness` "simhost,cgf" loopback topology to verify the
DD-2 replication translators end-to-end.

The stage-2 suite reuses the eight scenarios from §6, with two
adaptations:

1. Tests run two `BootstrapNode` calls (Brain and Muscle) into one
   process over loopback DDS.
2. `PumpUntil` allows additional frames per network round-trip
   (~2 frames per direction per intent).

Implementation deferred until after stage 1 ships and the
networkless suite is stable. Brief outline only — full DD for
stage 2 will be a separate document if/when needed.

## 11. Resolutions summary (from v1.0 review)

All four open questions from DD-Tests v1.0 received architect
rulings. Where rulings confirmed v1.0 leanings, no body section
needed revision; the resolution status is recorded here for
traceability. Where they triggered material changes, the relevant
body section is updated.

### 11.1 ✅ Test fixture sharing across all integration scenarios

**Resolved.** Shared `IClassFixture` is strongly preferred and
approved. Bootstrap cost (hundreds of ms) must not be paid per
test if the agent's edit-compile-test loop is to stay under 10
seconds. `ResetWorld()` must be meticulous about destroying all
entities and resetting `SteppingTimeController` to tick 0
between tests. Reflected (already) in §5.1.

### 11.2 ✅ `CharacterAnimationDefRuntime.BakeForTest` test seam

**Resolved.** Option A approved — expose `BakeForTest` as an
internal seam via `[InternalsVisibleTo]`. Forcing every animation
integration test to spin up the full `AnimationTkbTranslator` and
hot-reload machinery violates test isolation. The DD-4 translator
gets its own dedicated tests; animation tests test the animation
systems and the ECS pipeline. Reflected (already) in §8.

### 11.3 ✅ `PumpUntil` location

**Resolved with split.** The two helpers go to different homes:

- **`PumpUntil`** itself is a universal pattern used across the
  engine's headless tests (e.g., `HrotRunnerHarness`). Promote it
  to shared integration test infrastructure (alongside other
  cross-cutting test helpers like the future replication tests
  will use).
- **`DumpAnimationDiagnostics`** is animation-specific (it knows how
  to serialize animation channel and queue components). It must
  remain inside `Hrot.Animation.Integration.Tests`.

The animation test fixture passes its `DumpAnimationDiagnostics`
method to `PumpUntil` as an optional diagnostic dump callback.
Reflected in §7.1 / §7.2 (split) and §5.2 (updated reference).

### 11.4 ✅ Integration of fake backend as default for development

**Resolved.** Always-fake approved for this test suite. As
established in DD-Fake, the fake is a best-effort approximation
designed to unblock AI behavior authoring, not a strict oracle for
cross-backend test parity. The 8 scenarios validate ECS logic,
the `IAnimationBackend` bridge contract, and (in stage 2) network
replication.

When the Stride backend is integrated, it gets a separate, much
smaller `StrideBackendSmokeTest` suite verifying the engine boots
and the pipeline doesn't crash — not duplicating the 8 complex AI
behavior scenarios. Reflected (already) in §5.1 and DD-Fake §1
Principle 1.

---

**No residual open questions remain.** DD-Tests is fully resolved
and approved for implementation. The complete animation control
design suite (mini design + 5 DDs + DD-Fake + DD-Tests) is now
architect-approved end-to-end.

---

## Summary

DD-Tests v1.1 specifies a three-layer test pyramid for the
animation control mechanism: ~18 unit tests for
`FakeAnimationBackend` itself, ~11 system tests for individual
Muscle ECS systems, and 8 networkless integration tests for the
full pipeline. Each test is explicit about its inputs, outputs,
assertions, and timeouts for autonomous AI-agent-driven
implementation. The `PumpUntil(predicate, maxFrames,
conditionName, diagnosticDump)` helper with mandatory frame budget
prevents test hangs; on timeout, a JSON diagnostic dump shows the
full pipeline state to enable rapid agent debugging.

Per architect ruling, `PumpUntil` lives in shared integration test
infrastructure (reusable across replication tests, TKB tests, and
future suites). `DumpAnimationDiagnostics` stays in
`Hrot.Animation.Integration.Tests` because it knows the
animation-specific component shapes. The fixture passes the latter
to the former via an optional callback.

Inline hand-rolled TKB data via `BakeForTest` keeps tests
self-contained and focused on animation systems rather than the
TKB pipeline. Shared `IClassFixture` keeps the agent's
edit-compile-test loop under 10 seconds. Always-fake backend
provides the deterministic test oracle.

All four v1.0 open questions resolved per architect review.

---

*End of DD-Tests v1.1. Architect-approved for implementation.
The complete animation control design suite is now end-to-end
approved.*
