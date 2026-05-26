using System.Collections.Generic;
using System.Linq;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Events;
using Xunit;

namespace Hrot.Animation.Network.Integration.Tests;

/// <summary>
/// Stage-2 networked integration tests (ANC-P8-04).
///
/// Reuses the eight animation scenarios from the stage-1 suite
/// (Hrot.Animation.Integration.Tests) with a full Brain/Muscle loopback:
///   - Intent is authored on the Brain world and replicated to Muscle.
///   - Muscle animation systems execute and produce status/events.
///   - Status and events are replicated back to Brain.
///   - Assertions are made on the Brain-side outcome (not the Muscle world directly).
///
/// Extra PumpUntil budget absorbs the ~2-tick round-trip latency:
///   1 tick for Brain->Muscle intent arrival,
///   1 tick for Muscle->Brain status/event arrival.
/// All budgets add at least 6 extra frames over stage-1 equivalents.
/// </summary>
public sealed class NetworkedAnimationScenarios : IClassFixture<AnimationNetworkLoopbackFixture>
{
    private const long NetId = 1001L;
    private const float Dt = 1f / 60f;

    // Extra frames to absorb round-trip latency beyond stage-1 budgets.
    private const int RoundTripBuffer = 6;

    private readonly AnimationNetworkLoopbackFixture _fix;

    public NetworkedAnimationScenarios(AnimationNetworkLoopbackFixture fix)
    {
        _fix = fix;
        _fix.ResetWorlds();
    }

    // ── Scenario 1: Happy-path single montage completion ──────────────────────

