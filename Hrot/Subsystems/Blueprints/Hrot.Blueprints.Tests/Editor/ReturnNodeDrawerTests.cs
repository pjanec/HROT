using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Core;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-89 — headless tests for <see cref="ReturnNodeDrawer"/> and <see cref="ReturnNodeSession"/>.
///
/// The user report was "Return node detail panel always shows Success and nothing else" — there
/// was no way to declare a function's outputs anywhere near the Return node. These tests exercise
/// the fix's two load-bearing seams: (1) the session finds the RIGHT containing graph out of
/// several on the asset, and (2) every Outputs mutation is undo-recorded AND notifies structure
/// changed (pin projection on the Return node and every call site), while a plain Status edit is
/// undo-recorded but does NOT notify structure changed.
/// </summary>
public sealed class ReturnNodeDrawerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ReturnNode MakeNode() => new() { Id = Guid.NewGuid(), Status = NodeStatus.Success };

    private static Graph MakeFunctionGraph(string name = "MyFunc")
        => new() { Id = Guid.NewGuid(), Kind = GraphKind.Function, Name = name };

    private static BlueprintAsset MakeAsset(params Graph[] graphs)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        foreach (var g in graphs) asset.Graphs.Add(g);
        return asset;
    }

    // ── RN-01: Handles ────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_Handles_ReturnNode_True()
    {
        var drawer = new ReturnNodeDrawer(new SpyEditService());
        Assert.True(drawer.Handles(new ReturnNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new ReturnNodeDrawer(new SpyEditService());
        Assert.False(drawer.Handles(new BranchNode      { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SequenceNode    { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new EventEntryNode  { Id = Guid.NewGuid() }));
    }

    // ── RN-02: Registration in CreateNodeDrawerRegistry (live registry lookup) ─

    [Fact]
    public void DrawerRegistry_TryGet_ReturnNode_ResolvesReturnNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry();

        Assert.True(registry.TryGet(typeof(ReturnNode), out var drawer));
        Assert.NotNull(drawer);
        Assert.IsType<ReturnNodeDrawer>(drawer);
    }

    [Fact]
    public void DrawerRegistry_GetDrawerFor_ReturnNodeInstance_ResolvesReturnNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry();

        var drawer = registry.GetDrawerFor(new ReturnNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<ReturnNodeDrawer>(drawer);
    }

    // ── RN-03: containing-graph resolution across several graphs ─────────────

    [Fact]
    public void Session_ResolvesContainingGraph_WhenNodeIsInSecondGraph()
    {
        var graphA = MakeFunctionGraph("GraphA");
        var graphB = MakeFunctionGraph("GraphB");
        var node   = MakeNode();
        graphB.Nodes.Add(node);
        var asset = MakeAsset(graphA, graphB);

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.Same(graphB, session.ResolvedGraphForTest);
    }

    [Fact]
    public void Session_ContainingGraphNotFound_ResolvesToNull()
    {
        var graphA = MakeFunctionGraph("GraphA");
        var node   = MakeNode(); // never added to any graph's Nodes list
        var asset  = MakeAsset(graphA);

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.Null(session.ResolvedGraphForTest);
        Assert.Null(session.OutputsModelForTest);
    }

    // ── RN-04: adding an output touches only the containing graph ───────────

    [Fact]
    public void AddOutput_AppendsToContainingGraphOnly_NotOtherGraphs()
    {
        var graphA = MakeFunctionGraph("GraphA");
        var graphB = MakeFunctionGraph("GraphB");
        var node   = MakeNode();
        graphB.Nodes.Add(node);
        var asset = MakeAsset(graphA, graphB);

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("result", "System.Single");

        Assert.Single(graphB.Outputs);
        Assert.Equal("result",        graphB.Outputs[0].Name);
        Assert.Equal("System.Single", graphB.Outputs[0].Type.TypeId);
        Assert.Empty(graphA.Outputs);
    }

    // ── RN-05: undo round-trips (Add / Remove / Rename / Retype) ─────────────

    [Fact]
    public void AddOutput_UndoRoundTrip_RestoresExactPriorOutputs()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);
        graph.Outputs.Add(new ParameterDecl { Id = Guid.NewGuid(), Name = "Existing", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } });
        var priorSnapshot = graph.Outputs.Select(p => (p.Id, p.Name, p.Type.TypeId)).ToList();

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("New", "System.Int32");
        Assert.Equal(2, graph.Outputs.Count);
        Assert.Equal(1, spy.Recorded.Count);

        spy.Recorded[0].Undo();

        Assert.Equal(priorSnapshot.Count, graph.Outputs.Count);
        Assert.Collection(graph.Outputs, p =>
        {
            Assert.Equal(priorSnapshot[0].Id,     p.Id);
            Assert.Equal(priorSnapshot[0].Name,   p.Name);
            Assert.Equal(priorSnapshot[0].TypeId, p.Type.TypeId);
        });
    }

    [Fact]
    public void RemoveOutput_UndoRoundTrip_RestoresExactPriorOutputs()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("A", "System.Int32");
        session.OutputsModelForTest!.AddParameter("B", "System.Single");
        var priorSnapshot = graph.Outputs.Select(p => (p.Id, p.Name, p.Type.TypeId)).ToList();
        spy.Recorded.Clear();

        session.OutputsModelForTest!.RemoveParameter("A");
        Assert.Single(graph.Outputs);
        Assert.Equal(1, spy.Recorded.Count);

        spy.Recorded[0].Undo();

        Assert.Equal(priorSnapshot.Count, graph.Outputs.Count);
        Assert.Equal(priorSnapshot.Select(p => p.Name), graph.Outputs.Select(p => p.Name));
        Assert.Equal(priorSnapshot.Select(p => p.Id),   graph.Outputs.Select(p => p.Id));
    }

    [Fact]
    public void RenameOutput_UndoRoundTrip_RestoresExactPriorName()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("Old", "System.Int32");
        var originalId = graph.Outputs[0].Id;
        spy.Recorded.Clear();

        session.OutputsModelForTest!.RenameParameter("Old", "New");
        Assert.Equal("New", graph.Outputs[0].Name);
        Assert.Equal(1, spy.Recorded.Count);

        spy.Recorded[0].Undo();

        Assert.Equal("Old", graph.Outputs[0].Name);
        Assert.Equal(originalId, graph.Outputs[0].Id);
    }

    [Fact]
    public void RetypeOutput_UndoRoundTrip_RestoresExactPriorTypeId()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("Result", "System.Int32");
        spy.Recorded.Clear();

        session.OutputsModelForTest!.RetypeParameter("Result", "System.Single");
        Assert.Equal("System.Single", graph.Outputs[0].Type.TypeId);
        Assert.Equal(1, spy.Recorded.Count);

        spy.Recorded[0].Undo();

        Assert.Equal("System.Int32", graph.Outputs[0].Type.TypeId);
    }

    /// <summary>
    /// An undo entry survives being replayed. Undo publishes the captured "before" list back into
    /// the graph; if it published the snapshot's own <c>ParameterDecl</c> instances rather than
    /// copies, the later in-place rename below would rewrite this entry's captured state, and
    /// undoing it a second time would restore the *newer* name instead of the original one.
    /// Reachable from the editor as undo → redo → edit → undo → undo.
    /// </summary>
    [Fact]
    public void OutputUndoEntry_ReplayedAfterALaterInPlaceRename_StillRestoresTheOriginalName()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("Original", "System.Int32");
        spy.Recorded.Clear();

        // The entry under test: remove the output, then undo it (the "before" list goes live).
        session.OutputsModelForTest!.RemoveParameter("Original");
        var removeEntry = spy.Recorded[0];
        removeEntry.Undo();
        Assert.Equal("Original", graph.Outputs[0].Name);

        // A later edit mutates the restored decl IN PLACE — this is what can corrupt a snapshot
        // that was handed over by reference rather than copied.
        spy.Recorded.Clear();
        session.OutputsModelForTest!.RenameParameter("Original", "Renamed");
        Assert.Equal("Renamed", graph.Outputs[0].Name);
        spy.Recorded[0].Undo();
        Assert.Equal("Original", graph.Outputs[0].Name);

        // Replaying the earlier entry must still restore what it captured, not the newer name.
        removeEntry.Undo();
        Assert.Equal("Original", Assert.Single(graph.Outputs).Name);
    }

    // ── RN-06: BP-86 regression guard through the new reuse path ────────────

    [Fact]
    public void RenameOutput_ToShorterName_StoresExactStringWithNoInteriorNul()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("Param0", "System.Single");

        // Simulate exactly what ParameterRowsView hands to ImGuiBufferText.Decode: a fixed-size
        // buffer seeded with the old name, over-typed with a SHORTER new name (stale bytes remain
        // past the new terminator — the BP-86 corruption pattern).
        var buf = System.Text.Encoding.UTF8.GetBytes("Param0" + "\0");
        Array.Resize(ref buf, 256);
        var typed = System.Text.Encoding.UTF8.GetBytes("R1");
        typed.CopyTo(buf, 0);
        buf[typed.Length] = 0;

        var decoded = Fdp.Presentation.Utils.ImGuiBufferText.Decode(buf);
        session.OutputsModelForTest!.RenameParameter("Param0", decoded);

        Assert.Equal("R1", graph.Outputs[0].Name);
        Assert.DoesNotContain('\0', graph.Outputs[0].Name);
    }

    // ── RN-07: structure-changed notification ────────────────────────────────

    [Fact]
    public void AddOutput_NotifiesStructureChanged()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("R", "System.Int32");

        Assert.Equal(1, spy.StructureChangedCallCount);
    }

    [Fact]
    public void AddOutput_Undo_AlsoNotifiesStructureChanged()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.OutputsModelForTest!.AddParameter("R", "System.Int32");
        Assert.Equal(1, spy.StructureChangedCallCount);

        spy.Recorded[0].Undo();

        Assert.Equal(2, spy.StructureChangedCallCount);
    }

    [Fact]
    public void StatusChange_DoesNotNotifyStructureChanged()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.SetStatusForTest(NodeStatus.Failure);

        Assert.Equal(0, spy.StructureChangedCallCount);
    }

    // ── RN-08: Status is a plain undoable value edit ─────────────────────────

    [Fact]
    public void StatusChange_IsRecordedAsOneUndoableEntry_AndUndoRestoresPreviousValue()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode(); // Status = Success
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var spy     = new SpyEditService();
        var drawer  = new ReturnNodeDrawer(spy);
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        session.SetStatusForTest(NodeStatus.Failure);

        Assert.Equal(NodeStatus.Failure, node.Status);
        Assert.Equal(1, spy.Recorded.Count);

        spy.Recorded[0].Undo();

        Assert.Equal(NodeStatus.Success, node.Status);
    }

    [Fact]
    public void StatusChange_SetsDirty()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.False(session.IsDirty);
        session.SetStatusForTest(NodeStatus.Running);
        Assert.True(session.IsDirty);
    }

    // ── RN-09: null-recorder regression guard (Change 2) ─────────────────────

    [Fact]
    public void GraphSignatureEditModel_WithNullRecorder_BehavesExactlyAsBeforeBP89()
    {
        var graph = MakeFunctionGraph();
        var spy   = new List<int>();
        var model = new Hrot.Blueprints.Editor.Variables.GraphSignatureEditModel(
            graph, isOutputs: true, onChanged: () => spy.Add(1));

        model.AddParameter("x", "System.Int32");
        Assert.Single(graph.Outputs);
        Assert.Equal(1, spy.Count);

        model.RemoveParameter("nonexistent");
        Assert.Equal(1, spy.Count); // no-op: no additional onChanged fire

        model.RenameParameter("x", "y");
        Assert.Equal("y", graph.Outputs[0].Name);
        Assert.Equal(2, spy.Count);
    }

    [Fact]
    public void GraphSignatureEditModel_NoOpMutation_FiresNeitherOnChangedNorRecord()
    {
        var graph    = MakeFunctionGraph();
        var onChangedCount = 0;
        var recordCount    = 0;
        var model = new Hrot.Blueprints.Editor.Variables.GraphSignatureEditModel(
            graph, isOutputs: true,
            onChanged: () => onChangedCount++,
            record: (_, apply, _) => { recordCount++; apply(); });

        model.RemoveParameter("ghost");

        Assert.Equal(0, onChangedCount);
        Assert.Equal(0, recordCount);
    }

    // ── BP-105: which section(s) apply, per dispatch ─────────────────────────
    //
    // MakeAsset leaves Dispatch at its default (Library == 0) unless overridden below — see
    // BlueprintDispatchKind's declaration order in BlueprintAsset.cs.

    [Fact]
    public void Instance_ShowsOutputs_NotStatus()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);
        asset.Dispatch = BlueprintDispatchKind.Instance;

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.True(session.ShowsOutputsForTest);
        Assert.False(session.ShowsStatusForTest);
    }

    [Fact]
    public void AiPrimitive_ShowsStatus_NotOutputs()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);
        asset.Dispatch = BlueprintDispatchKind.AiPrimitive;

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.False(session.ShowsOutputsForTest);
        Assert.True(session.ShowsStatusForTest);
    }

    [Fact]
    public void Library_ZeroOutputs_ShowsBothOutputsAndStatus()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);
        asset.Dispatch = BlueprintDispatchKind.Library;

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.True(session.ShowsOutputsForTest);
        Assert.True(session.ShowsStatusForTest);
    }

    [Fact]
    public void Library_WithDeclaredOutput_ShowsOutputs_NotStatus()
    {
        var graph = MakeFunctionGraph();
        var node  = MakeNode();
        graph.Nodes.Add(node);
        var asset = MakeAsset(graph);
        asset.Dispatch = BlueprintDispatchKind.Library;

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        // Declaring an output must flip Status off — the same instant the compiler (BP-104)
        // switches from IrTerm_ReturnStatus to IrTerm_Return for this graph.
        session.OutputsModelForTest!.AddParameter("Result", "System.Int32");

        Assert.True(session.ShowsOutputsForTest);
        Assert.False(session.ShowsStatusForTest);
    }

    [Fact]
    public void ContainingGraphNotFound_ShowsNeitherSection()
    {
        var graphA = MakeFunctionGraph("GraphA");
        var node   = MakeNode(); // never added to any graph's Nodes list
        var asset  = MakeAsset(graphA);
        asset.Dispatch = BlueprintDispatchKind.Instance;

        var drawer  = new ReturnNodeDrawer(new SpyEditService());
        var session = (ReturnNodeSession)drawer.CreateSession(node, asset);

        Assert.False(session.ShowsOutputsForTest);
        Assert.False(session.ShowsStatusForTest);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintNodeDrawerRegistry CreateTestDrawerRegistry()
    {
        var channelCatalog    = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog      = BuiltInEngineEventCatalog.Instance;
        var editService       = new SpyEditService();
        var predicateCompiler = new TestPredicateCompiler();
        var eqsTemplates      = new EqsTemplateRegistry();

        return BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);
    }

    // ── Test stubs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Records every (description, apply, undo) triple, mirroring the real EditService: recording
    /// performs the edit immediately (matching IEditService.RecordPropertyEdit's documented
    /// contract), so a captured Undo lets a headless test reverse it exactly.
    /// </summary>
    private sealed class SpyEditService : IEditService
    {
        public int MarkDirtyCallCount { get; private set; }
        public BlueprintAsset? LastMarkedAsset { get; private set; }

        public void MarkDirty(BlueprintAsset asset)
        {
            MarkDirtyCallCount++;
            LastMarkedAsset = asset;
        }

        public List<(string Label, Action Apply, Action Undo)> Recorded { get; } = new();

        public int StructureChangedCallCount { get; private set; }

        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        {
            Recorded.Add((description, apply, undo));
            apply();
            MarkDirty(asset);
        }

        public void NotifyStructureChanged(BlueprintAsset asset) => StructureChangedCallCount++;
    }

    private sealed class TestPredicateCompiler : IPredicateCompiler
    {
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public Func<EntityRepository, Entity, bool> CompileEntityPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto predicate)
            => Array.Empty<Type>();
    }
}
