using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication.Translators.Events;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Events;
using Xunit;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Animation.Replication.Tests;

/// <summary>
/// Unit tests for all 7 animation event translators.
/// Each test verifies encode/decode round-trip via EncodeForTest/DecodeForTest.
/// Tests use null participant (no live DDS) and inject entities via NetworkEntityMap.
/// </summary>
public sealed class EventTranslatorTests : IDisposable
{
    private readonly EntityRepository _world;
    private readonly NetworkEntityMap _entityMap;
    private readonly Entity _entity;
    private const long NetId = 9001L;

    public EventTranslatorTests()
    {
        _world = new EntityRepository();
        _entityMap = new NetworkEntityMap();
        _entity = _world.CreateEntity();
        _entityMap.Register(NetId, _entity);
    }

    public void Dispose() => _world.Dispose();

    // ── MontageStarted ────────────────────────────────────────────────────────

    [Fact]
    public void MontageStarted_RoundTrip_AllFieldsPreserved()
    {
        var translator = new MontageStartedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = MakeStartedEvent(_entity, montageId: 111, actionInstanceId: 22, queueIndex: 1);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal(111, dds.MontageId);
        Assert.Equal(22u, dds.ActionInstanceId);
        Assert.Equal(1, dds.QueueIndex);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(111, decoded.MontageId);
        Assert.Equal(22u, decoded.ActionInstanceId);
        Assert.Equal(1, decoded.QueueIndex);
    }

    // ── MontageEnded ──────────────────────────────────────────────────────────

    [Fact]
    public void MontageEnded_RoundTrip_AllFieldsPreserved()
    {
        var translator = new MontageEndedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = new MontageEndedEvent(
            _entity, montageId: 222, actionInstanceId: 33, queueIndex: 0,
            endReason: MontageEndReason.Interrupted);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal(222, dds.MontageId);
        Assert.Equal((byte)MontageEndReason.Interrupted, dds.EndReason);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(222, decoded.MontageId);
        Assert.Equal(MontageEndReason.Interrupted, decoded.EndReason);
    }

    // ── MontageSectionAdvanced ────────────────────────────────────────────────

    [Fact]
    public void MontageSectionAdvanced_RoundTrip_AllFieldsPreserved()
    {
        var translator = new MontageSectionAdvancedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = MakeSectionAdvancedEvent(_entity, montageId: 333, from: 0, to: 1);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal(333, dds.MontageId);
        Assert.Equal(0, dds.FromSectionIndex);
        Assert.Equal(1, dds.ToSectionIndex);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(0, decoded.FromSectionIndex);
        Assert.Equal(1, decoded.ToSectionIndex);
    }

    // ── StanceChanged ─────────────────────────────────────────────────────────

    [Fact]
    public void StanceChanged_RoundTrip_AllFieldsPreserved()
    {
        var translator = new StanceChangedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = new StanceChangedEvent(_entity, StanceId.Standing, StanceId.Crouched);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal((byte)StanceId.Standing, dds.PreviousStance);
        Assert.Equal((byte)StanceId.Crouched, dds.NewStance);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(StanceId.Standing, decoded.PreviousStance);
        Assert.Equal(StanceId.Crouched, decoded.NewStance);
    }

    // ── HitWindowOpened ───────────────────────────────────────────────────────

    [Fact]
    public void HitWindowOpened_RoundTrip_AllFieldsPreserved()
    {
        var translator = new HitWindowOpenedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = MakeHitWindowOpenedEvent(_entity, montageId: 444, windowId: 3);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal(444, dds.MontageId);
        Assert.Equal(3, dds.WindowId);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(444, decoded.MontageId);
        Assert.Equal(3, decoded.WindowId);
    }

    // ── HitWindowClosed ───────────────────────────────────────────────────────

    [Fact]
    public void HitWindowClosed_RoundTrip_AllFieldsPreserved()
    {
        var translator = new HitWindowClosedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = MakeHitWindowClosedEvent(_entity, montageId: 555, windowId: 7);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal(555, dds.MontageId);
        Assert.Equal(7, dds.WindowId);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(7, decoded.WindowId);
    }

