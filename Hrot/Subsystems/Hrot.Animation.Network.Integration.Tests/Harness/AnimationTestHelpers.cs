using System;
using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Animation.Network.Integration.Tests;

/// <summary>
/// Animation command helpers for network integration tests (ANC-P8-04).
/// Mirrors only the subset of stage-1 AnimationTestHelpers that is needed here.
/// All methods write intent components on the Brain EntityRepository.
/// </summary>
internal static class AnimationTestHelpers
{
    /// <summary>
    /// Dispatch a PlayMontage command to an entity's AnimationChannel.
    /// Writes the PlayMontageParams blob and bumps ActionInstanceId.
    /// </summary>
    public static unsafe void IssuePlayMontage(
        Entity entity,
        int montageId,
        EntityRepository repo,
        float blendInTime = -1f,
        float blendOutTime = -1f)
    {
        ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);

        var p = new PlayMontageParams
        {
            MontageId = montageId,
            PlayRate = 1.0f,
            BlendInTime = blendInTime,
            BlendOutTime = blendOutTime,
        };
        fixed (byte* dst = ch.Params)
            *(PlayMontageParams*)dst = p;

        ch.ActiveAction = AnimationActionIds.PlayMontage;
        ch.ActionInstanceId++;
    }

    /// <summary>
    /// Dispatch a StopMontage command to an entity's AnimationChannel.
    /// Bumps ActionInstanceId.
    /// </summary>
    public static unsafe void IssueStopMontage(
        Entity entity,
        EntityRepository repo,
        float blendOutTime = 0.2f)
    {
        ref var ch = ref repo.GetComponentRW<AnimationChannel>(entity);

        var p = new StopMontageParams
        {
            BlendOutTime = blendOutTime,
            StopReason = 0,
        };
        fixed (byte* dst = ch.Params)
            *(StopMontageParams*)dst = p;

        ch.ActiveAction = AnimationActionIds.StopMontage;
        ch.ActionInstanceId++;
    }

    /// <summary>
    /// Dispatch a SetStance command via the StanceIntent component.
    /// Bumps StanceIntent.Version.
    /// </summary>
    public static void IssueSetStance(
        Entity entity,
        StanceId targetStance,
        EntityRepository repo,
        float blendTime = 0.3f)
    {
        ref var stance = ref repo.GetComponentRW<StanceIntent>(entity);
        stance.TargetStance = targetStance;
        stance.BlendTime = blendTime;
        stance.Version++;
    }

    /// <summary>
    /// Append a montage to the AnimationMontageQueue side-buffer.
    /// Does NOT bump ActionInstanceId (deferred-queue pattern per DD-1 SS6.4).
    /// </summary>
    public static unsafe void IssueEnqueueMontage(
        Entity entity,
        int montageId,
        EntityRepository repo,
        float blendInTime = 0.1f,
        float playRate = 1.0f)
    {
        ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);

        fixed (byte* ptr = queue.EntriesData)
        {
            var entriesSpan = new System.Span<MontageQueueEntry>(ptr, 8);
            if (queue.Count < 8)
            {
                entriesSpan[queue.Count] = new MontageQueueEntry
                {
                    MontageId = montageId,
                    BlendIntoTime = blendInTime,
                    PlayRate = playRate,
                    StartSectionIndex = 0,
                    Flags = 0,
                };
                queue.Count++;
                queue.QueueVersion++;
            }
        }
    }

    /// <summary>
    /// Dispatch an AcquireLookAt command to an entity's LookAtChannel.
    /// Bumps ActionInstanceId.
    /// </summary>
    public static unsafe void IssueAcquireLookAt(
        Entity entity,
        float targetX,
        float targetY,
        float targetZ,
        EntityRepository repo,
        float blendInTime = 0.1f)
    {
        ref var ch = ref repo.GetComponentRW<LookAtChannel>(entity);

        var p = new LookAtPointParams
        {
            WorldPointX = targetX,
            WorldPointY = targetY,
            WorldPointZ = targetZ,
            BlendInTime = blendInTime,
            Priority = 0,
        };
        fixed (byte* dst = ch.Params)
            *(LookAtPointParams*)dst = p;

        ch.ActiveAction = LookAtActionIds.LookAtPoint;
        ch.ActionInstanceId++;
    }

    /// <summary>
    /// Dispatch a ReleaseLookAt command to an entity's LookAtChannel.
    /// Bumps ActionInstanceId.
    /// </summary>
    public static unsafe void IssueReleaseLookAt(Entity entity, EntityRepository repo)
    {
        ref var ch = ref repo.GetComponentRW<LookAtChannel>(entity);

        var p = new ReleaseLookParams { BlendOutTime = 0.1f };
        fixed (byte* dst = ch.Params)
            *(ReleaseLookParams*)dst = p;

        ch.ActiveAction = LookAtActionIds.ReleaseLook;
        ch.ActionInstanceId++;
    }
}
