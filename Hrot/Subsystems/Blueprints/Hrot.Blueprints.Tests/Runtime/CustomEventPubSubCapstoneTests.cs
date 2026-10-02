using System;
using System.Collections.Generic;
using System.Linq;
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
    /// ⭐ CE-2011 (<c>DESIGN_Typed_Event_Nodes</c> E1, I4/T-4) — two Event graphs on ONE event type in one Instance BOTH
    /// run. ⛔ Before: the handler table was an indexer initialiser keyed by the event FQN, so the second handler silently
    /// replaced the first and <c>First</c> stayed 0.
    /// </summary>
    [Fact]
    public void TwoEventGraphsOnOneEventType_InOneInstance_BothRun()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string eventFqn = typeof(PingDemoEvent).FullName!;
        var asset = BlueprintAssetBuilder
            .Instance("TwoHandlersOneEvent")
            .WithVariable("First", typeof(int), "0")
            .WithVariable("Second", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.Graphs.Add(BuildOnPingGraph(eventFqn, asset.Variables[0].Id, name: "OnPingFirst"));
        asset.Graphs.Add(BuildOnPingGraph(eventFqn, asset.Variables[1].Id, name: "OnPingSecond"));

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity subscriber = harness.SpawnAndAttach(asset);

        fixture.World.Bus.Publish(new PingDemoEvent { Target = subscriber, Value = 42 });
        harness.Pump(1);

        Assert.Equal(42, harness.ReadIntField(subscriber, asset, "First"));
        Assert.Equal(42, harness.ReadIntField(subscriber, asset, "Second"));
    }

    /// <summary>
    /// ⭐⭐ CE-2013 (<c>DESIGN_Typed_Event_Nodes</c> E2, T-2) — ONE Event graph handles TWO events: each typed event node
    /// fires its own chain with its own payload. Ping writes <c>First</c>; Pong writes <c>Second</c>.
    /// <para>✅ Red-proof: before the split the second node was dropped (E1: BP1682), so Pong never wrote.</para>
    /// </summary>
    [Fact]
    public void OneEventGraph_TwoEventNodes_EachRunsOnItsOwnEvent()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BlueprintAssetBuilder.Instance("OneGraphTwoEvents")
            .WithVariable("First", typeof(int), "0")
            .WithVariable("Second", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var t = new TypedEventGraph();
        var ping = t.Event(typeof(PingDemoEvent).FullName!, "Value");
        var pong = t.Event(typeof(PongDemoEvent).FullName!, "Value");
        var setFirst = t.Set(asset.Variables[0]);
        var setSecond = t.Set(asset.Variables[1]);
        t.Then(ping, setFirst).Data(ping, "Value", setFirst, "Value");
        t.Then(pong, setSecond).Data(pong, "Value", setSecond, "Value");
        asset.Graphs.Add(t.Graph);

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity e = harness.SpawnAndAttach(asset);

        fixture.World.Bus.Publish(new PingDemoEvent { Target = e, Value = 42 });
        harness.Pump(1);
        Assert.Equal(42, harness.ReadIntField(e, asset, "First"));
        Assert.Equal(0, harness.ReadIntField(e, asset, "Second"));

        fixture.World.Bus.Publish(new PongDemoEvent { Target = e, Value = 7 });
        harness.Pump(1);
        Assert.Equal(42, harness.ReadIntField(e, asset, "First"));
        Assert.Equal(7, harness.ReadIntField(e, asset, "Second"));
    }

    /// <summary>
    /// ⭐⭐ CE-2013 (T-3) — two event nodes feed ONE exec chain: each event runs the shared tail (<c>Count = Count + 1</c>)
    /// after its own head, so both arriving in one frame count twice. The tail is cloned for the second handler.
    /// <para>✅ Red-proof: give the second handler the tail without cloning it (same node ids) and the generated class
    /// does not compile — or with E1 alone, the second event never runs and <c>Count</c> is 1.</para>
    /// </summary>
    [Fact]
    public void TwoEventNodesIntoOneExecChain_BothRunTheSharedTail()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BlueprintAssetBuilder.Instance("SharedTail")
            .WithVariable("First", typeof(int), "0")
            .WithVariable("Second", typeof(int), "0")
            .WithVariable("Count", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var t = new TypedEventGraph();
        var ping = t.Event(typeof(PingDemoEvent).FullName!, "Value");
        var pong = t.Event(typeof(PongDemoEvent).FullName!, "Value");
        var setFirst = t.Set(asset.Variables[0]);
        var setSecond = t.Set(asset.Variables[1]);
        var tail = t.Increment(asset.Variables[2]);
        t.Then(ping, setFirst).Data(ping, "Value", setFirst, "Value").Then(setFirst, tail);
        t.Then(pong, setSecond).Data(pong, "Value", setSecond, "Value").Then(setSecond, tail);
        asset.Graphs.Add(t.Graph);

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity e = harness.SpawnAndAttach(asset);

        fixture.World.Bus.Publish(new PingDemoEvent { Target = e, Value = 42 });
        fixture.World.Bus.Publish(new PongDemoEvent { Target = e, Value = 7 });
        harness.Pump(1);

        Assert.Equal(42, harness.ReadIntField(e, asset, "First"));
        Assert.Equal(7, harness.ReadIntField(e, asset, "Second"));
        Assert.Equal(2, harness.ReadIntField(e, asset, "Count"));
    }

    /// <summary>
    /// ⭐⭐ CE-2014 (<c>DESIGN_Typed_Event_Nodes</c> E3, T-6) — the event node's WHOLE-EVENT pin carries the event struct;
    /// Break Struct splits it. Ping's <c>Event</c> → Break Struct → <c>Value</c> → <c>First</c>, and Pong's handler (no
    /// whole-event wire) still reads its field pin — both in one graph.
    /// <para>✅ Red-proof: pass <c>__ev.__event</c> instead of <c>__ev</c> for the whole-event input and the generated class
    /// does not compile; drop the pin's input and it reads the wrong argument.</para>
    /// </summary>
    [Fact]
    public void TheWholeEventPin_CarriesTheEventStruct_AndBreakStructSplitsIt()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BlueprintAssetBuilder.Instance("WholeEventPin")
            .WithVariable("First", typeof(int), "0")
            .WithVariable("Second", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        string pingFqn = typeof(PingDemoEvent).FullName!;
        var t = new TypedEventGraph();
        var ping = t.Event(pingFqn, "Value");
        var pong = t.Event(typeof(PongDemoEvent).FullName!, "Value");
        var brk = t.BreakStruct(pingFqn, "Value");
        var setFirst = t.Set(asset.Variables[0]);
        var setSecond = t.Set(asset.Variables[1]);
        t.Then(ping, setFirst).Data(ping, "Event", brk, "Value").Data(brk, "Value", setFirst, "Value");
        t.Then(pong, setSecond).Data(pong, "Value", setSecond, "Value");
        asset.Graphs.Add(t.Graph);

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity e = harness.SpawnAndAttach(asset);

        fixture.World.Bus.Publish(new PingDemoEvent { Target = e, Value = 42 });
        fixture.World.Bus.Publish(new PongDemoEvent { Target = e, Value = 7 });
        harness.Pump(1);

        Assert.Equal(42, harness.ReadIntField(e, asset, "First"));
        Assert.Equal(7, harness.ReadIntField(e, asset, "Second"));
    }

    /// <summary>
    /// ⭐⭐ CE-2016 (<c>DESIGN_Typed_Event_Nodes</c> E5, Q14-A2) — a SYSTEM event subscribed from the palette: the
    /// "On: HitEvent" node (fields reflected from the engine type, no hand-baked list) runs its handler when a real
    /// <c>HitEvent</c> is published — twice published, counted twice.
    /// <para>✅ Red-proof: before E5 the palette had no "On: HitEvent" (system events reached the When node only).</para>
    /// </summary>
    [Fact]
    public void ASystemEvent_FromThePalette_RunsItsHandler()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        _ = typeof(Fdp.Toolkit.Combat.Contracts.HitEvent);   // loaded, so discovery can reflect it
        var asset = BlueprintAssetBuilder.Instance("OnHitFromPalette")
            .WithVariable("Hits", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var t = new TypedEventGraph();
        var onHit = t.Adopt((EventEntryNode)Hrot.Blueprints.Editor.NodeDrawers.BlueprintEventPaletteEntries
            .SubscribeEntries().Single(d => d.DisplayName == "On: HitEvent").CreateInstance());
        t.Then(onHit, t.Increment(asset.Variables[0]));
        asset.Graphs.Add(t.Graph);

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity e = harness.SpawnAndAttach(asset);

        fixture.World.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.HitEvent { HitEntity = e, HitT = 0.5f });
        fixture.World.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.HitEvent { HitEntity = e, HitT = 0.7f });
        harness.Pump(1);

        Assert.Equal(2, harness.ReadIntField(e, asset, "Hits"));
    }

    /// <summary>
    /// ⭐⭐⭐ CE-2017 (<c>DESIGN_Typed_Event_Nodes</c> E6, T-3) — a node in a SHARED tail probes with its AUTHORED id from
    /// every handler: Ping's run and Pong's run both report the tail's own id (never the clone's), the debug map's
    /// entries for it name the authored graph, and it is a breakpoint target — so one breakpoint there pauses whichever
    /// handler runs it, and the canvas lights the node the designer drew.
    /// <para>✅ Red-proof: skip the Stage 5 debug-identity rewrite and Pong's run reports the clone's id.</para>
    /// </summary>
    [Fact]
    public void ASharedTailNode_ProbesWithItsAuthoredId_FromEveryHandler()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BlueprintAssetBuilder.Instance("SharedTailProbes")
            .WithVariable("Count", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var t = new TypedEventGraph();
        var ping = t.Event(typeof(PingDemoEvent).FullName!);
        var pong = t.Event(typeof(PongDemoEvent).FullName!);
        var tail = t.Increment(asset.Variables[0]);
        t.Then(ping, tail).Then(pong, tail);
        asset.Graphs.Add(t.Graph);
        Guid cloneOfTail = Hrot.Blueprints.Core.Compiler.DeterministicIds.FromString($"event-handler:{pong.Id:N}:{tail.Id:N}");

        var compiled = new Hrot.Blueprints.Core.Compiler.BlueprintCompiler().Compile(asset, new Hrot.Blueprints.Core.Compiler.CompileOptions(
            Mode: Hrot.Blueprints.Core.Compiler.CompilerMode.Debug,
            NodeRegistry: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInNodeRegistry.Instance,
            TypeRegistry: Hrot.Blueprints.Core.Compiler.Catalogs.StaticTypeRegistry.Instance,
            EngineEvents: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInEngineEventCatalog.Instance,
            ChannelCommands: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: System.Array.Empty<Hrot.Blueprints.Core.Compiler.BlueprintSignature>()));
        Assert.True(compiled.Succeeded);
        var map = compiled.DebugMap!;
        Assert.True(map.BreakpointTargets.ContainsKey(tail.Id));
        Assert.DoesNotContain(map.BreakpointTargets.Keys, k => k == cloneOfTail);
        var tailEntries = map.Entries.Where(en => en.NodeId == tail.Id).ToList();
        Assert.True(tailEntries.Count >= 2, "one debug-map entry per handler");
        // ⚠ Some entries carry an empty GraphId (pre-existing: DebugOf leaves it default at many sites, every graph);
        //   none may name the derived handler graph, and the named ones are the authored graph.
        Guid handlerGraph = Hrot.Blueprints.Core.Compiler.DeterministicIds.FromString($"event-handler-graph:{t.Graph.Id:N}:{pong.Id:N}");
        Assert.DoesNotContain(map.Entries, en => en.GraphId == handlerGraph);
        Assert.Contains(tailEntries, en => en.GraphId == t.Graph.Id);
        Assert.DoesNotContain(cloneOfTail.ToString("D"), compiled.GeneratedSource!);

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        Entity e = harness.SpawnAndAttach(asset);
        fixture.World.Bus.Publish(new PongDemoEvent { Target = e, Value = 1 });
        harness.Pump(1);

        var ids = fixture.DebugSession.GetRecentNodeHistory(500).Select(h => h.NodeIdString).ToList();
        Assert.Contains(tail.Id.ToString("D"), ids);
        Assert.DoesNotContain(cloneOfTail.ToString("D"), ids);
        Assert.Equal(1, harness.ReadIntField(e, asset, "Count"));
    }

    /// <summary>
    /// Builds the <c>OnPing</c> Event graph with explicit pins/links:
    /// <c>EventEntry.Out(exec) → SetVariable.In</c>, <c>EventEntry.Value(data) → SetVariable.Value(data)</c>,
    /// <c>SetVariable.Out(exec) → Return.In</c>. <c>Graph.Inputs=[Value:int]</c> so Stage5 matches the
    /// <c>EventEntry</c> "Value" data-out to payload arg 0.
    /// </summary>
    internal static Graph BuildOnPingGraph(string eventFqn, Guid lastValueId, bool selfFilter = false, string name = "OnPing")
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
            Name   = name,
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
