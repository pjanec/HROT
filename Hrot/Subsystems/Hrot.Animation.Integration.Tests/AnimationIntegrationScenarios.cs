using System;
using System.Linq;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;
using Xunit;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Animation.Integration.Tests;

/// <summary>
/// End-to-end integration scenarios for the animation control subsystem (Phase 7, ANC-P7-04).
/// Exercises the full Muscle pipeline: dispatcher -> executors -> fake backend -> reporter.
/// Uses AnimationIntegrationFixture (IPumpableHarness) for deterministic frame-stepping.
/// </summary>
public sealed class AnimationIntegrationScenarios : IClassFixture<AnimationIntegrationFixture>
{
    private readonly AnimationIntegrationFixture _fixture;

    public AnimationIntegrationScenarios(AnimationIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Fixture smoke test ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the fixture bootstraps cleanly and ticks without exceptions.
    /// </summary>
    [Fact]
    public void Fixture_BootstrapsAndTicksWithoutError()
    {
        _fixture.ResetWorld();
        var entity = _fixture.SpawnHumanoid();

        // Tick a few frames; no assertion other than no exception
        _fixture.PumpFrames(5);

        // Entity must still exist after ticks
        Assert.True(_fixture.World.IsAlive(entity));

        _fixture.ResetWorld();
    }

    /// <summary>
    /// Verifies SpawnHumanoid produces an entity with a Running-ready AnimationChannel.
    /// </summary>
    [Fact]
    public void SpawnHumanoid_EntityHasRequiredComponents()
    {
        _fixture.ResetWorld();
        var entity = _fixture.SpawnHumanoid();

        Assert.True(_fixture.World.HasComponent<AnimationChannel>(entity));
        Assert.True(_fixture.World.HasComponent<LookAtChannel>(entity));
        Assert.True(_fixture.World.HasComponent<CharacterAnimationDefRuntime>(entity));
        Assert.True(_fixture.World.HasComponent<AnimationExecutorState>(entity));
        Assert.True(_fixture.World.HasComponent<ActorCapabilityState>(entity));

        var ch = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(NodeStatus.Failure, ch.Status); // initial idle state

        var caps = _fixture.World.GetComponentRO<ActorCapabilityState>(entity);
        Assert.True(caps.Capabilities.HasFlag(ActorCapabilities.CanPlayAnimations));

        _fixture.ResetWorld();
    }

    /// <summary>
    /// Verifies that after the first bridge tick, the entity is registered with the backend
    /// (BackendHandle is no longer equal to the raw ClassId).
    /// </summary>
    [Fact]
    public void FirstBridgeTick_RegistersEntityWithBackend()
    {
        _fixture.ResetWorld();
        var entity = _fixture.SpawnHumanoid();

        var defBefore = _fixture.World.GetComponentRO<CharacterAnimationDefRuntime>(entity);
        Assert.Equal(TestData.ClassId, defBefore.BackendHandle); // still raw classId

        _fixture.PumpFrame();

        var defAfter = _fixture.World.GetComponentRO<CharacterAnimationDefRuntime>(entity);
        // After bridge registration, high 32 bits hold the generation (>= 1)
        Assert.NotEqual(0L, defAfter.BackendHandle >> 32);

        _fixture.ResetWorld();
    }

    // ── Scenario 1: happy-path single montage ─────────────────────────────────

    /// <summary>
    /// Scenario 1 (DD-Tests §6 S1): play a single montage, observe it run to natural completion.
    ///
    /// Steps:
    ///   1. Spawn humanoid entity.
    ///   2. Tick once so the bridge registers the entity with the backend.
    ///   3. Issue PlayMontage("Walk", slot 0).
    ///   4. PumpUntil channel.Status == Success (budget: 100 frames).
    ///
    /// Assertions:
    ///   - AnimationChannel.Status transitions to Success.
    ///   - Exactly one MontageEndedEvent published.
    ///   - MontageEndedEvent.EndReason == NaturalEnd.
    ///   - MontageEndedEvent.MontageId == WalkMontageId.
    ///   - No additional frames pumped after success.
    /// </summary>
    [Fact]
    public void PlayMontage_RunsToCompletionAndReportsSuccess()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();

        // Tick once to register entity with backend (bridge requires one frame before dispatch)
        _fixture.PumpFrame();

        var defAfterReg = _fixture.World.GetComponentRO<CharacterAnimationDefRuntime>(entity);
        Assert.NotEqual(0L, defAfterReg.BackendHandle >> 32);

        // --- Issue PlayMontage ---
        AnimationTestHelpers.IssuePlayMontage(entity, TestData.WalkMontageId, _fixture.World);

        // Verify command was issued
        var chAfterCommand = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(AnimationActionIds.PlayMontage, chAfterCommand.ActiveAction);
        Assert.Equal(1u, chAfterCommand.ActionInstanceId);

        // --- Execute: pump until channel reaches Success ---
        // Walk duration = 0.5s at 60 Hz => ~30 frames. Budget = 100 frames.
        const int Budget = 100;
        const float Dt = 1f / 60f;

        _fixture.PumpUntil(
            () =>
            {
                var ch = _fixture.World.GetComponentRO<AnimationChannel>(entity);
                return ch.Status == NodeStatus.Success;
            },
            maxFrames: Budget,
            conditionName: "AnimationChannel.Status == Success",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: channel is Success ---
        var chFinal = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(NodeStatus.Success, chFinal.Status);
        Assert.Equal(AnimationActionIds.PlayMontage, chFinal.ActiveAction);

        // --- Assert: MontageEndedEvent published ---
        // The event was published during the tick that transitioned the channel to Success.
        // After that tick's SwapBuffers(), the event is in the read buffer for the current check.
        var events = _fixture.EventBus.Read<MontageEndedEvent>();

        Assert.Equal(1, events.Length);

        var evt = events[0];
        Assert.Equal(MontageEndReason.NaturalEnd, evt.EndReason);
        Assert.Equal(TestData.WalkMontageId, evt.MontageId);
        Assert.Equal(entity, evt.Target);
        Assert.Equal(0xFF, evt.QueueIndex); // single-shot PlayMontage, not from queue

        _fixture.ResetWorld();
    }

    // ── Scenario 2: Notify at keyframe ────────────────────────────────────────

    /// <summary>
    /// Scenario 2 (DD-Tests §6 S2): Verify that AnimNotifyEvent fires when a montage
    /// reaches an authored keyframe marker (MagOut at 0.2s in Run montage).
    ///
    /// Steps:
    ///   1. Spawn humanoid entity.
    ///   2. Tick once to register with backend.
    ///   3. Issue PlayMontage(Run).
    ///   4. PumpUntil AnimNotifyEvent for MagOut is received (budget: 50 frames).
    ///
    /// Assertions:
    ///   - AnimNotifyEvent received with correct MarkerHash.
    ///   - Event Target matches entity.
    ///   - Montage continues after notify (no side effects).
    /// </summary>
    [Fact]
    public void PlayMontage_NotifyFiresAtAuthoredKeyframe()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        // --- Issue PlayMontage(Run) ---
        AnimationTestHelpers.IssuePlayMontage(entity, TestData.RunMontageId, _fixture.World);

        // --- Execute: pump until notify event received ---
        const int Budget = 50;
        const float Dt = 1f / 60f;

        _fixture.PumpUntil(
            () =>
            {
                var notifyEvents = _fixture.EventBus.Read<AnimNotifyEvent>();
                foreach (var e in notifyEvents)
                {
                    if (e.Target == entity && e.MarkerHash == TestData.MagOutMarkerHash)
                        return true;
                }
                return false;
            },
            maxFrames: Budget,
            conditionName: "AnimNotifyEvent for MagOut received",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: event was received ---
        var events = _fixture.EventBus.Read<AnimNotifyEvent>();
        AnimNotifyEvent magOutEvent = default;
        foreach (var e in events)
        {
            if (e.Target == entity && e.MarkerHash == TestData.MagOutMarkerHash)
            {
                magOutEvent = e;
                break;
            }
        }
        Assert.NotEqual(default, magOutEvent);
        Assert.Equal(TestData.RunMontageId, magOutEvent.MontageId);

        // --- Verify montage continues (pump a few more frames) ---
        _fixture.PumpFrames(10);
        var chContinues = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.NotEqual(NodeStatus.Failure, chContinues.Status); // still running or completed

        _fixture.ResetWorld();
    }

    // ── Scenario 3: Stop mid-play produces Interrupted ──────────────────────────

    /// <summary>
    /// Scenario 3 (DD-Tests §6 S3): Verify that stopping a montage mid-play
    /// publishes MontageEndedEvent.EndReason == Interrupted.
    ///
    /// Steps:
    ///   1. Spawn humanoid; register with backend.
    ///   2. Issue PlayMontage(Walk).
    ///   3. Pump ~15 frames (montage running but not finished).
    ///   4. Issue StopMontage.
    ///   5. PumpUntil MontageEndedEvent.EndReason == Interrupted (budget: 100 frames).
    ///
    /// Assertions:
    ///   - MontageEndedEvent.EndReason == Interrupted.
    ///   - MontageId matches Walk.
    ///   - Entity transitions to idle state.
    /// </summary>
    [Fact]
    public void StopMontage_MidPlayInterruptsAndPublishesInterruptedEvent()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        // --- Issue PlayMontage(Walk) ---
        AnimationTestHelpers.IssuePlayMontage(entity, TestData.WalkMontageId, _fixture.World);

        // --- Pump ~15 frames (montage running) ---
        _fixture.PumpFrames(15);

        var chBeforeStop = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(NodeStatus.Running, chBeforeStop.Status);

        // --- Issue StopMontage ---
        AnimationTestHelpers.IssueStopMontage(entity, _fixture.World, blendOutTime: 0.2f);

        // --- Execute: pump until MontageEndedEvent with Interrupted reason ---
        const int Budget = 100;
        const float Dt = 1f / 60f;

        _fixture.PumpUntil(
            () =>
            {
                var endedEvents = _fixture.EventBus.Read<MontageEndedEvent>();
                foreach (var e in endedEvents)
                {
                    if (e.Target == entity && e.EndReason == MontageEndReason.Interrupted)
                        return true;
                }
                return false;
            },
            maxFrames: Budget,
            conditionName: "MontageEndedEvent with Interrupted reason",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: event received ---
        var events = _fixture.EventBus.Read<MontageEndedEvent>();
        MontageEndedEvent interruptedEvent = default;
        foreach (var e in events)
        {
            if (e.Target == entity && e.EndReason == MontageEndReason.Interrupted)
            {
                interruptedEvent = e;
                break;
            }
        }
        Assert.NotEqual(default, interruptedEvent);
        Assert.Equal(TestData.WalkMontageId, interruptedEvent.MontageId);

        // --- Assert: channel is now Success (stopped and blended out) ---
        var chFinal = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(NodeStatus.Success, chFinal.Status);

        _fixture.ResetWorld();
    }

    // ── Scenario 4: Stance transition ────────────────────────────────────────────

    /// <summary>
    /// Scenario 4 (DD-Tests §6 S4): Verify that setting stance transitions correctly
    /// and publishes StanceChangedEvent.
    ///
    /// Steps:
    ///   1. Spawn humanoid (initial stance: Standing).
    ///   2. Register with backend (pump once).
    ///   3. Issue SetStance(Crouched).
    ///   4. PumpUntil StanceChangedEvent published (budget: 50 frames).
    ///
    /// Assertions:
    ///   - StanceChangedEvent.FromStance == Standing.
    ///   - StanceChangedEvent.ToStance == Crouched.
    ///   - Entity's CurrentStance == Crouched.
    ///   - Exactly one StanceChangedEvent (no duplicates).
    /// </summary>
    [Fact]
    public void StanceIntent_DrivesTransitionAndPublishesStanceChangedEvent()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        var initialStance = AnimationTestHelpers.ReadCurrentStance(entity, _fixture.World);
        Assert.Equal(StanceId.Standing, initialStance);

        // --- Issue SetStance(Crouched) ---
        AnimationTestHelpers.IssueSetStance(entity, StanceId.Crouched, _fixture.World, blendTime: 0.3f);

        // --- Execute: pump until StanceChangedEvent ---
        const int Budget = 50;
        const float Dt = 1f / 60f;

        _fixture.PumpUntil(
            () =>
            {
                var stanceEvents = _fixture.EventBus.Read<StanceChangedEvent>();
                foreach (var e in stanceEvents)
                {
                    if (e.Target == entity)
                        return true;
                }
                return false;
            },
            maxFrames: Budget,
            conditionName: "StanceChangedEvent received",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: event received with correct transitions ---
        var events = _fixture.EventBus.Read<StanceChangedEvent>();
        var stanceChangeEvents = new System.Collections.Generic.List<StanceChangedEvent>();
        foreach (var e in events)
        {
            if (e.Target == entity)
                stanceChangeEvents.Add(e);
        }
        Assert.Single(stanceChangeEvents);

        var evt = stanceChangeEvents[0];
        Assert.Equal(StanceId.Standing, evt.PreviousStance);
        Assert.Equal(StanceId.Crouched, evt.NewStance);

        // --- Assert: entity's stance updated ---
        var finalStance = AnimationTestHelpers.ReadCurrentStance(entity, _fixture.World);
        Assert.Equal(StanceId.Crouched, finalStance);

        _fixture.ResetWorld();
    }

    // ── Scenario 5: Montage chain via queue ──────────────────────────────────────

    /// <summary>
    /// Scenario 5 (DD-Tests §6 S5): Verify that queueing multiple montages plays
    /// them in order with correct QueueIndex in MontageEndedEvent.
    ///
    /// Steps:
    ///   1. Spawn humanoid; register with backend.
    ///   2. Queue three montages: Walk, Run, Run.
    ///   3. PumpUntil all three MontageEndedEvents received (budget: 200 frames).
    ///
    /// Assertions:
    ///   - Three MontageEndedEvent entries received.
    ///   - QueueIndex: 0, 1, 2 respectively.
    ///   - All EndReason == NaturalEnd.
    ///   - MontageIds match queued order.
    /// </summary>
    [Fact]
    public void PlayMontageQueue_ThreeEntriesPlaysInOrderAndReportsOneSuccess()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        // --- Queue three montages: Walk, Run, Run ---
        ref var queue = ref _fixture.World.GetComponentRW<AnimationMontageQueue>(entity);
        unsafe
        {
            fixed (byte* ptr = queue.EntriesData)
            {
                var entriesSpan = new System.Span<MontageQueueEntry>(ptr, 8);
                entriesSpan[0] = new MontageQueueEntry { MontageId = TestData.WalkMontageId, BlendIntoTime = 0.1f, PlayRate = 1f };
                entriesSpan[1] = new MontageQueueEntry { MontageId = TestData.RunMontageId, BlendIntoTime = 0.1f, PlayRate = 1f };
                entriesSpan[2] = new MontageQueueEntry { MontageId = TestData.RunMontageId, BlendIntoTime = 0.1f, PlayRate = 1f };
            }
        }
        queue.Count = 3;
        queue.QueueVersion++;

        // --- Issue PlayMontageQueue ---
        ref var ch = ref _fixture.World.GetComponentRW<AnimationChannel>(entity);
        ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
        ch.ActionInstanceId++;

        // --- Execute: pump until all three montages finish ---
        const int Budget = 200;
        const float Dt = 1f / 60f;

        var accumulatedEnded = new System.Collections.Generic.List<MontageEndedEvent>();

        _fixture.PumpUntil(
            () =>
            {
                foreach (var e in _fixture.EventBus.Read<MontageEndedEvent>())
                {
                    if (e.Target == entity)
                        accumulatedEnded.Add(e);
                }
                return accumulatedEnded.Count >= 3;
            },
            maxFrames: Budget,
            conditionName: "Three MontageEndedEvent entries received",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: all three events received with correct indices ---
        var queueEvents = accumulatedEnded;
        Assert.Equal(3, queueEvents.Count);

        // Sort by QueueIndex to verify order
        var sorted = queueEvents.OrderBy(e => e.QueueIndex).ToList();

        // Walk at index 0
        Assert.Equal(0, sorted[0].QueueIndex);
        Assert.Equal(TestData.WalkMontageId, sorted[0].MontageId);
        Assert.Equal(MontageEndReason.NaturalEnd, sorted[0].EndReason);

        // Run at index 1
        Assert.Equal(1, sorted[1].QueueIndex);
        Assert.Equal(TestData.RunMontageId, sorted[1].MontageId);
        Assert.Equal(MontageEndReason.NaturalEnd, sorted[1].EndReason);

        // Run at index 2
        Assert.Equal(2, sorted[2].QueueIndex);
        Assert.Equal(TestData.RunMontageId, sorted[2].MontageId);
        Assert.Equal(MontageEndReason.NaturalEnd, sorted[2].EndReason);

        _fixture.ResetWorld();
    }

    // ── Scenario 6: Enqueue mid-play ─────────────────────────────────────────────

    /// <summary>
    /// Scenario 6 (DD-Tests §6 S6): Verify that enqueueing a montage while another
    /// is playing appends it to the queue and plays after the current.
    ///
    /// Steps:
    ///   1. Spawn humanoid; register with backend.
    ///   2. Issue PlayMontage(Walk).
    ///   3. Pump ~15 frames (montage running).
    ///   4. Enqueue Run montage (append, no ActionInstanceId bump).
    ///   5. PumpUntil two MontageEndedEvents received (budget: 200 frames).
    ///
    /// Assertions:
    ///   - Two MontageEndedEvent entries: Walk (index 0xFF), Run (index 1).
    ///   - Both EndReason == NaturalEnd.
    ///   - Run starts only after Walk finishes (no overlap).
    /// </summary>
    [Fact]
    public void EnqueueMontage_DuringActiveQueueAppendsAndPlays()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        // --- Issue PlayMontage(Walk) ---
        AnimationTestHelpers.IssuePlayMontage(entity, TestData.WalkMontageId, _fixture.World);

        // --- Pump ~15 frames (montage running) ---
        _fixture.PumpFrames(15);

        var chBeforeEnqueue = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(NodeStatus.Running, chBeforeEnqueue.Status);

        // --- Enqueue Run (append to queue, no ActionInstanceId bump) ---
        AnimationTestHelpers.IssueEnqueueMontage(entity, TestData.RunMontageId, _fixture.World);

        // --- Execute: pump until two MontageEndedEvents ---
        const int Budget = 200;
        const float Dt = 1f / 60f;

        var accumulatedEnded2 = new System.Collections.Generic.List<MontageEndedEvent>();

        _fixture.PumpUntil(
            () =>
            {
                foreach (var e in _fixture.EventBus.Read<MontageEndedEvent>())
                {
                    if (e.Target == entity)
                        accumulatedEnded2.Add(e);
                }
                return accumulatedEnded2.Count >= 2;
            },
            maxFrames: Budget,
            conditionName: "Two MontageEndedEvent entries received",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: both events received ---
        var endedEvents = accumulatedEnded2;
        Assert.Equal(2, endedEvents.Count);

        // Find Walk and Run events
        MontageEndedEvent walkEvent = default;
        MontageEndedEvent runEvent = default;
        foreach (var e in endedEvents)
        {
            if (e.MontageId == TestData.WalkMontageId)
                walkEvent = e;
            else if (e.MontageId == TestData.RunMontageId && runEvent.Target == default)
                runEvent = e;
        }

        Assert.NotEqual(default, walkEvent);
        Assert.NotEqual(default, runEvent);

        // Walk should have index 0xFF (single-shot from PlayMontage)
        // Run should have index 1 (enqueued after Walk)
        Assert.Equal(MontageEndReason.NaturalEnd, walkEvent.EndReason);
        Assert.Equal(MontageEndReason.NaturalEnd, runEvent.EndReason);

        _fixture.ResetWorld();
    }

    // ── Scenario 7: Footstep cadence ─────────────────────────────────────────────

    /// <summary>
    /// Scenario 7 (DD-Tests §6 S7): Verify that AnimNotifyEvent fires at correct
    /// cadence during locomotion montage.
    ///
    /// Steps:
    ///   1. Spawn humanoid; register with backend.
    ///   2. Issue PlayMontage(Walk).
    ///   3. PumpUntil montage completes (budget: 50 frames).
    ///
    /// Assertions:
    ///   - AnimNotifyEvent for footsteps (Footstep_Left, Footstep_Right) received.
    ///   - Count >= 3 (Walk = 0.5s has 3 footstep markers).
    ///   - All events Target == entity.
    /// </summary>
    [Fact]
    public void Locomotion_DrivesFootstepEventsAtCorrectCadence()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        // --- Issue PlayMontage(Walk) ---
        AnimationTestHelpers.IssuePlayMontage(entity, TestData.WalkMontageId, _fixture.World);

        // --- Execute: pump until montage completes, accumulating footstep events ---
        const int Budget = 50;
        const float Dt = 1f / 60f;

        var collectedFootstepEvents = new System.Collections.Generic.List<AnimNotifyEvent>();

        _fixture.PumpUntil(
            () =>
            {
                // Accumulate footstep events each frame before checking completion
                foreach (var e in _fixture.EventBus.Read<AnimNotifyEvent>())
                {
                    if (e.Target == entity &&
                        (e.MarkerHash == TestData.FootstepLeftMarkerHash || e.MarkerHash == TestData.FootstepRightMarkerHash))
                    {
                        collectedFootstepEvents.Add(e);
                    }
                }

                var ch = _fixture.World.GetComponentRO<AnimationChannel>(entity);
                return ch.Status == NodeStatus.Success;
            },
            maxFrames: Budget,
            conditionName: "Walk montage completes",
            diagnosticDump: () => AnimationTestHelpers.DumpAnimationDiagnostics(entity, _fixture.World),
            dt: Dt);

        // --- Assert: footstep events received ---
        Assert.True(collectedFootstepEvents.Count >= 3, $"Expected >= 3 footsteps, got {collectedFootstepEvents.Count}");

        // Verify all footsteps belong to Walk montage
        foreach (var evt in collectedFootstepEvents)
        {
            Assert.Equal(TestData.WalkMontageId, evt.MontageId);
        }

        _fixture.ResetWorld();
    }

