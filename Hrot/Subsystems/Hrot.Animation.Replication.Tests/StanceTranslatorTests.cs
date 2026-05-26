using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication.Translators.Descriptors;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.Animation.Replication.Tests;

/// <summary>
/// Unit tests for StanceIntent and StanceStatus egress/ingress translators.
/// </summary>
public sealed class StanceTranslatorTests : IDisposable
{
    private sealed class CapturingWriter<T> : IAnimDdsWriter<T>
    {
        public List<T> Written { get; } = new();
        public void Write(T sample) => Written.Add(sample);
    }

    private readonly EntityRepository _world;
    private readonly NetworkEntityMap _entityMap;

    public StanceTranslatorTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<StanceIntent>();
        _world.RegisterComponent<StanceStatus>();
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

    // ── SC-1: StanceStatus egress fires on Phase change ──────────────────────

    [Fact]
    public void StanceStatusEgress_PublishesOnPhaseChange()
    {
        var writer = new CapturingWriter<DdsStanceStatus>();
        var translator = new StanceStatusEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(500L);

        _world.AddComponent(entity, new StanceStatus
        {
            Phase = StanceTransitionPhase.Transitioning,
            CurrentStance = StanceId.Crouched,
            AckVersion = 1,
            TransitionProgress = 0.5f,
        });

        translator.ScanAndPublish(_world);

        Assert.Equal(1, translator.SentSampleCount);
        Assert.Single(writer.Written);
        Assert.Equal((byte)StanceTransitionPhase.Transitioning, writer.Written[0].Phase);
        Assert.Equal((byte)StanceId.Crouched, writer.Written[0].CurrentStance);
        Assert.Equal(1u, writer.Written[0].AckVersion);
        Assert.Equal(0.5f, writer.Written[0].TransitionProgress);
    }

    // ── SC-2: StanceStatus egress does NOT fire when only TransitionProgress changes

    [Fact]
    public void StanceStatusEgress_DoesNotPublish_WhenOnlyTransitionProgressChanges()
    {
        var writer = new CapturingWriter<DdsStanceStatus>();
        var translator = new StanceStatusEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(501L);

        _world.AddComponent(entity, new StanceStatus
        {
            Phase = StanceTransitionPhase.Idle,
            CurrentStance = StanceId.Standing,
            AckVersion = 0,
            TransitionProgress = 0.0f,
        });
        translator.ScanAndPublish(_world); // First publish
        writer.Written.Clear();

        // Only TransitionProgress changes — dirty trigger is NOT met
        _world.SetComponent(entity, new StanceStatus
        {
            Phase = StanceTransitionPhase.Idle,
            CurrentStance = StanceId.Standing,
            AckVersion = 0,
            TransitionProgress = 0.9f, // Changed, but not a dirty trigger
        });
        translator.ScanAndPublish(_world);

        Assert.Empty(writer.Written);
        Assert.Equal(1, translator.DirtyFalsePositiveCount);
    }

    // ── SC-3: StanceStatus fires when AckVersion changes ─────────────────────

    [Fact]
    public void StanceStatusEgress_Publishes_WhenAckVersionChanges()
    {
        var writer = new CapturingWriter<DdsStanceStatus>();
        var translator = new StanceStatusEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(502L);

        _world.AddComponent(entity, new StanceStatus { AckVersion = 0 });
        translator.ScanAndPublish(_world);
        writer.Written.Clear();

        _world.SetComponent(entity, new StanceStatus { AckVersion = 1 });
        translator.ScanAndPublish(_world);

        Assert.Single(writer.Written);
        Assert.Equal(1u, writer.Written[0].AckVersion);
    }

    // ── SC-4: StanceIntent round-trip ─────────────────────────────────────────

    [Fact]
    public void StanceIntentRoundTrip_PreservesAllFields()
    {
        var egressWriter = new CapturingWriter<DdsStanceIntent>();
        var egressTranslator = new StanceIntentEgressTranslator(egressWriter, _entityMap);
        var entity = SpawnEntity(600L);

        _world.AddComponent(entity, new StanceIntent
        {
            TargetStance = StanceId.Prone,
            BlendTime = 1.5f,
            Version = 3,
        });

        egressTranslator.ScanAndPublish(_world);
        var msg = egressWriter.Written[0];

        Assert.Equal((byte)StanceId.Prone, msg.TargetStance);
        Assert.Equal(1.5f, msg.BlendTime);
        Assert.Equal(3u, msg.Version);

        // Ingress: decode msg back
        var ingressMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<StanceIntent>();
        var ingressEntity = ingressWorld.CreateEntity();
        ingressMap.Register(600L, ingressEntity);
        ingressWorld.AddComponent(ingressEntity, new StanceIntent());

        var ingressTranslator = new StanceIntentIngressTranslator(participant: null, ingressMap);
        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        ref readonly var result = ref ingressWorld.GetComponentRO<StanceIntent>(ingressEntity);
        Assert.Equal(StanceId.Prone, result.TargetStance);
        Assert.Equal(1.5f, result.BlendTime);
        Assert.Equal(3u, result.Version);

        ingressWorld.Dispose();
    }

    // ── SC-5: StanceStatus round-trip preserves TransitionProgress ────────────

    [Fact]
    public void StanceStatusRoundTrip_PreservesTransitionProgress()
    {
        var egressWriter = new CapturingWriter<DdsStanceStatus>();
        var egressTranslator = new StanceStatusEgressTranslator(egressWriter, _entityMap);
        var entity = SpawnEntity(700L);

        _world.AddComponent(entity, new StanceStatus
        {
            Phase = StanceTransitionPhase.Transitioning,
            CurrentStance = StanceId.Crouched,
            AckVersion = 2,
            TransitionProgress = 0.75f,
        });

        egressTranslator.ScanAndPublish(_world);
        var msg = egressWriter.Written[0];

        var ingressMap = new NetworkEntityMap();
        var ingressWorld = new EntityRepository();
        ingressWorld.RegisterComponent<StanceStatus>();
        var ingressEntity = ingressWorld.CreateEntity();
        ingressMap.Register(700L, ingressEntity);
        ingressWorld.AddComponent(ingressEntity, new StanceStatus());

        var ingressTranslator = new StanceStatusIngressTranslator(participant: null, ingressMap);
        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        ref readonly var result = ref ingressWorld.GetComponentRO<StanceStatus>(ingressEntity);
        Assert.Equal(StanceTransitionPhase.Transitioning, result.Phase);
        Assert.Equal(StanceId.Crouched, result.CurrentStance);
        Assert.Equal(2u, result.AckVersion);
        Assert.Equal(0.75f, result.TransitionProgress);

        ingressWorld.Dispose();
    }
}
