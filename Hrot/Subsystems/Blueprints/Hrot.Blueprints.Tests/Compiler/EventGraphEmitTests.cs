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
    /// ⭐ CE-2010 (<c>DESIGN_Typed_Event_Nodes</c> E1, I1) — a SECOND event node in one Event graph is a diagnostic on
    /// that node. ⛔ Before: <c>FindEntryNode</c> took the first one and the second compiled to nothing, with no word.
    /// </summary>
    [Fact]
    public void ASecondEventNodeInOneEventGraph_IsADiagnostic()
    {
        var asset = BlueprintAssetBuilder
            .Instance("TwoEventNodes")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithEventGraph("OnPing", g => g.Entry("Test.Events.PingEvent").Return())
            .Build();
        var second = new EventEntryNode { Id = System.Guid.NewGuid(), EventTypeId = "Test.Events.PongEvent" };
        asset.Graphs.Single(g => g.Name == "OnPing").Nodes.Add(second);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.False(result.Succeeded);
        var d = Assert.Single(result.Diagnostics, x => x.Code == DiagnosticCodes.BP1682);
        Assert.Equal(second.Id, d.NodeId);
    }
}
