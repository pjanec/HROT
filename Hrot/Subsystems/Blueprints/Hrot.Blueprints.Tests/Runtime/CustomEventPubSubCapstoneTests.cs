using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>Local blittable custom event for the capstone (mirrors the AI.Behaviors <c>PingEvent</c> demo
/// carrier, but self-contained so the test needs no game-assembly reference). Distinct <c>[EventId]</c> to
/// avoid any registry collision with the shipped demo event. Declared TOP-LEVEL (not nested) so its
/// <c>Type.FullName</c> is dotted — a nested type's <c>+</c>-separated FullName bakes an uncompilable FQN.</summary>
[EventId(7402)]
public struct PingDemoEvent
{
    public Entity Target;
    public int Value;
}

/// <summary>
/// Q#14 CAPSTONE — the full custom-event publish→dispatch→subscribe loop through the REAL runtime.
///
/// <para>
/// A blittable <c>[EventId]</c> event is published onto the live bus; an <b>Instance</b> subscriber
/// blueprint — <b>compiled by the real compiler</b> into a generated <c>_Bp</c> class with an
/// <c>EventHandlers</c> entry keyed by the event FQN — is attached to an entity and pumped through the
/// production <see cref="Fdp.Toolkit.Blueprints.Systems.BlueprintTickSystem"/> (the same system the editor
/// ticks live). Its <c>OnPing</c> Event graph reads the payload's <c>Value</c> off the <c>EventEntry</c>
/// and mirrors it into a WorkingState field. Asserting <c>LastValue == 42</c> proves every link:
/// </para>
/// <list type="number">
///   <item>the event routes by type-id — publisher's <c>EventType&lt;PingDemoEvent&gt;.Id</c> equals the
///     dispatch pump's FQN→<c>[EventId].Id</c> resolution;</item>
///   <item><c>BlueprintTickSystem</c> invokes the per-slot dispatch for the subscriber;</item>
///   <item>the generated thunk reinterprets the raw payload bytes as the event struct and passes
///     <c>__ev.Value</c> into the handler;</item>
///   <item>the handler reads that payload arg (<c>IrOp_ReadInputArg</c> off the Event-graph
///     <c>EventEntry</c> data-out — the pins the Stage0 enrichment fix now emits for Event graphs) and
///     writes it into the instance's WorkingState.</item>
/// </list>
///
/// <para>
/// The Event graph is hand-built with explicit pins so the wiring is fully deterministic (the fluent
/// <see cref="GraphBuilder"/> auto-adds exec pins, which suppresses Stage0 enrichment and offers no
/// inter-node data-link API). Everything else — the Instance shell, WorkingState, and the empty Tick
/// graph — comes from <see cref="BlueprintAssetBuilder"/>.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class CustomEventPubSubCapstoneTests
{
    [Fact]
    public void PublishedCustomEvent_DispatchedToInstanceSubscriber_MirrorsPayloadIntoWorkingState()
    {
        // Compiled-and-run blueprints JIT-pin their collectible ALC, so the strict unload check is
        // disabled here exactly as the sibling runtime suites do (WhenNode/Utility/SpawnEqsSensor).
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string eventFqn = typeof(PingDemoEvent).FullName!;

        // Instance shell + a Variable (Instance per-entity state lives in Variables, not WorkingState —
        // BP1031) + an (empty) Tick graph via the builder.
        var asset = BlueprintAssetBuilder
            .Instance("CustomEventSubscriberCapstone")
            .WithVariable("LastValue", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        // The SetVariable node targets the Variable by its (builder-assigned) id.
        Guid lastValueId = asset.Variables[0].Id;

        // Hand-append the OnPing Event graph: EventEntry(fqn).Value → SetVariable(LastValue).
        asset.Graphs.Add(BuildOnPingGraph(eventFqn, lastValueId));

        // Compile for real (generates EventHandlers keyed by the FQN) + attach to an entity.
        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity subscriber = harness.SpawnAndAttach(asset);

        Assert.Equal(0, harness.ReadIntField(subscriber, asset, "LastValue"));

        // Publish, then pump one frame: TickFrame swaps the bus (making the event readable) and runs
        // BlueprintTickSystem, whose per-slot dispatch fires the OnPing handler.
        fixture.World.Bus.Publish(new PingDemoEvent { Target = subscriber, Value = 42 });
        harness.Pump(1);

        Assert.Equal(42, harness.ReadIntField(subscriber, asset, "LastValue"));
    }

    /// <summary>
    /// Q#14 (3d) Self filter: a targeted event fires ONLY the subscriber whose entity matches the event's
    /// [EventTarget] field. Two entities run the same Self-filtered subscriber; publishing PingDemoEvent
    /// targeted at A updates A's LastValue and leaves B's at its default.
    /// </summary>
    [Fact]
    public void SelfFilteredSubscriber_OnlyTargetedEntityHandlesTheEvent()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string eventFqn = typeof(PingDemoEvent).FullName!;
        var asset = BlueprintAssetBuilder
            .Instance("SelfFilteredSubscriber")
            .WithVariable("LastValue", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.Graphs.Add(BuildOnPingGraph(eventFqn, asset.Variables[0].Id, selfFilter: true));

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity a = harness.SpawnAndAttach(asset);
        Entity b = harness.SpawnAndAttach(asset);

        // Target A only.
        fixture.World.Bus.Publish(new PingDemoEvent { Target = a, Value = 42 });
        harness.Pump(1);

        Assert.Equal(42, harness.ReadIntField(a, asset, "LastValue")); // A matched → handled
        Assert.Equal(0,  harness.ReadIntField(b, asset, "LastValue")); // B not targeted → skipped
    }

    /// <summary>
    /// Builds the <c>OnPing</c> Event graph with explicit pins/links:
    /// <c>EventEntry.Out(exec) → SetVariable.In</c>, <c>EventEntry.Value(data) → SetVariable.Value(data)</c>,
    /// <c>SetVariable.Out(exec) → Return.In</c>. <c>Graph.Inputs=[Value:int]</c> so Stage5 matches the
    /// <c>EventEntry</c> "Value" data-out to payload arg 0.
    /// </summary>
    private static Graph BuildOnPingGraph(string eventFqn, Guid lastValueId, bool selfFilter = false)
    {
        var intType = new BlueprintTypeRef { TypeId = "System.Int32" };

        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",   Direction = "Out", IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var entryValOut  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = intType };
        var entry = new EventEntryNode
        {
            Id = Guid.NewGuid(),
            EventTypeId = eventFqn,
            // Q#14 (3d): Self filter compares the event's [EventTarget] field ("Target") against self.
            TargetFilterSelf = selfFilter,
            TargetFieldName  = selfFilter ? "Target" : null,
        };
        entry.Pins.Add(entryExecOut);
        entry.Pins.Add(entryValOut);

        var svExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",    Direction = "In",  IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var svExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",   Direction = "Out", IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var svValIn   = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In",  IsExec = false, TypeRef = intType };
        var svValOut  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = intType };
        var setVar = new SetVariableNode { Id = Guid.NewGuid(), VariableId = lastValueId.ToString() };
        setVar.Pins.Add(svExecIn);
        setVar.Pins.Add(svExecOut);
        setVar.Pins.Add(svValIn);
        setVar.Pins.Add(svValOut);

        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retExecIn);

        var links = new List<Link>
        {
            new Link { FromNodeId = entry.Id,  FromPinId = entryExecOut.Id, ToNodeId = setVar.Id, ToPinId = svExecIn.Id },
            new Link { FromNodeId = entry.Id,  FromPinId = entryValOut.Id,  ToNodeId = setVar.Id, ToPinId = svValIn.Id  },
            new Link { FromNodeId = setVar.Id, FromPinId = svExecOut.Id,    ToNodeId = ret.Id,    ToPinId = retExecIn.Id },
        };

        return new Graph
        {
            Id     = Guid.NewGuid(),
            Name   = "OnPing",
            Kind   = GraphKind.Event,
            Nodes  = new List<Node> { entry, setVar, ret },
            Links  = links,
            Inputs = new List<ParameterDecl>
            {
                new ParameterDecl { Id = Guid.NewGuid(), Name = "Value", Type = intType },
            },
            Outputs = new List<ParameterDecl>(),
        };
    }
}
