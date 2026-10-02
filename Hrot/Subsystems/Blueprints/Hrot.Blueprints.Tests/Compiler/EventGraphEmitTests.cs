using System.Linq;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Q#14 3b-wiring: an Instance Event graph is emitted with its handler keyed in EventHandlers by the event
/// IDENTITY (EventEntryNode.EventTypeId → IrGraph.EventTypeFqn), and the thunk marshals the dispatched payload
/// by reinterpreting it as that struct (exercises the CSharpEmitter/InstanceEmitter paths the proof suite
/// doesn't reach — no real blueprint has an Event graph).
/// </summary>
public sealed class EventGraphEmitTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: System.Array.Empty<BlueprintSignature>());

    [Fact]
    public void EventGraph_KeysHandlerByEventFqn_AndThunkMarshalsPayload()
    {
        const string fqn = "Test.Events.PingEvent";

        var asset = BlueprintAssetBuilder
            .Instance("EvtSub")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithEventGraph("OnPing", g => g
                .WithInput("Value", "System.Int32")
                .Entry(fqn)     // EventEntry carries the event identity (FQN)
                .Return())
            .Build();

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Event-graph compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        var src = result.GeneratedSource!;

        // Keyed by the event FQN — NOT the graph name "OnPing".
        Assert.Contains($"[\"{fqn}\"] =", src);
        Assert.Contains("Event_OnPing_Thunk", src);
        // Thunk reinterprets the payload as the event struct and passes its field.
        Assert.Contains($"global::{fqn}", src);
        Assert.Contains("__ev.Value", src);
    }

    /// <summary>
    /// ⭐ CE-2010 → CE-2013 (<c>DESIGN_Typed_Event_Nodes</c> E1 → E2, I1/T-2) — two TYPED event nodes in one Event graph
    /// compile to two handlers, each with its own thunk and its own handler-table key. ⛔ Before E1: the second node
    /// compiled to nothing, silently; E1 made it <c>BP1682</c>; E2 lifts it.
    /// </summary>
    [Fact]
    public void TwoTypedEventNodesInOneEventGraph_CompileToTwoHandlers()
    {
        var asset = BlueprintAssetBuilder
            .Instance("TwoEventNodes")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithEventGraph("OnPing", g => g.Entry("Test.Events.PingEvent").Return())
            .Build();
        var graph = asset.Graphs.Single(g => g.Name == "OnPing");
        var second = new EventEntryNode { Id = System.Guid.NewGuid(), EventTypeId = "Test.Events.PongEvent" };
        second.Pins.Add(new Pin { Id = System.Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() });
        graph.Nodes.Add(second);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var src = result.GeneratedSource!;
        Assert.Contains("[\"Test.Events.PingEvent\"] = ", src);
        Assert.Contains("[\"Test.Events.PongEvent\"] = ", src);
        Assert.Contains("Event_OnPing_Thunk", src);
        Assert.Contains("Event_OnPing_1_Thunk", src);
    }

    /// <summary>
    /// ⭐ CE-2013 (T-7) — a custom-event body takes ONE entry: its untyped entry beside another event node is still
    /// <c>BP1682</c>, on the extra node.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1682")]
    public void ACustomEventEntryBesideAnotherEventNode_IsBP1682()
    {
        const GraphKind kind = GraphKind.Event;
        const string firstEventType = "";
        var asset = BlueprintAssetBuilder
            .Instance("OneEntryRule")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var graph = new Graph { Id = System.Guid.NewGuid(), Name = "Body", Kind = kind };
        graph.Nodes.Add(new EventEntryNode { Id = System.Guid.NewGuid(), EventTypeId = firstEventType });
        var second = new EventEntryNode { Id = System.Guid.NewGuid(), EventTypeId = "Test.Events.PongEvent" };
        graph.Nodes.Add(second);
        asset.Graphs.Add(graph);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.False(result.Succeeded);
        var d = Assert.Single(result.Diagnostics, x => x.Code == DiagnosticCodes.BP1682);
        Assert.Equal(second.Id, d.NodeId);
    }

    /// <summary>
    /// ⭐ CE-2013 (T-3) — a node run by one event that reads ANOTHER event node's pin is <c>BP1683</c>: that payload does
    /// not exist when this event fires. Pong's handler writes Ping's <c>Value</c>.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1683")]
    public void AHandlerReadingAnotherEventsPin_IsBP1683()
    {
        var asset = BlueprintAssetBuilder.Instance("CrossRead")
            .WithVariable("Got", typeof(int), "0")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var t = new Hrot.Blueprints.Tests.Runtime.TypedEventGraph();
        var ping = t.Event("Test.Events.PingEvent", "Value");
        var pong = t.Event("Test.Events.PongEvent", "Value");
        var set = t.Set(asset.Variables[0]);
        t.Then(pong, set).Data(ping, "Value", set, "Value");
        asset.Graphs.Add(t.Graph);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.False(result.Succeeded);
        var d = Assert.Single(result.Diagnostics, x => x.Code == DiagnosticCodes.BP1683);
        Assert.Equal(set.Id, d.NodeId);
    }

    /// <summary>
    /// ⭐⭐ CE-2012 (T-1) — the load-time migration: a bus-event graph that keeps its payload on the GRAPH is read back
    /// with it on the event NODE, and the generated code is byte-identical to compiling the legacy shape directly.
    /// <para>✅ Red-proof: drop <c>EventPayload.MigrateLegacyInputs</c> from the load and <c>Fields</c> stays null.</para>
    /// </summary>
    [Fact]
    public void ALegacyEventGraph_MovesItsInputsOntoTheNodeOnLoad_AndGeneratesTheSameCode()
    {
        const string fqn = "Test.Events.PingEvent";
        BlueprintAsset Legacy() => BlueprintAssetBuilder
            .Instance("LegacyPing")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithEventGraph("OnPing", g => g.WithInput("Value", "System.Int32").Entry(fqn).Return())
            .Build();
        var legacy = Legacy();
        var loaded = Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(
            Hrot.Blueprints.Core.BlueprintJsonServices.Serialize(legacy))!;

        var graph = loaded.Graphs.Single(g => g.Name == "OnPing");
        var entry = graph.Nodes.OfType<EventEntryNode>().Single();
        Assert.Empty(graph.Inputs);
        var field = Assert.Single(entry.Fields!);
        Assert.Equal(("Value", "System.Int32"), (field.Name, field.TypeId));

        var before = new BlueprintCompiler().Compile(legacy, DefaultOptions());
        var after  = new BlueprintCompiler().Compile(loaded, DefaultOptions());
        Assert.True(before.Succeeded && after.Succeeded);
        Assert.Equal(before.GeneratedSource, after.GeneratedSource);
    }

    /// <summary>⭐ CE-2012 — a custom-event body keeps its payload on the graph (it is paired with its declaration, BP1408).</summary>
    [Fact]
    public void ACustomEventBody_KeepsItsInputsOnTheGraphOnLoad()
    {
        var asset = BlueprintAssetBuilder.Instance("CustomBody")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithEventGraph("OnHit", g => g.WithInput("Damage", "System.Single").Entry("Test.Events.HitEvent").Return())
            .Build();
        asset.CustomEvents.Add(new CustomEventDecl
        {
            Id = System.Guid.NewGuid(), Name = "OnHit",
            Parameters = { new ParameterDecl { Id = System.Guid.NewGuid(), Name = "Damage", Type = new BlueprintTypeRef { TypeId = "System.Single" } } },
        });
        Assert.False(EventPayload.MigrateLegacyInputs(asset));
        Assert.Single(asset.Graphs.Single(g => g.Name == "OnHit").Inputs);
    }
}