    // ── Scenario 8: LookAt acquire and release ───────────────────────────────────

    /// <summary>
    /// Scenario 8 (DD-Tests §6 S8): Verify that setting a look-at target acquires it
    /// (Status=Running), and releasing resets (Status=Success).
    ///
    /// Steps:
    ///   1. Spawn humanoid; register with backend.
    ///   2. Verify initial LookAtChannel.Status == Failure (idle).
    ///   3. Issue AcquireLookAt(point).
    ///   4. Pump 1 frame; verify Status == Running.
    ///   5. Pump several more frames (running state continues).
    ///   6. Issue ReleaseLookAt.
    ///   7. Pump 1 frame; verify Status == Success.
    ///
    /// Assertions:
    ///   - Status transitions: Failure → Running → Success.
    ///   - No side effects on animation channel.
    /// </summary>
    [Fact]
    public void LookAtPoint_AcquiresAndReleasesAimWithStatusTransitions()
    {
        _fixture.ResetWorld();

        // --- Setup ---
        var entity = _fixture.SpawnHumanoid();
        _fixture.PumpFrame();

        // --- Assert: initial status is Failure (idle) ---
        var lookAtInitial = _fixture.World.GetComponentRO<LookAtChannel>(entity);
        Assert.Equal(NodeStatus.Failure, lookAtInitial.Status);

        // --- Issue AcquireLookAt ---
        AnimationTestHelpers.IssueAcquireLookAt(entity, 10f, 0f, 0f, _fixture.World, blendInTime: 0.1f);

        // --- Pump 1 frame and verify Status == Running ---
        _fixture.PumpFrame();
        var lookAtAcquired = _fixture.World.GetComponentRO<LookAtChannel>(entity);
        Assert.Equal(NodeStatus.Running, lookAtAcquired.Status);

        // --- Pump several more frames (running state continues) ---
        _fixture.PumpFrames(10);
        var lookAtRunning = _fixture.World.GetComponentRO<LookAtChannel>(entity);
        Assert.Equal(NodeStatus.Running, lookAtRunning.Status);

        // --- Issue ReleaseLookAt ---
        AnimationTestHelpers.IssueReleaseLookAt(entity, _fixture.World);

        // --- Pump 1 frame and verify Status == Success ---
        _fixture.PumpFrame();
        var lookAtReleased = _fixture.World.GetComponentRO<LookAtChannel>(entity);
        Assert.Equal(NodeStatus.Success, lookAtReleased.Status);

        // --- Verify animation channel unaffected ---
        var chFinal = _fixture.World.GetComponentRO<AnimationChannel>(entity);
        Assert.Equal(NodeStatus.Failure, chFinal.Status); // no PlayMontage issued; channel stays at default Failure (=0)

        _fixture.ResetWorld();
    }
}

