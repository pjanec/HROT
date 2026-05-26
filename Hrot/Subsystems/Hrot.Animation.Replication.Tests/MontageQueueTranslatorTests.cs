using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication.Translators.SideBuffers;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.Animation.Replication.Tests;

/// <summary>
/// Unit tests for AnimationMontageQueue and AnimationMontageQueueState egress/ingress translators.
/// </summary>
public sealed class MontageQueueTranslatorTests : IDisposable
{
    private sealed class CapturingWriter<T> : IAnimDdsWriter<T>
    {
        public List<T> Written { get; } = new();
        public void Write(T sample) => Written.Add(sample);
    }

    private readonly EntityRepository _world;
    private readonly NetworkEntityMap _entityMap;

    public MontageQueueTranslatorTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<AnimationMontageQueue>();
        _world.RegisterComponent<AnimationMontageQueueState>();
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

    // ── SC-1: MontageQueue egress fires on QueueVersion bump ─────────────────

    [Fact]
    public void MontageQueueEgress_PublishesOnQueueVersionBump()
    {
        var writer = new CapturingWriter<DdsMontageQueue>();
        var translator = new AnimationMontageQueueEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(800L);

        _world.AddComponent(entity, new AnimationMontageQueue { Count = 2, QueueVersion = 1 });

        translator.ScanAndPublish(_world);

        Assert.Equal(1, translator.SentSampleCount);
        Assert.Single(writer.Written);
        Assert.Equal(2, writer.Written[0].Count);
        Assert.Equal(1u, writer.Written[0].QueueVersion);
    }

    // ── SC-2: MontageQueue egress does NOT fire if QueueVersion unchanged ─────

    [Fact]
    public void MontageQueueEgress_DoesNotPublish_WhenQueueVersionUnchanged()
    {
        var writer = new CapturingWriter<DdsMontageQueue>();
        var translator = new AnimationMontageQueueEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(801L);

        _world.AddComponent(entity, new AnimationMontageQueue { QueueVersion = 5, Count = 1 });
        translator.ScanAndPublish(_world);
        writer.Written.Clear();

        // Change Count but NOT QueueVersion — should NOT publish
        _world.SetComponent(entity, new AnimationMontageQueue { QueueVersion = 5, Count = 2 });
        translator.ScanAndPublish(_world);

        Assert.Empty(writer.Written);
        Assert.Equal(1, translator.DirtyFalsePositiveCount);
    }

    // ── SC-3: LogicalPayloadBytes formula — Count=3 => 60 bytes ──────────────

    [Fact]
    public void MontageQueue_LogicalPayloadBytes_EqualsHeaderPlusLiveEntries()
    {
        // DD-2 §4.2: logical payload = EntityId(8) + QueueVersion(4) + Count * 16.
        // The DDS wire frame is fixed-size (DdsMontageQueue struct) but only 12 + Count*16
        // bytes carry meaningful content.
        Assert.Equal(60, AnimationMontageQueueEgressTranslator.LogicalPayloadBytes(3));
        Assert.Equal(12, AnimationMontageQueueEgressTranslator.LogicalPayloadBytes(0));
        Assert.Equal(140, AnimationMontageQueueEgressTranslator.LogicalPayloadBytes(8));
    }

    // ── SC-3b: Egress zeroes tail entries in wire message ─────────────────────

    [Fact]
    public unsafe void MontageQueueEgress_WireMessage_ZeroesTailEntriesBeyondCount()
    {
        var writer = new CapturingWriter<DdsMontageQueue>();
        var translator = new AnimationMontageQueueEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(802L);

        // Build a queue with 3 live entries and stale data in tail slots
        var q = new AnimationMontageQueue { Count = 3, QueueVersion = 2 };
        var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
            new Span<byte>(q.EntriesData, 128));
        entries[0] = new MontageQueueEntry { MontageId = 11, PlayRate = 1.0f };
        entries[1] = new MontageQueueEntry { MontageId = 22, PlayRate = 1.0f };
        entries[2] = new MontageQueueEntry { MontageId = 33, PlayRate = 1.0f };
        // Put stale junk in tail slots (simulating previously occupied entries)
        entries[3] = new MontageQueueEntry { MontageId = 99, PlayRate = 9.9f };
        entries[4] = new MontageQueueEntry { MontageId = 99, PlayRate = 9.9f };
        _world.AddComponent(entity, q);

        translator.ScanAndPublish(_world);
        Assert.Single(writer.Written);

        var msg = writer.Written[0];
        Assert.Equal(3, msg.Count);

