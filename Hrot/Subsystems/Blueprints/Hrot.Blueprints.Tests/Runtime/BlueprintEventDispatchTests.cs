using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>Q#14 slice 3b: per-slot dispatch invokes an event handler with the payload for a present event.</summary>
public sealed class BlueprintEventDispatchTests
{
    [EventId(990001234)]
    public struct TestDispatchEvent { public int Value; }

    private static BlueprintDefinition DefWithHandler(EventHandlerDelegate handler) => new()
    {
        Name = "Sub", Kind = BlueprintDispatchKind.Instance, StructureHash = 1, StateSize = 8,
        EventHandlers = new Dictionary<string, EventHandlerDelegate>(StringComparer.Ordinal)
        {
            [typeof(TestDispatchEvent).FullName!] = handler,
        },
    };

    [Fact]
    public void DispatchForSlot_InvokesHandlerWithPayload_ForPresentEvent()
    {
        using var repo = new EntityRepository();
        repo.Bus.Publish(new TestDispatchEvent { Value = 42 });
        repo.Bus.SwapBuffers(); // last frame's event is now readable

        int received = -1;
        EventHandlerDelegate handler = (Span<byte> state, ISimulationView v, IEntityCommandBuffer e,
            Entity self, float t, float dt, ReadOnlySpan<byte> payload) =>
        {
            received = MemoryMarshal.Read<TestDispatchEvent>(payload).Value;
        };

        Span<byte> state = stackalloc byte[8];
        BlueprintEventDispatch.DispatchForSlot(
            DefWithHandler(handler), state, repo.Bus,
            view: null!, ecb: null!, self: default, time: 1f, deltaTime: 0.016f);

        Assert.Equal(42, received);
    }

    [Fact]
    public void DispatchForSlot_SkipsHandler_WhenEventAbsent()
    {
        using var repo = new EntityRepository();
        repo.Bus.SwapBuffers(); // nothing published this frame

        bool ran = false;
        EventHandlerDelegate handler = (s, v, e, self, t, dt, p) => ran = true;

        Span<byte> state = stackalloc byte[8];
        BlueprintEventDispatch.DispatchForSlot(
            DefWithHandler(handler), state, repo.Bus, null!, null!, default, 1f, 0.016f);

        Assert.False(ran);
    }

    [Fact]
    public void ResolveTypeId_MatchesBusTypedId_ForEventIdStruct()
    {
        Assert.Equal(
            EventType<TestDispatchEvent>.Id,
            BlueprintEventDispatch.ResolveTypeId(typeof(TestDispatchEvent).FullName!));
    }
}
