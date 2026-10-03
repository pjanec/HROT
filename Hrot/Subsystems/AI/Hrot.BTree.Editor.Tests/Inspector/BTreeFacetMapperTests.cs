using System;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// AIE-023 behavioral tests for <see cref="BTreeFacetMapper"/> and
/// <see cref="InspectorWindow"/> facet dispatch.
/// All tests are headless — no ImGui context.
/// </summary>
public sealed class BTreeFacetMapperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

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

    private static BehaviorTreeAsset MakeAsset(BehaviorTreeBlob blob) =>
        BehaviorTreeAssetProjector.Project(
            blob, null, null,
            Guid.NewGuid(), blob.TreeName, "/test.cs", false,
            string.Empty, string.Empty);

    /// <summary>
    /// ⭐⭐ <b><c>S2</c> (<c>BP-399</c>) — the facet now resolves through
    /// <c>NodePropertiesSource</c>, not through <c>InspectorWindow</c>.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.6 ②: the node arms were EXTRACTED to
    /// <c>details.nodeproperties</c>. ⚠ The claim each test below makes is UNCHANGED — it was always
    /// about the MAPPER; the window was only a convenient driver.
    /// <para>⭐ And the port is closer to production: the context is built by
    /// <c>DetailsContextBuilder</c>, the same call the shell makes every frame.</para>
    /// </summary>
    private static Hrot.Editor.AiShared.Shell.NodePropertiesSource MakeFacetSource(
        IFacetDispatcher? dispatcher = null)
    {
        var source = new Hrot.Editor.AiShared.Shell.NodePropertiesSource();
        source.SetFacetDispatcher(dispatcher);
        return source;
    }

    private static Hrot.Editor.AiShared.Shell.DetailsContext ContextOf(EditorSelectionStore store)
        => Hrot.Editor.AiShared.Shell.DetailsContextBuilder.Build(
               store, "BTree", Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    // ── BTreeFacetMapper tests ────────────────────────────────────────────────

    [Fact]
    public void Inspector_BTreeNodeSelection_YieldsActionFacet()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        // Find the first Action node.
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var sel = new BTreeNodeSelection(actionNode.VisualId);
        store.ActiveSubSelection = sel;

        // The mapper must return a BTreeActionFacet.
        var facet = mapper.GetFacet(sel);

        facet.Should().NotBeNull();
        facet.Should().BeOfType<BTreeActionFacet>();
        var af = (BTreeActionFacet)facet!;
        af.Action.MethodFqn.Should().Be("Ns.C.Action1");
    }

    [Fact]
    public void Inspector_BTreeNodeSelection_YieldsSequenceFacet()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);
        var seqNode = asset.Nodes.First(n => n.KernelType == NodeType.Sequence);
        var sel = new BTreeNodeSelection(seqNode.VisualId);

        var facet = mapper.GetFacet(sel);

        facet.Should().BeOfType<BTreeSequenceFacet>();
        var sf = (BTreeSequenceFacet)facet!;
        sf.ChildCount.Should().Be(2);
    }

    [Fact]
    public void Inspector_BTreeNodeSelection_YieldsRootFacet()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);
        var rootNode = asset.Nodes.First(n => n.KernelType == NodeType.Root);
        var sel = new BTreeNodeSelection(rootNode.VisualId);

        var facet = mapper.GetFacet(sel);

        facet.Should().BeOfType<BTreeRootFacet>();
    }

    [Fact]
    public void Inspector_Commit_AppliesToAsset_AndMarksDirty()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var sel = new BTreeNodeSelection(actionNode.VisualId);
        store.ActiveSubSelection = sel;

        // Get the current facet and modify it.
        var facet = (BTreeActionFacet)mapper.GetFacet(sel)!;
        facet.Comment = "edited comment";

        // Apply: mapper should write back to the asset.
        mapper.ApplyFacet(sel, facet);

        // Asset should now be dirty.
        asset.IsDirty.Should().BeTrue("apply must mark dirty");
        // The change must be reflected in the asset node.
        actionNode.Comment.Should().Be("edited comment");
    }

    [Fact]
    public void Inspector_NoSubSelection_FallsBackToAssetProperties()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;
        // No sub-selection set.

        var source = MakeFacetSource(mapper);

        // FacetFor should return null when no sub-selection.
        source.FacetFor(ContextOf(store)).Should().BeNull(
            "no sub-selection means no facet returned");
    }

    [Fact]
    public void Inspector_UnknownSubSelection_ReturnsNull()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);

        // Use an HSM sub-selection — should not be handled by BTree mapper.
        var hsmSel = new HsmStateSelection(Guid.NewGuid());
        var facet  = mapper.GetFacet(hsmSel);

        facet.Should().BeNull("BTree mapper ignores non-BTree sub-selections");
    }

    [Fact]
    public void Inspector_FacetForContext_MatchesDirectMapperOutput()
    {
        var asset  = MakeAsset(RootSequence2Actions());
        var mapper = new BTreeFacetMapper(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var sel = new BTreeNodeSelection(actionNode.VisualId);
        store.ActiveSubSelection = sel;

        var source = MakeFacetSource(mapper);

        // NodePropertiesSource.FacetFor() must return the same type as a direct mapper call.
        var sourceFacet = source.FacetFor(ContextOf(store));
        var directFacet = mapper.GetFacet(sel);

        sourceFacet.Should().NotBeNull();
        sourceFacet!.GetType().Should().Be(directFacet!.GetType());
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

    // ── ⭐⭐ CE-439 — the subtree pick LANDS and binds the child's params (Q76 §12.28) ──────────────

    public struct PatrolInputs { public float Speed; public int Laps; }

    private sealed class ChildTree : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "PatrolTree";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.BTree;
        public string SourceFilePath => "/patrol.btree.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        // ⭐ S8b-2: what the ONE child-inputs lookup answers for this child (the test stands in for the registry).
        public string? InputsTypeId { get; init; }
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class OneAssetCatalog : Hrot.Editor.AiShared.Catalog.IAssetCatalog
    {
        private readonly Hrot.Editor.AiShared.IEditableAsset _a;
        public OneAssetCatalog(Hrot.Editor.AiShared.IEditableAsset a) => _a = a;
        public System.Collections.Generic.IReadOnlyList<Hrot.Editor.AiShared.IEditableAsset> All => new[] { _a };
        public Hrot.Editor.AiShared.IEditableAsset? FindByAssetId(Guid id) => _a.AssetId == id ? _a : null;
        public Hrot.Editor.AiShared.IEditableAsset? FindByName(string n) => _a.Name == n ? _a : null;
        public System.Collections.Generic.IReadOnlyList<Hrot.Editor.AiShared.IEditableAsset> WhereDependsOn(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.IEditableAsset>();
#pragma warning disable 67
        public event Action<Hrot.Editor.AiShared.AssetKind>? Changed;
#pragma warning restore 67
    }

    private static BehaviorTreeBlob RootHostingOneSubtree() =>
        new BehaviorTreeBlob
        {
            TreeName = "Host",
            Nodes = new[]
            {
                new NodeDefinition { Type = NodeType.Root,    ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Subtree, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
            },
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = new[] { "" },
        };

    /// <summary>
    /// 🔴 RED before: <c>ApplyFacet</c>'s <c>BTreeSubtreeFacet</c> arm wrote only <c>Comment</c>/<c>IsBreakpoint</c> ⇒ the
    /// picked name was DISCARDED. ⭐ Now the pick lands (name + Guid captured at pick time) and a child with Inputs brings a
    /// <c>Role=Input</c> host variable typed as its Inputs struct, bound as the site's <c>ParamsVariable</c>.
    /// </summary>
    [Fact]
    public void CE439_PickingASubtree_LandsAndBindsTheChildsParams()
    {
        var child  = new ChildTree { InputsTypeId = typeof(PatrolInputs).FullName };
        var asset  = MakeAsset(RootHostingOneSubtree());
        var mapper = new BTreeFacetMapper(asset, new OneAssetCatalog(child),
                                          childInputsTypeOf: name => name == child.Name ? child.InputsTypeId : null);
        var node   = asset.Nodes.Single(n => n.KernelType == NodeType.Subtree);
        var sel    = new BTreeNodeSelection(node.VisualId);

        var facet = (BTreeSubtreeFacet)mapper.GetFacet(sel)!;
        facet.SubtreeName = "PatrolTree";
        mapper.ApplyFacet(sel, facet);

        node.Subtree!.SubtreeName.Should().Be("PatrolTree");
        node.Subtree.SubtreeAssetId.Should().Be(child.AssetId);
        node.Subtree.ParamsVariable.Should().Be("PatrolTreeParams");
        asset.BlackboardVariables.Single(v => v.Name == "PatrolTreeParams").FieldType.Should().Be(typeof(PatrolInputs));
    }

    /// <summary>⭐ A child with no Inputs: the pick lands, nothing is bound.</summary>
    [Fact]
    public void CE439_PickingASubtreeWithNoInputs_LandsAndBindsNothing()
    {
        var asset  = MakeAsset(RootHostingOneSubtree());
        var mapper = new BTreeFacetMapper(asset, new OneAssetCatalog(new ChildTree()), childInputsTypeOf: _ => null);
        var node   = asset.Nodes.Single(n => n.KernelType == NodeType.Subtree);
        var sel    = new BTreeNodeSelection(node.VisualId);

        var facet = (BTreeSubtreeFacet)mapper.GetFacet(sel)!;
        facet.SubtreeName = "PatrolTree";
        mapper.ApplyFacet(sel, facet);

        node.Subtree!.SubtreeName.Should().Be("PatrolTree");
        node.Subtree.ParamsVariable.Should().BeNull();
    }
}