    /// <summary>
    /// S1 (networked): Brain issues PlayMontage; Brain eventually sees AnimationChannel.Status ==
    /// Success and a MontageEndedEvent with EndReason == NaturalEnd, both originating from Muscle.
    ///
    /// Network assertions:
    /// - Brain.AnimationChannel.Status flips to Running after Brain->Muscle intent arrives (tick 2+).
    /// - Brain.AnimationChannel.Status flips to Success after Muscle->Brain status replication.
    /// - MontageEndedEvent on Brain bus has correct MontageId and EndReason.
    /// </summary>
    [Fact]
    public void Networked_PlayMontage_RunsToCompletionAndBrainSeesSuccess()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);

        // Register with backend: pump 1 frame so bridge initialises the Muscle entity.
        _fix.PumpFrame();

        // Issue PlayMontage on Brain entity (intent author side)
        AnimationTestHelpers.IssuePlayMontage(brainEntity, TestData.WalkMontageId, _fix.BrainWorld);

        // Verify intent was written to Brain entity
        var chAfterIntent = _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity);
        Assert.Equal(AnimationActionIds.PlayMontage, chAfterIntent.ActiveAction);

        // Pump until Brain sees Running (intent arrived at Muscle, Muscle acked, status replicated back)
        // Stage-1 budget: 5 frames. Add RoundTripBuffer.
        _fix.PumpUntil(
            () => _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity).Status == NodeStatus.Running,
            maxFrames: 5 + RoundTripBuffer,
            conditionName: "Brain.AnimationChannel.Status == Running");

        var chRunning = _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity);
        Assert.Equal(NodeStatus.Running, chRunning.Status);
        // DispatchedInstanceId must have been replicated from Muscle to Brain.
        Assert.NotEqual(0u, chRunning.DispatchedInstanceId);

        // Pump until Brain sees Success AND receives the MontageEndedEvent.
        // Events have an extra 1-tick bus-swap delay vs component status (Muscle write buffer
        // is not readable until next frame). Keep pumping until both conditions are true.
        var accumulatedEnded = new List<MontageEndedEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<MontageEndedEvent>())
                {
                    if (e.Target == brainEntity)
                        accumulatedEnded.Add(e);
                }
                return accumulatedEnded.Count >= 1
                    && _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity).Status == NodeStatus.Success;
            },
            maxFrames: 100 + RoundTripBuffer,
            conditionName: "Brain.AnimationChannel.Status == Success AND MontageEndedEvent received");

        // Brain-side assertions on replicated status
        var chFinal = _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity);
        Assert.Equal(NodeStatus.Success, chFinal.Status);

        // Brain-side assertion on replicated event
        Assert.True(accumulatedEnded.Count >= 1,
            $"Expected at least 1 MontageEndedEvent on Brain bus, got {accumulatedEnded.Count}");
        var endEvt = accumulatedEnded.First(e => e.Target == brainEntity);
        Assert.Equal(MontageEndReason.NaturalEnd, endEvt.EndReason);
        Assert.Equal(TestData.WalkMontageId, endEvt.MontageId);
    }

    // ── Scenario 2: Notify at keyframe ────────────────────────────────────────

    /// <summary>
    /// S2 (networked): AnimNotifyEvent (MagOut) fires on Muscle and is replicated to Brain bus.
    ///
    /// Network assertions:
    /// - AnimNotifyEvent on Brain bus has correct MarkerHash and MontageId.
    /// - Event Target is the Brain entity (resolved via brainEntityMap).
    /// </summary>
    [Fact]
    public void Networked_PlayMontage_NotifyFiresOnBrainBus()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        // Run montage has MagOut at 0.2s
        AnimationTestHelpers.IssuePlayMontage(brainEntity, TestData.RunMontageId, _fix.BrainWorld);

        var accumulatedNotifies = new List<AnimNotifyEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<AnimNotifyEvent>())
                {
                    if (e.Target == brainEntity && e.MarkerHash == TestData.MagOutMarkerHash)
                        accumulatedNotifies.Add(e);
                }
                return accumulatedNotifies.Count > 0;
            },
            maxFrames: 50 + RoundTripBuffer,
            conditionName: "AnimNotifyEvent(MagOut) on Brain bus");

        // Brain-side assertion: event arrived with correct fields
        var notifyEvt = accumulatedNotifies.First();
        Assert.Equal(brainEntity, notifyEvt.Target);
        Assert.Equal(TestData.MagOutMarkerHash, notifyEvt.MarkerHash);
        Assert.Equal(TestData.RunMontageId, notifyEvt.MontageId);
    }

    // ── Scenario 3: Stop mid-play produces Interrupted on Brain bus ───────────

    /// <summary>
    /// S3 (networked): Brain issues StopMontage mid-play; Brain sees MontageEndedEvent with
    /// EndReason == Interrupted, replicated from Muscle.
    ///
    /// Network assertions:
    /// - Brain sees Status leaving Running after stop replication.
    /// - MontageEndedEvent.EndReason == Interrupted on Brain bus.
    /// </summary>
    [Fact]
    public void Networked_StopMontage_MidPlayBrainSeesInterruptedEvent()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        AnimationTestHelpers.IssuePlayMontage(brainEntity, TestData.WalkMontageId, _fix.BrainWorld);

        // Wait for Brain to see Running (montage started and replicated)
        _fix.PumpUntil(
            () => _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity).Status == NodeStatus.Running,
            maxFrames: 10 + RoundTripBuffer,
            conditionName: "Brain.AnimationChannel.Status == Running before stop");

        // Issue stop on Brain side
        AnimationTestHelpers.IssueStopMontage(brainEntity, _fix.BrainWorld, blendOutTime: 0.2f);

        var accumulatedEnded = new List<MontageEndedEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<MontageEndedEvent>())
                {
                    if (e.Target == brainEntity && e.EndReason == MontageEndReason.Interrupted)
                        accumulatedEnded.Add(e);
                }
                return accumulatedEnded.Count > 0;
            },
            maxFrames: 100 + RoundTripBuffer,
            conditionName: "MontageEndedEvent(Interrupted) on Brain bus");

        // Brain-side assertion on replicated event
        var interruptedEvt = accumulatedEnded.First();
        Assert.Equal(brainEntity, interruptedEvt.Target);
        Assert.Equal(MontageEndReason.Interrupted, interruptedEvt.EndReason);
        Assert.Equal(TestData.WalkMontageId, interruptedEvt.MontageId);

        // Brain-side assertion on replicated status
        var chFinal = _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity);
        Assert.NotEqual(NodeStatus.Running, chFinal.Status);
    }

    // ── Scenario 4: Stance transition observed on Brain side ──────────────────

    /// <summary>
    /// S4 (networked): Brain issues SetStance; Muscle performs transition and emits
    /// StanceChangedEvent + StanceStatus update, both replicated to Brain.
    ///
    /// Network assertions:
    /// - Brain.StanceStatus.CurrentStance == Crouched after Muscle->Brain replication.
    /// - StanceChangedEvent on Brain bus has correct PreviousStance and NewStance.
    /// </summary>
    [Fact]
    public void Networked_StanceIntent_BrainSeesReplicatedStanceStatus()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        // Verify initial stance on Brain entity
        var initialStance = _fix.BrainWorld.GetComponentRO<StanceStatus>(brainEntity);
        Assert.Equal(StanceId.Standing, initialStance.CurrentStance);

        // Brain issues SetStance intent
        AnimationTestHelpers.IssueSetStance(brainEntity, StanceId.Crouched, _fix.BrainWorld, blendTime: 0.3f);

        // Wait for Brain to see StanceStatus == Crouched AND receive StanceChangedEvent.
        // StanceChangedEvent has an extra 1-tick bus-swap delay vs StanceStatus component.
        // Keep pumping until both conditions are satisfied.
        var accumulatedStanceChanged = new List<StanceChangedEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<StanceChangedEvent>())
                {
                    if (e.Target == brainEntity)
                        accumulatedStanceChanged.Add(e);
                }
                return accumulatedStanceChanged.Count >= 1
                    && _fix.BrainWorld.GetComponentRO<StanceStatus>(brainEntity).CurrentStance
                       == StanceId.Crouched;
            },
            maxFrames: 50 + RoundTripBuffer,
            conditionName: "Brain.StanceStatus.CurrentStance == Crouched AND StanceChangedEvent received");

        // Brain-side assertion on replicated StanceStatus component
        var brainStance = _fix.BrainWorld.GetComponentRO<StanceStatus>(brainEntity);
        Assert.Equal(StanceId.Crouched, brainStance.CurrentStance);

        // Brain-side assertion on replicated StanceChangedEvent
        Assert.True(accumulatedStanceChanged.Count >= 1,
            $"Expected StanceChangedEvent on Brain bus, got {accumulatedStanceChanged.Count}");
        var stanceEvt = accumulatedStanceChanged.First();
        Assert.Equal(brainEntity, stanceEvt.Target);
        Assert.Equal(StanceId.Standing, stanceEvt.PreviousStance);
        Assert.Equal(StanceId.Crouched, stanceEvt.NewStance);
    }

    // ── Scenario 5: Montage queue chain observed on Brain side ────────────────

    /// <summary>
    /// S5 (networked): Brain queues three montages; Brain sees three MontageEndedEvents
    /// (replicated from Muscle) with correct QueueIndex values.
    ///
    /// Network assertions:
    /// - Three MontageEndedEvent entries on Brain bus, all EndReason == NaturalEnd.
    /// - QueueIndex values are 0, 1, 2.
    /// - All Target == brainEntity.
    /// </summary>
    [Fact]
    public void Networked_PlayMontageQueue_BrainSeesThreeEndedEventsInOrder()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        // Write queue entries on Brain entity
        unsafe
        {
            ref var queue = ref _fix.BrainWorld.GetComponentRW<AnimationMontageQueue>(brainEntity);
            fixed (byte* ptr = queue.EntriesData)
            {
                var entries = new System.Span<MontageQueueEntry>(ptr, 8);
                entries[0] = new MontageQueueEntry
                    { MontageId = TestData.WalkMontageId, BlendIntoTime = 0.1f, PlayRate = 1f };
                entries[1] = new MontageQueueEntry
                    { MontageId = TestData.RunMontageId, BlendIntoTime = 0.1f, PlayRate = 1f };
                entries[2] = new MontageQueueEntry
                    { MontageId = TestData.RunMontageId, BlendIntoTime = 0.1f, PlayRate = 1f };
            }
            queue.Count = 3;
            queue.QueueVersion++;
        }

        // Issue PlayMontageQueue command on Brain entity
        ref var ch = ref _fix.BrainWorld.GetComponentRW<AnimationChannel>(brainEntity);
        ch.ActiveAction = AnimationActionIds.PlayMontageQueue;
        ch.ActionInstanceId++;

        var accumulatedEnded = new List<MontageEndedEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<MontageEndedEvent>())
                {
                    if (e.Target == brainEntity)
                        accumulatedEnded.Add(e);
                }
                return accumulatedEnded.Count >= 3;
            },
            maxFrames: 200 + RoundTripBuffer,
            conditionName: "Three MontageEndedEvents on Brain bus");

        // Brain-side assertions on replicated events
        Assert.Equal(3, accumulatedEnded.Count);
        var sorted = accumulatedEnded.OrderBy(e => e.QueueIndex).ToList();

        Assert.Equal(0, sorted[0].QueueIndex);
        Assert.Equal(TestData.WalkMontageId, sorted[0].MontageId);
        Assert.Equal(MontageEndReason.NaturalEnd, sorted[0].EndReason);

        Assert.Equal(1, sorted[1].QueueIndex);
        Assert.Equal(TestData.RunMontageId, sorted[1].MontageId);
        Assert.Equal(MontageEndReason.NaturalEnd, sorted[1].EndReason);

        Assert.Equal(2, sorted[2].QueueIndex);
        Assert.Equal(TestData.RunMontageId, sorted[2].MontageId);
        Assert.Equal(MontageEndReason.NaturalEnd, sorted[2].EndReason);
    }

    // ── Scenario 6: Enqueue mid-play observed on Brain side ───────────────────

    /// <summary>
    /// S6 (networked): Brain issues PlayMontage then enqueues a second montage mid-play.
    /// Brain sees two MontageEndedEvents (Walk then Run) replicated from Muscle.
    ///
    /// Network assertions:
    /// - Two MontageEndedEvents on Brain bus, both EndReason == NaturalEnd.
    /// - Walk event precedes Run event (Walk ends first).
    /// </summary>
    [Fact]
    public void Networked_EnqueueMidPlay_BrainSeesBothEndedEvents()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        // Start with Walk
        AnimationTestHelpers.IssuePlayMontage(brainEntity, TestData.WalkMontageId, _fix.BrainWorld);

        // Wait for Brain to see Running
        _fix.PumpUntil(
            () => _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity).Status == NodeStatus.Running,
            maxFrames: 15 + RoundTripBuffer,
            conditionName: "Brain sees Running before enqueue");

        // Enqueue Run mid-play on Brain side (no ActionInstanceId bump)
        AnimationTestHelpers.IssueEnqueueMontage(brainEntity, TestData.RunMontageId, _fix.BrainWorld);

        var accumulatedEnded = new List<MontageEndedEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<MontageEndedEvent>())
                {
                    if (e.Target == brainEntity)
                        accumulatedEnded.Add(e);
                }
                return accumulatedEnded.Count >= 2;
            },
            maxFrames: 200 + RoundTripBuffer,
            conditionName: "Two MontageEndedEvents on Brain bus (Walk + Run)");

        // Brain-side assertions
        Assert.Equal(2, accumulatedEnded.Count);

        var walkEvt = accumulatedEnded.FirstOrDefault(e => e.MontageId == TestData.WalkMontageId);
        var runEvt = accumulatedEnded.FirstOrDefault(e => e.MontageId == TestData.RunMontageId);

        Assert.NotEqual(default, walkEvt);
        Assert.NotEqual(default, runEvt);
        Assert.Equal(MontageEndReason.NaturalEnd, walkEvt.EndReason);
        Assert.Equal(MontageEndReason.NaturalEnd, runEvt.EndReason);

        // Walk must appear before Run in accumulated list (received in order)
        Assert.True(accumulatedEnded.IndexOf(walkEvt) < accumulatedEnded.IndexOf(runEvt),
            "Walk MontageEndedEvent must be received before Run MontageEndedEvent");
    }

    // ── Scenario 7: Footstep cadence observed on Brain bus ────────────────────

    /// <summary>
    /// S7 (networked): Footstep AnimNotifyEvents from Muscle are replicated to Brain bus.
    ///
    /// Network assertions:
    /// - At least 3 AnimNotifyEvents with Footstep_Left or Footstep_Right on Brain bus.
    /// - All events Target == brainEntity.
    /// - All events MontageId == WalkMontageId.
    /// </summary>
    [Fact]
    public void Networked_Locomotion_BrainSeesFootstepEvents()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        AnimationTestHelpers.IssuePlayMontage(brainEntity, TestData.WalkMontageId, _fix.BrainWorld);

        var accumulatedFootsteps = new List<AnimNotifyEvent>();
        _fix.PumpUntil(
            () =>
            {
                foreach (ref readonly var e in _fix.BrainWorld.Bus.Read<AnimNotifyEvent>())
                {
                    if (e.Target == brainEntity
                        && (e.MarkerHash == TestData.FootstepLeftMarkerHash
                            || e.MarkerHash == TestData.FootstepRightMarkerHash))
                    {
                        accumulatedFootsteps.Add(e);
                    }
                }
                // Walk completes at ~0.5s (30 frames); check for success after accumulating footsteps
                return _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity).Status == NodeStatus.Success;
            },
            maxFrames: 50 + RoundTripBuffer,
            conditionName: "Walk montage completes with footsteps replicated to Brain");

        // Brain-side assertion on footstep count (Walk has 3 markers at 0.1, 0.25, 0.4s)
        Assert.True(accumulatedFootsteps.Count >= 3,
            $"Expected >= 3 footstep events on Brain bus, got {accumulatedFootsteps.Count}");

        foreach (var evt in accumulatedFootsteps)
        {
            Assert.Equal(brainEntity, evt.Target);
            Assert.Equal(TestData.WalkMontageId, evt.MontageId);
        }
    }

    // ── Scenario 8: LookAt acquire/release status transitions on Brain side ───

    /// <summary>
    /// S8 (networked): Brain issues AcquireLookAt; Muscle sets LookAtChannel.Status = Running,
    /// which is replicated to Brain. Brain then issues ReleaseLookAt; Muscle sets Status = Success,
    /// which is replicated to Brain.
    ///
    /// Network assertions:
    /// - Brain.LookAtChannel.Status transitions: Failure -> Running -> Success.
    /// - All status changes originate from Muscle and are replicated back to Brain.
    /// - Brain.AnimationChannel remains unaffected (Status == Failure, no PlayMontage issued).
    /// </summary>
    [Fact]
    public void Networked_LookAt_BrainSeesReplicatedStatusTransitions()
    {
        var brainEntity = _fix.SpawnPairedHumanoid(NetId);
        _fix.PumpFrame();

        // Verify initial LookAt status on Brain side
        var lookAtInitial = _fix.BrainWorld.GetComponentRO<LookAtChannel>(brainEntity);
        Assert.Equal(NodeStatus.Failure, lookAtInitial.Status);

        // Brain issues AcquireLookAt
        AnimationTestHelpers.IssueAcquireLookAt(brainEntity, 10f, 0f, 0f, _fix.BrainWorld, blendInTime: 0.1f);

        // Wait for Brain to see Running (LookAt status replicated from Muscle)
        _fix.PumpUntil(
            () => _fix.BrainWorld.GetComponentRO<LookAtChannel>(brainEntity).Status == NodeStatus.Running,
            maxFrames: 5 + RoundTripBuffer,
            conditionName: "Brain.LookAtChannel.Status == Running");

        var lookAtRunning = _fix.BrainWorld.GetComponentRO<LookAtChannel>(brainEntity);
        Assert.Equal(NodeStatus.Running, lookAtRunning.Status);
        // DispatchedInstanceId must have been replicated from Muscle
        Assert.NotEqual(0u, lookAtRunning.DispatchedInstanceId);

        // Hold a few more frames in Running
        _fix.PumpFrames(5);
        Assert.Equal(NodeStatus.Running,
            _fix.BrainWorld.GetComponentRO<LookAtChannel>(brainEntity).Status);

        // Brain issues ReleaseLookAt
        AnimationTestHelpers.IssueReleaseLookAt(brainEntity, _fix.BrainWorld);

        // Wait for Brain to see Success (LookAt status replicated from Muscle after release)
        _fix.PumpUntil(
            () => _fix.BrainWorld.GetComponentRO<LookAtChannel>(brainEntity).Status == NodeStatus.Success,
            maxFrames: 5 + RoundTripBuffer,
            conditionName: "Brain.LookAtChannel.Status == Success");

        var lookAtSuccess = _fix.BrainWorld.GetComponentRO<LookAtChannel>(brainEntity);
        Assert.Equal(NodeStatus.Success, lookAtSuccess.Status);

        // Animation channel must be unaffected (no PlayMontage was issued)
        var animChannel = _fix.BrainWorld.GetComponentRO<AnimationChannel>(brainEntity);
        Assert.Equal(NodeStatus.Failure, animChannel.Status);
    }
}