        // Wire message must have zeros in the tail beyond Count entries
        var wireEntries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
            new Span<byte>(msg.EntriesData, 128));
        Assert.Equal(11, wireEntries[0].MontageId);
        Assert.Equal(22, wireEntries[1].MontageId);
        Assert.Equal(33, wireEntries[2].MontageId);
        for (int i = 3; i < 8; i++)
        {
            Assert.Equal(0, wireEntries[i].MontageId);
            Assert.Equal(0f, wireEntries[i].PlayRate);
        }
    }

    // ── SC-4: MontageQueue round-trip: 3 entries correct, tail zeroed ─────────

    [Fact]
    public unsafe void MontageQueueRoundTrip_ThreeEntries_TailIsZeroed()
    {
        var egressWriter = new CapturingWriter<DdsMontageQueue>();
        var egressTranslator = new AnimationMontageQueueEgressTranslator(egressWriter, _entityMap);
        var entity = SpawnEntity(900L);

        // Build a queue with 3 entries
        var q = new AnimationMontageQueue { Count = 3, QueueVersion = 7 };
        var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
            new Span<byte>(q.EntriesData, 128));
        entries[0] = new MontageQueueEntry { MontageId = 101, PlayRate = 1.0f };
        entries[1] = new MontageQueueEntry { MontageId = 102, PlayRate = 1.5f };
        entries[2] = new MontageQueueEntry { MontageId = 103, PlayRate = 2.0f };
        _world.AddComponent(entity, q);

        egressTranslator.ScanAndPublish(_world);
        var msg = egressWriter.Written[0];

        Assert.Equal(3, msg.Count);
        Assert.Equal(7u, msg.QueueVersion);

        // Ingress side
        var ingressMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<AnimationMontageQueue>();
        var ingressEntity = ingressWorld.CreateEntity();
        ingressMap.Register(900L, ingressEntity);
        ingressWorld.AddComponent(ingressEntity, new AnimationMontageQueue());

        var ingressTranslator = new AnimationMontageQueueIngressTranslator(participant: null, ingressMap);
        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        var result = ingressWorld.GetComponentRO<AnimationMontageQueue>(ingressEntity);
        Assert.Equal(3, result.Count);
        Assert.Equal(7u, result.QueueVersion);

        var resultEntries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
            new Span<byte>(result.EntriesData, 128));
        Assert.Equal(101, resultEntries[0].MontageId);
        Assert.Equal(102, resultEntries[1].MontageId);
        Assert.Equal(103, resultEntries[2].MontageId);

        // Tail entries (3-7) must be zeroed
        for (int i = 3; i < 8; i++)
        {
            Assert.Equal(0, resultEntries[i].MontageId);
        }

        ingressWorld.Dispose();
    }

    // ── SC-5: MontageQueueState egress fires on CurrentEntryIndex change ──────

    [Fact]
    public void MontageQueueStateEgress_PublishesOnCurrentEntryIndexChange()
    {
        var writer = new CapturingWriter<DdsMontageQueueState>();
        var translator = new AnimationMontageQueueStateEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(1000L);

        _world.AddComponent(entity, new AnimationMontageQueueState
        {
            CurrentEntryIndex = 1,
            InBlendOutWindow = false,
            EntryElapsedSeconds = 0.5f,
        });

        translator.ScanAndPublish(_world);

        Assert.Equal(1, translator.SentSampleCount);
        Assert.Single(writer.Written);
        Assert.Equal(1, writer.Written[0].CurrentEntryIndex);
        Assert.Equal(0.5f, writer.Written[0].EntryElapsedSeconds);
    }

    // ── SC-6: MontageQueueState egress does NOT fire on EntryElapsedSeconds change

    [Fact]
    public void MontageQueueStateEgress_DoesNotPublish_WhenOnlyElapsedSecondsChanges()
    {
        var writer = new CapturingWriter<DdsMontageQueueState>();
        var translator = new AnimationMontageQueueStateEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(1001L);

        _world.AddComponent(entity, new AnimationMontageQueueState
        {
            CurrentEntryIndex = 0,
            InBlendOutWindow = false,
            EntryElapsedSeconds = 0.0f,
        });
        translator.ScanAndPublish(_world);
        writer.Written.Clear();

        // Only EntryElapsedSeconds changes — NOT a dirty trigger
        _world.SetComponent(entity, new AnimationMontageQueueState
        {
            CurrentEntryIndex = 0,
            InBlendOutWindow = false,
            EntryElapsedSeconds = 3.0f,
        });
        translator.ScanAndPublish(_world);

        Assert.Empty(writer.Written);
        Assert.Equal(1, translator.DirtyFalsePositiveCount);
    }

    // ── SC-7: MontageQueueState ingress preserves ObservedQueueVersion ────────

    [Fact]
    public void MontageQueueStateIngress_PreservesObservedQueueVersion()
    {
        var ingressMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<AnimationMontageQueueState>();
        var ingressEntity = ingressWorld.CreateEntity();
        ingressMap.Register(1002L, ingressEntity);
        ingressWorld.AddComponent(ingressEntity, new AnimationMontageQueueState { ObservedQueueVersion = 42 });

        var ingressTranslator = new AnimationMontageQueueStateIngressTranslator(participant: null, ingressMap);
        var msg = new DdsMontageQueueState
        {
            EntityId = 1002L,
            CurrentEntryIndex = 2,
            InBlendOutWindow = 1,
            EntryElapsedSeconds = 1.25f,
        };

        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        ref readonly var result = ref ingressWorld.GetComponentRO<AnimationMontageQueueState>(ingressEntity);
        Assert.Equal(2, result.CurrentEntryIndex);
        Assert.True(result.InBlendOutWindow);
        Assert.Equal(1.25f, result.EntryElapsedSeconds);
        // ObservedQueueVersion must NOT have been overwritten
        Assert.Equal(42u, result.ObservedQueueVersion);

        ingressWorld.Dispose();
    }
}
