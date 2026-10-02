using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>Q#14 slice 2c: "Publish: {Event}" palette entries per discovered event, baking the event shape.</summary>
public sealed class BlueprintEventPaletteEntriesTests
{
    [Fact]
    public void PublishEntries_IncludeDiscoveredEvent_AndBakeShapeOnCreate()
    {
        var entry = BlueprintEventPaletteEntries.PublishEntries()
            .FirstOrDefault(d => d.DisplayName == "Publish: Test Ping");   // the 1b TestPingEvent

        Assert.NotNull(entry);
        Assert.StartsWith("Events", entry!.Category);

        var node = Assert.IsType<PublishEventNode>(entry.CreateInstance());
        Assert.EndsWith("TestPingEvent", node.EventTypeFqn!);
        Assert.Equal("Target", node.TargetFieldName);
        Assert.NotNull(node.PayloadFields);
        Assert.Contains(node.PayloadFields!, f => f.Name == "Count"    && f.TypeId == "System.Int32");
        Assert.Contains(node.PayloadFields!, f => f.Name == "Strength" && f.TypeId == "System.Single");
    }

    [Fact]
    public void PublishEntries_IncludeEditorAuthoredDefs()
    {
        var catalog = new BlueprintEventCatalog
        {
            Events = { new BlueprintEventDef { Name = "MyEditorEvent", Category = "Custom" } },
        };
        var names = BlueprintEventPaletteEntries.PublishEntries(catalog).Select(d => d.DisplayName).ToList();
        Assert.Contains("Publish: MyEditorEvent", names);
    }

    // ── CE-2015 (DESIGN_Typed_Event_Nodes E4): the subscribe side ───────────────────────────────────────────────

    private static NodeKindDescriptor OnTestPing()
        => BlueprintEventPaletteEntries.SubscribeEntries().Single(d => d.DisplayName == "On: Test Ping");

    /// <summary>
    /// ⭐⭐ CE-2015 — "On: {Event}" mirrors "Publish: {Event}": a typed event node baked with the event's FQN, payload
    /// Fields and recipient, whose pins come from those Fields (+ the whole-event pin, T-6).
    /// <para>✅ Red-proof: before E4 there was no subscribe entry (only the generic Event Entry).</para>
    /// </summary>
    [Fact]
    public void SubscribeEntries_BakeTheEventShape_AndTheNodeProjectsItsPins()
    {
        var entry = OnTestPing();
        Assert.StartsWith("Events", entry.Category);

        var node = Assert.IsType<EventEntryNode>(entry.CreateInstance());
        Assert.EndsWith("TestPingEvent", node.EventTypeId);
        Assert.Equal("Target", node.TargetFieldName);
        Assert.Contains(node.Fields!, f => f.Name == "Count"    && f.TypeId == "System.Int32");
        Assert.Contains(node.Fields!, f => f.Name == "Strength" && f.TypeId == "System.Single");

        var graph = new Graph { Id = Guid.NewGuid(), Name = "OnEvents", Kind = GraphKind.Event };
        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: graph);
        Assert.Contains(pins, p => p.IsExec && p.Direction == "Out");
        Assert.Contains(pins, p => p.Name == "Count" && p.Direction == "Out");
        Assert.Contains(pins, p => p.Name == "Strength" && p.Direction == "Out");
        Assert.Contains(pins, p => p.Name == "Event" && p.TypeRef.TypeId == "global::" + node.EventTypeId);
    }

    /// <summary>
    /// ⭐⭐ CE-2015 — SEVERAL event nodes on one canvas: two "On: Test Ping" nodes dropped into one Event graph, each
    /// with its own policy, compile to two handlers (T-4) — the editor-made shape, pins projected as the canvas does.
    /// </summary>
    [Fact]
    public void TwoOnNodesDroppedIntoOneEventGraph_CompileToTwoHandlers()
    {
        var asset = BlueprintAssetBuilder.Instance("TwoOnNodes")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var graph = new Graph { Id = Guid.NewGuid(), Name = "OnEvents", Kind = GraphKind.Event };
        var first  = (EventEntryNode)OnTestPing().CreateInstance();
        var second = (EventEntryNode)OnTestPing().CreateInstance();
        second.Policy = EventFiberPolicy.Restart;
        foreach (var n in new[] { first, second })
        {
            graph.Nodes.Add(n);
            n.Pins.AddRange(NodePinSchema.GetCanonicalPins(n, containingGraph: graph));
        }
        asset.Graphs.Add(graph);

        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());

        Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains("Event_OnEvents_Thunk", result.GeneratedSource!);
        Assert.Contains("Event_OnEvents_1_Thunk", result.GeneratedSource!);
        Assert.Contains("EventGroup_OnEvents_Thunk", result.GeneratedSource!);   // one table entry, both handlers
    }

    /// <summary>⭐ CE-2015 (T-7) — an "On:" node dropped into a Function graph subscribes to nothing: <c>BP1682</c>.</summary>
    [Fact]
    [Hrot.Blueprints.Tests.Compiler.CoversDiagnosticCode("BP1682")]
    public void AnOnNodeInAFunctionGraph_IsBP1682()
    {
        var asset = BlueprintAssetBuilder.Instance("OnInFunction")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        var node = (EventEntryNode)OnTestPing().CreateInstance();
        asset.Graphs.Single(g => g.Name == "Tick").Nodes.Add(node);

        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());

        var d = Assert.Single(result.Diagnostics, x => x.Code == "BP1682");
        Assert.Equal(node.Id, d.NodeId);
    }

    private sealed class SpyEditService : IEditService
    {
        public List<Action> Undos { get; } = new();
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        { Undos.Add(undo); apply(); }
        public void NotifyStructureChanged(BlueprintAsset asset) { }
    }

    /// <summary>
    /// ⭐⭐ CE-2015 — the Details drawer edits an event node's OWN policy, capacity and Self filter, undoably; Restart
    /// drops a capacity it cannot hold (BP1681), and capacity is clamped to 1..16 (1 is stored as the default 0).
    /// </summary>
    [Fact]
    public void TheEventNodeDrawer_EditsPolicyCapacityAndSelf_Undoably()
    {
        var svc = new SpyEditService();
        var node = (EventEntryNode)OnTestPing().CreateInstance();
        var asset = BlueprintAssetBuilder.Instance("DrawerAsset").Build();
        var session = (EventEntryNodeSession)new EventEntryNodeDrawer(svc).CreateSession(node, asset);

        session.SetPolicyForTest(EventFiberPolicy.Queue);
        session.SetCapacityForTest(40);
        Assert.Equal((EventFiberPolicy.Queue, EventEntryNode.MaxCapacity), (node.Policy, node.Capacity));

        session.SetPolicyForTest(EventFiberPolicy.Restart);
        Assert.Equal((EventFiberPolicy.Restart, 0), (node.Policy, node.Capacity));
        svc.Undos[^1]();
        Assert.Equal((EventFiberPolicy.Queue, EventEntryNode.MaxCapacity), (node.Policy, node.Capacity));

        session.SetCapacityForTest(1);
        Assert.Equal(0, node.Capacity);

        session.SetSelfFilterForTest(true);
        Assert.True(node.TargetFilterSelf);
        svc.Undos[^1]();
        Assert.False(node.TargetFilterSelf);
    }
}
