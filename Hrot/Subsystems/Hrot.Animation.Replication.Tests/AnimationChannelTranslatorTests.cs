using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication.Translators.Channels;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Xunit;

namespace Hrot.Animation.Replication.Tests;

/// <summary>
/// Unit tests for AnimationChannel and LookAtChannel egress/ingress translators.
/// </summary>
public sealed class AnimationChannelTranslatorTests : IDisposable
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private sealed class CapturingWriter<T> : IAnimDdsWriter<T>
    {
        public List<T> Written { get; } = new();
        public void Write(T sample) => Written.Add(sample);
    }

    private readonly EntityRepository _world;
    private readonly NetworkEntityMap _entityMap;

    public AnimationChannelTranslatorTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<AnimationChannel>();
        _world.RegisterComponent<LookAtChannel>();
        _world.RegisterComponent<NetworkIdentity>();
        _entityMap = new NetworkEntityMap();
    }

    public void Dispose() => _world.Dispose();

    private Entity SpawnEntity(long netId)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new NetworkIdentity(netId));
        _entityMap.Register(netId, entity);
        return entity;
    }

    // ── SC-1: Intent egress fires on ActionInstanceId change ─────────────────

    [Fact]
    public void AnimChannelIntentEgress_PublishesOnActionInstanceIdChange()
    {
        var writer = new CapturingWriter<DdsAnimationChannelIntent>();
        var translator = new AnimationChannelIntentEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(100L);

        _world.AddComponent(entity, new AnimationChannel
        {
            ActiveAction = 5,
            ActionInstanceId = 42,
            BehaviorInstanceId = 10,
        });

        translator.ScanAndPublish(_world);

        Assert.Equal(1, translator.SentSampleCount);
        Assert.Single(writer.Written);
        Assert.Equal(100L, writer.Written[0].EntityId);
        Assert.Equal(42u, writer.Written[0].ActionInstanceId);
        Assert.Equal(5, writer.Written[0].ActiveAction);
    }

    // ── SC-2: Intent egress does NOT fire if ActionInstanceId unchanged ──────

    [Fact]
    public void AnimChannelIntentEgress_DoesNotPublish_WhenActionInstanceIdUnchanged()
    {
        var writer = new CapturingWriter<DdsAnimationChannelIntent>();
        var translator = new AnimationChannelIntentEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(101L);

        _world.AddComponent(entity, new AnimationChannel { ActionInstanceId = 7 });
        translator.ScanAndPublish(_world); // First call — should publish
        writer.Written.Clear();

        translator.ScanAndPublish(_world); // Second call — ActionInstanceId unchanged
        Assert.Empty(writer.Written);
        Assert.Equal(1, translator.DirtyFalsePositiveCount);
    }

    // ── SC-3: Intent egress does NOT fire when only DispatchedInstanceId changes

    [Fact]
    public void AnimChannelIntentEgress_DoesNotPublish_WhenOnlyDispatchedInstanceIdChanges()
    {
        var writer = new CapturingWriter<DdsAnimationChannelIntent>();
        var translator = new AnimationChannelIntentEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(102L);

        _world.AddComponent(entity, new AnimationChannel { ActionInstanceId = 1 });
        translator.ScanAndPublish(_world); // First call
        writer.Written.Clear();

        // Simulate DispatchedInstanceId bumping without ActionInstanceId changing
        _world.SetComponent(entity, new AnimationChannel { ActionInstanceId = 1, DispatchedInstanceId = 999 });
        translator.ScanAndPublish(_world); // Should NOT publish
        Assert.Empty(writer.Written);
    }

    // ── SC-4: Status egress fires on Status change ───────────────────────────

    [Fact]
    public void AnimChannelStatusEgress_PublishesOnStatusChange()
    {
        var writer = new CapturingWriter<DdsAnimationChannelStatus>();
        var translator = new AnimationChannelStatusEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(200L);

        _world.AddComponent(entity, new AnimationChannel
        {
            Status = NodeStatus.Running,
            DispatchedInstanceId = 3,
        });

        translator.ScanAndPublish(_world);

        Assert.Equal(1, translator.SentSampleCount);
        Assert.Single(writer.Written);
        Assert.Equal((byte)NodeStatus.Running, writer.Written[0].Status);
        Assert.Equal(3u, writer.Written[0].DispatchedInstanceId);
    }

    // ── SC-5: Status egress does NOT fire when Status/DispatchedInstanceId unchanged

    [Fact]
    public void AnimChannelStatusEgress_DoesNotPublish_WhenUnchanged()
    {
        var writer = new CapturingWriter<DdsAnimationChannelStatus>();
        var translator = new AnimationChannelStatusEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(201L);

        _world.AddComponent(entity, new AnimationChannel { Status = NodeStatus.Running });
        translator.ScanAndPublish(_world);
        writer.Written.Clear();

        translator.ScanAndPublish(_world); // No change
        Assert.Empty(writer.Written);
    }

    // ── SC-6: Intent round-trip: egress captures msg → ingress ProcessSample ─

    [Fact]
    public unsafe void AnimChannelIntentRoundTrip_PreservesIntentFieldsAndDispatchedInstanceId()
    {
        // Egress side: create msg
        var egressWriter = new CapturingWriter<DdsAnimationChannelIntent>();
        var egressTranslator = new AnimationChannelIntentEgressTranslator(egressWriter, _entityMap);
        var entity = SpawnEntity(300L);

        _world.AddComponent(entity, new AnimationChannel
        {
            ActiveAction = 3,
            ActionInstanceId = 55,
            BehaviorInstanceId = 7,
            DispatchedInstanceId = 0xDEAD, // Should be preserved by ingress
        });

        egressTranslator.ScanAndPublish(_world);
        var msg = egressWriter.Written[0];

        // Ingress side: entity with existing DispatchedInstanceId
        var ingressEntityMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<AnimationChannel>();

        var ingressEntity = ingressWorld.CreateEntity();
        ingressWorld.AddComponent(ingressEntity, new AnimationChannel { DispatchedInstanceId = 0xBEEF });
        ingressEntityMap.Register(300L, ingressEntity);

        var ingressTranslator = new AnimationChannelIntentIngressTranslator(
            participant: null, ingressEntityMap);

        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        ref readonly var result = ref ingressWorld.GetComponentRO<AnimationChannel>(ingressEntity);
        Assert.Equal(3, result.ActiveAction);
        Assert.Equal(55u, result.ActionInstanceId);
        Assert.Equal(7u, result.BehaviorInstanceId);
        // DispatchedInstanceId should be PRESERVED (not overwritten by ingress)
        Assert.Equal(0xBEEFu, result.DispatchedInstanceId);

        ingressWorld.Dispose();
    }

    // ── SC-7: LookAt intent round-trip ───────────────────────────────────────

    [Fact]
    public void LookAtChannelIntentRoundTrip_PreservesFields()
    {
        var egressWriter = new CapturingWriter<DdsLookAtChannelIntent>();
        var egressTranslator = new LookAtChannelIntentEgressTranslator(egressWriter, _entityMap);
        var entity = SpawnEntity(400L);

        _world.AddComponent(entity, new LookAtChannel
        {
            ActiveAction = 9,
            ActionInstanceId = 77,
            BehaviorInstanceId = 2,
        });

        egressTranslator.ScanAndPublish(_world);
        Assert.Single(egressWriter.Written);
        Assert.Equal(77u, egressWriter.Written[0].ActionInstanceId);
        Assert.Equal(9, egressWriter.Written[0].ActiveAction);
    }

    // ── SC-8: LookAtEntity ingress remaps TargetEntityId to local entity index

    [Fact]
    public unsafe void LookAtChannelIngress_RemapsTargetEntityId_ForLookAtEntityAction()
    {
        var ingressMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<LookAtChannel>();

        // The muscle-side entity receiving the intent
        var muscleEntity = ingressWorld.CreateEntity();
        ingressWorld.AddComponent(muscleEntity, new LookAtChannel());
        ingressMap.Register(500L, muscleEntity);

        // A second entity that is the remap target (network ID 777 -> local entity)
        var targetEntity = ingressWorld.CreateEntity();
        // targetEntity.Index is the local index we expect to be written into TargetEntityId
        ingressMap.Register(777L, targetEntity);

        var ingressTranslator = new LookAtChannelIntentIngressTranslator(participant: null, ingressMap);

        // Build a DdsLookAtChannelIntent with LookAtEntity action and TargetEntityId = 777
        var msg = new DdsLookAtChannelIntent
        {
            EntityId = 500L,
            ActiveAction = LookAtActionIds.LookAtEntity,
            ActionInstanceId = 10,
            BehaviorInstanceId = 1,
        };

        // Write LookAtEntityParams into ActionParams bytes using direct pointer (stack-allocated struct)
        DdsLookAtChannelIntent* pMsgSetup = &msg;
        LookAtEntityParams* pSetup = (LookAtEntityParams*)pMsgSetup->ActionParams;
        pSetup->TargetEntityId = 777u; // network entity ID
        pSetup->BlendInTime = 0.2f;

        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        ref readonly var result = ref ingressWorld.GetComponentRO<LookAtChannel>(muscleEntity);
        Assert.Equal(LookAtActionIds.LookAtEntity, result.ActiveAction);
        Assert.Equal(10u, result.ActionInstanceId);

        // TargetEntityId must be the LOCAL entity index, not the network ID 777
        var resultCopy = result;
        LookAtChannel* pResult = &resultCopy;
        LookAtEntityParams* rParams = (LookAtEntityParams*)pResult->Params;
        Assert.Equal((uint)targetEntity.Index, rParams->TargetEntityId);

        Assert.Equal(1, ingressTranslator.ReceivedSampleCount);

        ingressWorld.Dispose();
    }

    // ── SC-9: LookAtEntity ingress keeps channel unchanged for unknown target ─

    [Fact]
    public unsafe void LookAtChannelIngress_KeepsChannelUnchanged_WhenTargetEntityNotInMap()    {
        var ingressMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<LookAtChannel>();

        var muscleEntity = ingressWorld.CreateEntity();
        var originalChannel = new LookAtChannel
        {
            ActiveAction = LookAtActionIds.ReleaseLook,
            ActionInstanceId = 5,
        };
        ingressWorld.AddComponent(muscleEntity, originalChannel);
        ingressMap.Register(501L, muscleEntity);
        // Target entity 888 is NOT registered in the map

        var ingressTranslator = new LookAtChannelIntentIngressTranslator(participant: null, ingressMap);

        var msg = new DdsLookAtChannelIntent
        {
            EntityId = 501L,
            ActiveAction = LookAtActionIds.LookAtEntity,
            ActionInstanceId = 20,
        };
        DdsLookAtChannelIntent* pMsg9 = &msg;
        LookAtEntityParams* p9 = (LookAtEntityParams*)pMsg9->ActionParams;
        p9->TargetEntityId = 888u; // not in map

        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        // Channel must be unchanged since target was not in the entity map
        ref readonly var result = ref ingressWorld.GetComponentRO<LookAtChannel>(muscleEntity);
        Assert.Equal(originalChannel.ActiveAction, result.ActiveAction);
        Assert.Equal(originalChannel.ActionInstanceId, result.ActionInstanceId);
        Assert.Equal(0, ingressTranslator.ReceivedSampleCount);

        ingressWorld.Dispose();
    }

    // ── SC-10: OFX-012: Re-publishes when same ActionInstanceId but Params blob changed ──

    [Fact]
    public unsafe void AnimChannelIntentEgress_PublishesOnActionParamsChange_WhenSameInstanceId()
    {
        // OFX-012: The dirty check must compare the 32-byte Params blob as well as
        // ActionInstanceId. If only the Params change (same ActionInstanceId),
        // a second publish must be issued.
        var writer = new CapturingWriter<DdsAnimationChannelIntent>();
        var translator = new AnimationChannelIntentEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(600L);

        var ch = new AnimationChannel
        {
            ActiveAction = 3,
            ActionInstanceId = 42,
        };
        // Params bytes are all-zero by default (struct zero-init).
        _world.AddComponent(entity, ch);

        translator.ScanAndPublish(_world); // First scan — publishes
        Assert.Equal(1, translator.SentSampleCount);

        // Mutate only the Params blob; ActionInstanceId stays at 42.
        {
            ref var chRef = ref _world.GetComponentRW<AnimationChannel>(entity);
            fixed (byte* p = chRef.Params)
                p[0] = 0xFF; // change first byte
        }

        translator.ScanAndPublish(_world); // Second scan — must publish again (Params changed)
        Assert.Equal(2, translator.SentSampleCount);

        // A third scan with no changes must NOT publish.
        translator.ScanAndPublish(_world);
        Assert.Equal(2, translator.SentSampleCount);
        Assert.Equal(1, translator.DirtyFalsePositiveCount);
    }

    // ── SC-11: FIX2-008: LookAt re-publishes when same ActionInstanceId but Params blob changed ──

    [Fact]
    public unsafe void LookAtChannelIntentEgress_PublishesOnActionParamsChange_WhenSameInstanceId()
    {
        // FIX2-008: The dirty check on LookAtChannelIntentEgressTranslator must compare
        // the 32-byte Params blob as well as ActionInstanceId. If only the Params change
        // (same ActionInstanceId), a second publish must be issued.
        var writer = new CapturingWriter<DdsLookAtChannelIntent>();
        var translator = new LookAtChannelIntentEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(700L);

        var ch = new LookAtChannel
        {
            ActiveAction = 7,
            ActionInstanceId = 99,
        };
        // Params bytes are all-zero by default (struct zero-init).
        _world.AddComponent(entity, ch);

        translator.ScanAndPublish(_world); // First scan -- publishes
        Assert.Equal(1, translator.SentSampleCount);

        // Mutate only the Params blob; ActionInstanceId stays at 99.
        {
            ref var chRef = ref _world.GetComponentRW<LookAtChannel>(entity);
            fixed (byte* p = chRef.Params)
                p[0] = 0xAB; // change first byte
        }

        translator.ScanAndPublish(_world); // Second scan -- must publish again (Params changed)
        Assert.Equal(2, translator.SentSampleCount);

        // A third scan with no changes must NOT publish.
        translator.ScanAndPublish(_world);
        Assert.Equal(2, translator.SentSampleCount);
        Assert.Equal(1, translator.DirtyFalsePositiveCount);
    }
}
