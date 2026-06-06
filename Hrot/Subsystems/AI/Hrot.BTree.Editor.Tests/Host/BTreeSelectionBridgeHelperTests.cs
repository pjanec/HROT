using System;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// FIX-A headless tests for <see cref="BTreeSelectionBridgeHelper.MapSelection"/>.
/// Tests the pure static mapping: (SelectionState, BehaviorTreeAsset?) → BTreeNodeSelection?
/// and the full chain dispatcher + sub-selection → GetCurrentFacet() != null.
/// No ImGui context required.
/// </summary>
public sealed class BTreeSelectionBridgeHelperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    // Root → Sequence → [Action, Action] blob (mirrors existing BTreeFacetMapperTests helper).
    private static BehaviorTreeBlob RootSequence2Actions() =>
        new BehaviorTreeBlob
        {
            TreeName = "S2A",
            Nodes = new[]
            {
                new NodeDefinition { Type = NodeType.Root,     ChildCount = 1, SubtreeOffset = 4 },
                new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 1 },
            },
            MethodNames     = new[] { "Ns.C.Action1", "Ns.C.Action2" },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        BehaviorTreeAssetProjector.Project(
            RootSequence2Actions(), null, null,
            Guid.NewGuid(), "TestTree", "/test.cs", false,
            string.Empty, string.Empty);

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

        BTreeSelectionBridgeHelper.MapSelection(sel, btreeAsset: null).Should().BeNull();
    }

    [Fact]
    public void MapSelection_EmptySelection_ReturnsNull()
    {
        var asset = MakeAsset();
        BTreeSelectionBridgeHelper.MapSelection(new SelectionState(), asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_MultipleNodesSelected_ReturnsNull()
    {
        var asset = MakeAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),
        });

        BTreeSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_LinkSelected_ReturnsNull()
    {
        var asset = MakeAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(Guid.NewGuid())));

        BTreeSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    // ── MapSelection happy path ────────────────────────────────────────────────

    /// <summary>
    /// FIX-A core: single node selected → BTreeNodeSelection with the correct VisualId.
    /// Confirms canvas NodeId.Value == BTreeEditorNode.VisualId contract.
    /// </summary>
    [Fact]
    public void MapSelection_SingleNodeSelected_ReturnsBTreeNodeSelection_WithCorrectVisualId()
    {
        var asset      = MakeAsset();
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var sel        = new SelectionState();
        // Canvas NodeId.Value == node.VisualId per BTreeNodeModel.Id contract.
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(actionNode.VisualId)));

        var result = BTreeSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result!.VisualId.Should().Be(actionNode.VisualId,
            "canvas NodeId.Value must equal BTreeEditorNode.VisualId");
    }

    [Fact]
    public void MapSelection_RootNodeSelected_ReturnsBTreeNodeSelection()
    {
        var asset    = MakeAsset();
        var rootNode = asset.Nodes.First(n => n.KernelType == NodeType.Root);
        var sel      = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(rootNode.VisualId)));

        var result = BTreeSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result!.VisualId.Should().Be(rootNode.VisualId);
    }

    // ── GetCurrentFacet integration ───────────────────────────────────────────

    /// <summary>
    /// FIX-A end-to-end headless: confirms the full chain
    ///   SetFacetDispatcher(BuildFacetDispatcher(asset)) +
    ///   ActiveSubSelection = new BTreeNodeSelection(visualId)
    ///   → inspector.GetCurrentFacet() != null.
    /// This is the exact condition the lead's symptom report describes as broken.
    /// Uses Action node (Sequence tree): Action facet requires only a MethodName, no FloatParams.
    /// </summary>
    [Fact]
    public void GetCurrentFacet_ReturnsNonNull_WhenDispatcherAndSubSelectionAreWired()
    {
        var asset      = MakeAsset();
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);

        var store      = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var dispatcher = BTreeSelectionBridgeHelper.BuildFacetDispatcher(asset);
        dispatcher.Should().NotBeNull();

        var inspector  = MakeInspectorWindow(store, dispatcher);

        // Simulate what AfterDraw publishes: canvas node click → BTreeNodeSelection.
        store.ActiveSubSelection = new BTreeNodeSelection(actionNode.VisualId);

        var facet = inspector.GetCurrentFacet();

        facet.Should().NotBeNull(
            "dispatcher + ActiveSubSelection → facet must be non-null");
    }

    // ── BuildFacetDispatcher null guard ───────────────────────────────────────

    [Fact]
    public void BuildFacetDispatcher_NullAsset_ReturnsNull()
    {
        BTreeSelectionBridgeHelper.BuildFacetDispatcher(null).Should().BeNull();
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