    // ── AnimNotify ────────────────────────────────────────────────────────────

    [Fact]
    public void AnimNotify_RoundTrip_AllFieldsPreserved()
    {
        var translator = new AnimNotifyEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var original = new AnimNotifyEvent(_entity, montageId: 666, markerHash: 0xABCD1234, payloadFloat: 3.14f);

        Assert.True(translator.EncodeForTest(original, out var dds));
        Assert.Equal(NetId, dds.Target);
        Assert.Equal(666, dds.MontageId);
        Assert.Equal(0xABCD1234u, dds.MarkerHash);
        Assert.Equal(3.14f, dds.PayloadFloat, precision: 5);

        Assert.True(translator.DecodeForTest(dds, out var decoded));
        Assert.Equal(_entity, decoded.Target);
        Assert.Equal(0xABCD1234u, decoded.MarkerHash);
        Assert.Equal(3.14f, decoded.PayloadFloat, precision: 5);
    }

    // ── Unknown entity → encode returns false ─────────────────────────────────

    [Fact]
    public void EventTranslator_EncodeReturnsFalse_WhenEntityUnknown()
    {
        var unknownEntity = _world.CreateEntity(); // NOT registered in entityMap
        var translator = new MontageStartedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Egress);

        var evt = MakeStartedEvent(unknownEntity, 1, 1, 0);
        Assert.False(translator.EncodeForTest(evt, out _));
    }

    // ── Decode unknown network ID → returns false ─────────────────────────────

    [Fact]
    public void EventTranslator_DecodeReturnsFalse_WhenNetworkIdUnknown()
    {
        var translator = new MontageStartedEventTranslator(
            participant: null, _entityMap, TranslatorDirection.Ingress);

        var dds = new DdsMontageStartedEvent { Target = 9999L }; // unknown net ID
        Assert.False(translator.DecodeForTest(dds, out _));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MontageStartedEvent MakeStartedEvent(
        Entity target, int montageId, uint actionInstanceId, byte queueIndex)
    {
        var proxy = new MontageStartedProxy
        {
            Target = target,
            MontageId = montageId,
            ActionInstanceId = actionInstanceId,
            QueueIndex = queueIndex,
        };
        return Unsafe.As<MontageStartedProxy, MontageStartedEvent>(ref proxy);
    }

    private static MontageSectionAdvancedEvent MakeSectionAdvancedEvent(
        Entity target, int montageId, byte from, byte to)
    {
        var proxy = new MontageSectionAdvancedProxy
        {
            Target = target,
            MontageId = montageId,
            FromSectionIndex = from,
            ToSectionIndex = to,
        };
        return Unsafe.As<MontageSectionAdvancedProxy, MontageSectionAdvancedEvent>(ref proxy);
    }

    private static HitWindowOpenedEvent MakeHitWindowOpenedEvent(
        Entity target, int montageId, byte windowId)
    {
        var proxy = new HitWindowProxy
        {
            Target = target,
            MontageId = montageId,
            WindowId = windowId,
        };
        return Unsafe.As<HitWindowProxy, HitWindowOpenedEvent>(ref proxy);
    }

    private static HitWindowClosedEvent MakeHitWindowClosedEvent(
        Entity target, int montageId, byte windowId)
    {
        var proxy = new HitWindowProxy
        {
            Target = target,
            MontageId = montageId,
            WindowId = windowId,
        };
        return Unsafe.As<HitWindowProxy, HitWindowClosedEvent>(ref proxy);
    }

    // Mutable proxies matching the layout of readonly event structs.
    private struct MontageStartedProxy { public Entity Target; public int MontageId; public uint ActionInstanceId; public byte QueueIndex; }
    private struct MontageSectionAdvancedProxy { public Entity Target; public int MontageId; public byte FromSectionIndex; public byte ToSectionIndex; }
    private struct HitWindowProxy { public Entity Target; public int MontageId; public byte WindowId; }
}
