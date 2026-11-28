using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication.Translators.Descriptors;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;
using Fdp.Toolkit.Tkb.Domain;

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

    private Entity SpawnEntity(int netId)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new NetworkIdentity(netId));
        _entityMap.Register(netId, entity);
        return entity;
    }

    /// <summary>
    /// ⭐ CE-11014 — a transition the body started at t = 40 s with a 0.8 s blend (crouched → prone), sent by the body's egress and taken in on ANOTHER
    /// node: that node reads progress 0 at t, ½ at t + 0.4, 1 at t + b and after — from the replicated start, origin and duration alone (no per-frame
    /// progress). A NEW transition start republishes even when phase, stance and version did not change. 🔒 Red-proved: the ingress dropping the start ⇒ red.
    /// </summary>
    [Fact]
    public void CE11014_ATransitionStartedAtT_ReadsProgress0AtT_And1AtTPlusBlend_OnAnotherNode()
    {
        var writer = new CapturingWriter<DdsStanceStatus>();
        var egress = new StanceStatusEgressTranslator(writer, _entityMap);
        var body = SpawnEntity(700);
        _world.AddComponent(body, new StanceStatus
        {
            Phase = StanceTransitionPhase.Transitioning, CurrentStance = StanceId.Crouched, AckVersion = 3,
            FromStance = StanceId.Crouched, TransitionStartedAt = (SimInstant)40.0, TransitionSeconds = new SimDuration(0.8f),
        });
        egress.ScanAndPublish(_world);
        var sample = Assert.Single(writer.Written);

        using var other = new EntityRepository();
        other.RegisterComponent<StanceStatus>();
        var otherMap = new NetworkEntityMap();
        var replica = other.CreateEntity();
        otherMap.Register(700, replica);
        var ingress = new StanceStatusIngressTranslator(participant: null, otherMap);
        using (var cmd = new EntityCommandBuffer())
        {
            ingress.ProcessSample(in sample, cmd, other);
            cmd.Playback(other);
        }
        var seen = other.GetComponentRO<StanceStatus>(replica);
        Assert.Equal(StanceId.Crouched, seen.FromStance);
        Assert.Equal(0f, StanceTiming.ProgressAt(in seen, 40.0));
        Assert.Equal(0.5f, StanceTiming.ProgressAt(in seen, 40.4), 3);
        Assert.Equal(1f, StanceTiming.ProgressAt(in seen, 40.8), 3);
        Assert.Equal(1f, StanceTiming.ProgressAt(in seen, 99.0));

        // a new transition with the same phase / stance / version (an interrupted blend restarting) still goes out
        writer.Written.Clear();
        var st = _world.GetComponentRO<StanceStatus>(body);
        st.TransitionStartedAt = (SimInstant)41.0;
        _world.SetComponent(body, st);
        egress.ScanAndPublish(_world);
        Assert.Equal(41.0, Assert.Single(writer.Written).TransitionStartedAt);
    }

    /// <summary>⭐ CE-11014 — the ONE timing rule: an explicit blend wins; unset ⇒ 0.35 s crouch ↔ stand, 0.8 s into or out of prone (the map's feel).</summary>
    [Fact]
    public void CE11014_TheBlendDuration_IsTheIntentsElseThePairsDefault()
    {
        Assert.Equal(0.5f, StanceTiming.Effective(StanceId.Standing, StanceId.Prone, 0.5f));
        Assert.Equal(StanceTiming.ProneSeconds, StanceTiming.Effective(StanceId.Standing, StanceId.Prone, 0f));
        Assert.Equal(StanceTiming.CrouchSeconds, StanceTiming.Effective(StanceId.Crouched, StanceId.Standing, 0f));
        Assert.Equal(1f, StanceTiming.ProgressAt(default, 10.0));   // no transition has run
    }

    // ── SC-1: StanceStatus egress fires on Phase change ──────────────────────

    [Fact]
    public void StanceStatusEgress_PublishesOnPhaseChange()
    {
        var writer = new CapturingWriter<DdsStanceStatus>();
        var translator = new StanceStatusEgressTranslator(writer, _entityMap);
        var entity = SpawnEntity(500);

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
        var entity = SpawnEntity(501);

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
        var entity = SpawnEntity(502);

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
        var entity = SpawnEntity(600);

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
        ingressMap.Register(600, ingressEntity);
        ingressWorld.AddComponent(ingressEntity, new StanceIntent());

        var ingressTranslator = new StanceIntentIngressTranslator(participant: null, ingressMap);
        var view = (ISimulationView)ingressWorld;
        var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
        ingressTranslator.ProcessSample(msg, cmd, view);
        cmd.Playback(ingressWorld);

        ref readonly var result = ref ingressWorld.GetComponentRO<StanceIntent>(ingressEntity);
        Assert.Equal(StanceId.Prone, result.TargetStance);
        Assert.Equal(1.5f, result.BlendTime.Seconds);
        Assert.Equal(3u, result.Version);

        ingressWorld.Dispose();
    }

    // ── SC-5: StanceStatus round-trip preserves TransitionProgress ────────────

    [Fact]
    public void StanceStatusRoundTrip_PreservesTransitionProgress()
    {
        var egressWriter = new CapturingWriter<DdsStanceStatus>();
        var egressTranslator = new StanceStatusEgressTranslator(egressWriter, _entityMap);
        var entity = SpawnEntity(700);

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
        ingressMap.Register(700, ingressEntity);
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
