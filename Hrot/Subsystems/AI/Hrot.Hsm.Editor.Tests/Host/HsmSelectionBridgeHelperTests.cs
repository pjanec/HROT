using System;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// FIX-A headless tests for <see cref="HsmSelectionBridgeHelper.MapSelection"/>.
/// Tests the pure static mapping: (SelectionState, HsmAsset?) → HsmStateSelection?
/// and the full chain dispatcher + sub-selection → GetCurrentFacet() != null.
/// No ImGui context required.
/// </summary>
public sealed class HsmSelectionBridgeHelperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    private static HsmAsset MakeSimpleAsset()
    {
        var b = new HsmBuilder("Simple");
        b.Event("Fire", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Fire").GoTo("Active");
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Simple", "", false, "");
    }

    private static InspectorWindow MakeInspectorWindow(
        EditorSelectionStore store, IFacetDispatcher? dispatcher = null)
    {
        var refactor    = new StubRefactor();
        var findResults = new FindResultsWindow();
        return new InspectorWindow(store, refactor, findResults,
            facetDispatcher: dispatcher);
    }

    // ── MapSelection null / empty guards ──────────────────────────────────────

    [Fact]
    public void MapSelection_NullAsset_ReturnsNull()
    {
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        HsmSelectionBridgeHelper.MapSelection(sel, hsmAsset: null).Should().BeNull();
    }

    [Fact]
    public void MapSelection_EmptySelection_ReturnsNull()
    {
        var asset = MakeSimpleAsset();
        HsmSelectionBridgeHelper.MapSelection(new SelectionState(), asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_MultipleNodesSelected_ReturnsNull()
    {
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),
        });

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_UnknownLinkId_ReturnsNull()
    {
        // A canvas LinkId not present in the asset → returns null (stale-id guard).
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(Guid.NewGuid())));

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_MultipleLinksSelected_ReturnsNull()
    {
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfLink(new LinkId(Guid.NewGuid())),
            SelectionEntry.OfLink(new LinkId(Guid.NewGuid())),
        });

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_UnknownNodeId_ReturnsNull()
    {
        // A canvas NodeId not present in the asset → returns null (stale-id guard).
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    // ── MapSelection happy path ────────────────────────────────────────────────

    /// <summary>
    /// FIX-A core: canvas NodeId.Value == StateNode.StableId (HsmGraphModel contract).
    /// Selecting a state node returns HsmStateSelection with the correct StableId.
    /// </summary>
    [Fact]
    public void MapSelection_StateNodeSelected_ReturnsHsmStateSelection_WithCorrectStableId()
    {
        var asset     = MakeSimpleAsset();
        var idleState = asset.AllStates.First(s => s.Name == "Idle");

        var sel = new SelectionState();
        // Canvas NodeId.Value == StateNode.StableId per HsmGraphModel / StateNode.Id contract.
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(idleState.StableId)));

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result.Should().BeOfType<HsmStateSelection>();
        ((HsmStateSelection)result!).StableId.Should().Be(idleState.StableId,
            "canvas NodeId.Value must equal StateNode.StableId");
    }

    [Fact]
    public void MapSelection_AnotherState_ReturnsCorrectStableId()
    {
        var asset       = MakeSimpleAsset();
        var activeState = asset.AllStates.First(s => s.Name == "Active");

        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(activeState.StableId)));

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result.Should().BeOfType<HsmStateSelection>();
        ((HsmStateSelection)result!).StableId.Should().Be(activeState.StableId);
    }

    // ── MapSelection transition link happy path (HSM-TRANS) ───────────────────

    /// <summary>
    /// HSM-TRANS core: canvas LinkId.Value == TransitionNode.VisualId (HsmGraphModel contract).
    /// Selecting a transition link returns HsmTransitionSelection with the correct VisualId.
    /// </summary>
    [Fact]
    public void MapSelection_TransitionLinkSelected_ReturnsHsmTransitionSelection_WithCorrectVisualId()
    {
        var asset      = MakeSimpleAsset();
        // MakeSimpleAsset has Idle -Fire-> Active; that transition is the only one.
        var transition = asset.AllTransitions.First();

        var sel = new SelectionState();
        // Canvas LinkId.Value == TransitionNode.VisualId (HsmGraphModel / HsmTransitionLink contract).
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(transition.VisualId)));

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result.Should().BeOfType<HsmTransitionSelection>(
            "a single selected canvas link maps to HsmTransitionSelection");
        ((HsmTransitionSelection)result!).VisualId.Should().Be(transition.VisualId,
            "canvas LinkId.Value must equal TransitionNode.VisualId");
    }

    [Fact]
    public void MapSelection_MixedNodeAndLink_PrefersStateNode()
    {
        // When both a node and a link are selected, the state node is preferred.
        var asset      = MakeSimpleAsset();
        var idleState  = asset.AllStates.First(s => s.Name == "Idle");
        var transition = asset.AllTransitions.First();

        var sel = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(idleState.StableId)),
            SelectionEntry.OfLink(new LinkId(transition.VisualId)),
        });

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().BeOfType<HsmStateSelection>(
            "when both a node and a link are selected, the state node is preferred");
        ((HsmStateSelection)result!).StableId.Should().Be(idleState.StableId);
    }

    // ── GetCurrentFacet integration ───────────────────────────────────────────

    /// <summary>
    /// FIX-A end-to-end headless: confirms the full chain
    ///   SetFacetDispatcher(BuildFacetDispatcher(asset)) +
    ///   ActiveSubSelection = new HsmStateSelection(stableId)
    ///   → inspector.GetCurrentFacet() != null.
    /// This is the exact condition the lead's symptom report describes as broken.
    /// </summary>
    [Fact]
    public void GetCurrentFacet_ReturnsNonNull_WhenDispatcherAndSubSelectionAreWired()
    {
        var asset     = MakeSimpleAsset();
        var idleState = asset.AllStates.First(s => s.Name == "Idle");

        var store = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset);
        dispatcher.Should().NotBeNull();

        var inspector = MakeInspectorWindow(store, dispatcher);

        // Simulate what AfterDraw publishes: map canvas node click → HsmStateSelection.
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(idleState.StableId)));
        var subSel = HsmSelectionBridgeHelper.MapSelection(sel, asset);
        store.ActiveSubSelection = subSel;

        var facet = inspector.GetCurrentFacet();

        facet.Should().NotBeNull(
            "dispatcher + ActiveSubSelection → GetCurrentFacet must return the state facet");
        facet.Should().BeOfType<StateFacet>(
            "clicking an HSM state must yield a StateFacet");
        var sf = (StateFacet)facet!;
        sf.Name.Should().Be("Idle");
    }

    /// <summary>
    /// HSM-TRANS end-to-end headless: confirms the full chain
    ///   SetFacetDispatcher(BuildFacetDispatcher(asset)) +
    ///   ActiveSubSelection = new HsmTransitionSelection(visualId)
    ///   → inspector.GetCurrentFacet() returns TransitionFacet.
    /// </summary>
    [Fact]
    public void GetCurrentFacet_ReturnsTransitionFacet_WhenTransitionSubSelectionIsWired()
    {
        var asset      = MakeSimpleAsset();
        var transition = asset.AllTransitions.First();

        var store = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset);
        dispatcher.Should().NotBeNull();

        var inspector = MakeInspectorWindow(store, dispatcher);

        // Simulate what AfterDraw publishes: map canvas link click → HsmTransitionSelection.
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(transition.VisualId)));
        var subSel = HsmSelectionBridgeHelper.MapSelection(sel, asset);
        store.ActiveSubSelection = subSel;

        var facet = inspector.GetCurrentFacet();

        facet.Should().NotBeNull(
            "dispatcher + ActiveSubSelection → GetCurrentFacet must return the transition facet");
        facet.Should().BeOfType<TransitionFacet>(
            "clicking an HSM transition must yield a TransitionFacet");
        var tf = (TransitionFacet)facet!;
        tf.SourceStateName.Should().Be("Idle");
        tf.TargetStateName.Should().Be("Active");
    }

    // ── BuildFacetDispatcher null guard ───────────────────────────────────────

    [Fact]
    public void BuildFacetDispatcher_NullAsset_ReturnsNull()
    {
        HsmSelectionBridgeHelper.BuildFacetDispatcher(null).Should().BeNull();
    }

    // ── stub helpers ──────────────────────────────────────────────────────────

    private sealed class StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default) =>
            Task.FromResult(PreviewRename(f, t, o));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default) =>
            Task.FromResult(ApplyRename(p));
    }
}
