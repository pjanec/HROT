using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-125 — <see cref="GraphSignatureWindow"/> edits only called
/// <c>DirtyTracker.MarkDirty</c>; they never reached <see cref="IEditService.NotifyStructureChanged"/>,
/// so <c>BlueprintDocumentFactory</c> never ran <c>graphModel.RebuildAndNotify()</c> and a declared
/// output never became a pin on the Return node. The edits were also not undoable (BP-102).
///
/// <para>
/// <see cref="ReturnNodeDrawer"/> (the Details-panel path over the same <c>Graph.Outputs</c> /
/// <c>Graph.Inputs</c> state) always routed through <c>IEditService.RecordPropertyEdit</c> +
/// <c>NotifyStructureChanged</c> — these tests lock the fix that makes
/// <see cref="GraphSignatureWindow"/> do the same, and assert the two writers are now
/// indistinguishable to an observer.
/// </para>
/// </summary>
public sealed class BP125_SignatureEditsReprojectTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static BlueprintAsset MakeAsset(params Graph[] graphs)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        foreach (var g in graphs) asset.Graphs.Add(g);
        return asset;
    }

    private static Graph MakeFunctionGraph(string name = "Func1")
        => new() { Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Function };

    private static (GraphSignatureWindow window, EditorSelectionStore store) MakeWindow(
        IEditService? editService = null)
    {
        var store  = new EditorSelectionStore();
        var dirty  = new DirtyTracker();
        var window = editService == null
            ? new GraphSignatureWindow(store, dirty)
            : new GraphSignatureWindow(store, dirty, editServiceAccessor: () => editService);
        return (window, store);
    }

    /// <summary>
    /// Records every (description, apply, undo) triple, mirroring <c>ReturnNodeDrawerTests.SpyEditService</c>
    /// exactly: recording performs the edit immediately (the documented contract of
    /// <see cref="IEditService.RecordPropertyEdit"/>), so a captured Undo lets a headless test reverse
    /// it later.
    /// </summary>
    private sealed class SpyEditService : IEditService
    {
        public int MarkDirtyCallCount { get; private set; }

        public void MarkDirty(BlueprintAsset asset) => MarkDirtyCallCount++;

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

    // ── 1. The defect itself — Outputs ────────────────────────────────────────

    [Fact]
    public void OutputsEdit_WithEditService_NotifiesStructureChanged()
    {
        var spy = new SpyEditService();
        var (window, store) = MakeWindow(spy);
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        store.SelectAsset(asset);

        var (_, outputsModel) = window.ResolveEditModels()!.Value;
        outputsModel.AddParameter("Result", "System.Single");

        Assert.Single(graph.Outputs);
        Assert.Equal(1, spy.StructureChangedCallCount);
    }

    // ── 2. Same for Inputs — both tables were broken ─────────────────────────

    [Fact]
    public void InputsEdit_WithEditService_NotifiesStructureChanged()
    {
        var spy = new SpyEditService();
        var (window, store) = MakeWindow(spy);
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        store.SelectAsset(asset);

        var (inputsModel, _) = window.ResolveEditModels()!.Value;
        inputsModel.AddParameter("x", "System.Int32");

        Assert.Single(graph.Inputs);
        Assert.Equal(1, spy.StructureChangedCallCount);
    }

    // ── 3. Undoability (BP-102) ───────────────────────────────────────────────

    [Fact]
    public void OutputsEdit_ArrivesAsUndoableRecordPropertyEdit_AndUndoRestoresPriorListAndNotifies()
    {
        var spy = new SpyEditService();
        var (window, store) = MakeWindow(spy);
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        graph.Outputs.Add(new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Existing", Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        });
        var priorSnapshot = graph.Outputs.Select(p => (p.Id, p.Name, p.Type.TypeId)).ToList();
        store.SelectAsset(asset);

        var (_, outputsModel) = window.ResolveEditModels()!.Value;
        outputsModel.AddParameter("New", "System.Int32");

        Assert.Equal(1, spy.Recorded.Count);
        var (_, apply, undo) = spy.Recorded[0];
        Assert.NotNull(apply);
        Assert.NotNull(undo);
        Assert.Equal(2, graph.Outputs.Count);
        Assert.Equal(1, spy.StructureChangedCallCount);

        undo();

        Assert.Equal(priorSnapshot.Count, graph.Outputs.Count);
        Assert.Equal(priorSnapshot[0].Name, graph.Outputs[0].Name);
        Assert.Equal(priorSnapshot[0].Id,   graph.Outputs[0].Id);
        Assert.Equal(2, spy.StructureChangedCallCount); // fires on the undo path too
    }

    [Fact]
    public void InputsEdit_ArrivesAsUndoableRecordPropertyEdit_AndUndoRestoresPriorListAndNotifies()
    {
        var spy = new SpyEditService();
        var (window, store) = MakeWindow(spy);
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        graph.Inputs.Add(new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Existing", Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        });
        var priorSnapshot = graph.Inputs.Select(p => (p.Id, p.Name, p.Type.TypeId)).ToList();
        store.SelectAsset(asset);

        var (inputsModel, _) = window.ResolveEditModels()!.Value;
        inputsModel.AddParameter("New", "System.Int32");

        Assert.Equal(1, spy.Recorded.Count);
        Assert.Equal(2, graph.Inputs.Count);
        Assert.Equal(1, spy.StructureChangedCallCount);

        spy.Recorded[0].Undo();

        Assert.Equal(priorSnapshot.Count, graph.Inputs.Count);
        Assert.Equal(priorSnapshot[0].Name, graph.Inputs[0].Name);
        Assert.Equal(priorSnapshot[0].Id,   graph.Inputs[0].Id);
        Assert.Equal(2, spy.StructureChangedCallCount);
    }

    // ── 4. One gesture, one entry ─────────────────────────────────────────────

    [Fact]
    public void SingleAddParameter_ProducesExactlyOneRecordPropertyEditCall()
    {
        var spy = new SpyEditService();
        var (window, store) = MakeWindow(spy);
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        store.SelectAsset(asset);

        var (_, outputsModel) = window.ResolveEditModels()!.Value;
        outputsModel.AddParameter("Result", "System.Single");

        Assert.Equal(1, spy.Recorded.Count);
    }

    // ── 5. Parity with the Return-node path (⭐ the most valuable test) ──────

    /// <summary>
    /// The same logical edit — add an output — performed through
    /// <see cref="ReturnNodeDrawer"/>'s outputs model and through
    /// <see cref="GraphSignatureWindow"/>'s must be indistinguishable to a recording
    /// <see cref="IEditService"/>: same <c>RecordPropertyEdit</c> call count, and
    /// <c>NotifyStructureChanged</c> firing on both apply AND undo, for both paths. They edit the
    /// same state (<c>Graph.Outputs</c>) — a second, subtly different writer is exactly what caused
    /// BP-125.
    /// </summary>
    [Fact]
    public void AddOutput_ThroughWindow_And_ThroughReturnNodeDrawer_AreObservablyIndistinguishable()
    {
        // ── path A: GraphSignatureWindow ──────────────────────────────────────
        var spyWindow = new SpyEditService();
        var (window, store) = MakeWindow(spyWindow);
        var graphW = MakeFunctionGraph("ViaWindow");
        var assetW = MakeAsset(graphW);
        store.SelectAsset(assetW);

        var (_, outputsModelW) = window.ResolveEditModels()!.Value;
        outputsModelW.AddParameter("Result", "System.Single");

        // ── path B: ReturnNodeDrawer ───────────────────────────────────────────
        var spyDrawer = new SpyEditService();
        var graphD = MakeFunctionGraph("ViaReturnNode");
        var node   = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        graphD.Nodes.Add(node);
        var assetD = MakeAsset(graphD);

        var drawer  = new ReturnNodeDrawer(spyDrawer);
        var session = (ReturnNodeSession)drawer.CreateSession(node, assetD);

        session.OutputsModelForTest!.AddParameter("Result", "System.Single");

        // ── apply-side parity ──────────────────────────────────────────────────
        Assert.Equal(spyDrawer.Recorded.Count, spyWindow.Recorded.Count);
        Assert.Equal(1, spyWindow.Recorded.Count);
        Assert.Equal(spyDrawer.StructureChangedCallCount, spyWindow.StructureChangedCallCount);
        Assert.Equal(1, spyWindow.StructureChangedCallCount);
        Assert.Single(graphW.Outputs);
        Assert.Single(graphD.Outputs);

        // ── undo-side parity ───────────────────────────────────────────────────
        spyWindow.Recorded[0].Undo();
        spyDrawer.Recorded[0].Undo();

        Assert.Equal(spyDrawer.StructureChangedCallCount, spyWindow.StructureChangedCallCount);
        Assert.Equal(2, spyWindow.StructureChangedCallCount);
        Assert.Empty(graphW.Outputs);
        Assert.Empty(graphD.Outputs);
    }

    // ── 6. Back-compat — editServiceAccessor null (existing 2-arg ctor) ──────

    [Fact]
    public void EditServiceAccessorNull_TwoArgCtor_EditStillAppliesAndDoesNotThrow()
    {
        var (window, store) = MakeWindow(editService: null);
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        store.SelectAsset(asset);

        var (inputsModel, outputsModel) = window.ResolveEditModels()!.Value;

        var ex = Record.Exception(() =>
        {
            inputsModel.AddParameter("x", "System.Int32");
            outputsModel.AddParameter("y", "System.Single");
        });

        Assert.Null(ex);
        Assert.Single(graph.Inputs);
        Assert.Single(graph.Outputs);
    }
}
