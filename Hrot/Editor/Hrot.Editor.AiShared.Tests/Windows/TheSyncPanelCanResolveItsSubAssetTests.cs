using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 92 (<c>92d</c>) — the PARAMETER SYNCHRONIZATION panel can resolve a sub-asset.</b>
///
/// <para>⛔⛔ <b><c>S4</c> (<c>2026-08-22</c>) — RE-POINTED, not weakened.</b> The seam moved from
/// <c>InspectorWindow</c> to <c>Shell/ParameterSyncSource</c> when the arm became
/// <c>details.parametersync</c>; ⭐ every assertion below is unchanged and still asks the CONSTRUCTED
/// object *(<c>R-67</c>)*. ⚠ The original finding stands and is why these exist:</para>
///
/// <para>🔴🔴 <b>The defect, measured.</b> <c>InspectorWindow._subAssetResolver</c> is
/// <c>readonly</c>, constructor-only, with no setter. <c>PerspectiveWorkspaceRegistrar</c> — the ONLY
/// production construction — omitted it, so <c>InspectorWindow:449</c> rendered
/// <i>"Sub-asset resolver not configured."</i> for every asset on every host ⇒ ⛔ <b>no designer could
/// author a sync binding at all</b>, on a panel that is otherwise complete.</para>
///
/// <para>⭐⭐ <b>The silent-default pattern, textbook shape</b> — 📌 what distinguishes it from the
/// harmless majority of optional dependencies is that <b>the caller HELD the value</b>: the registrar
/// takes <c>IAssetCatalog</c> as a required argument and <c>FindByAssetId</c> is the answer.</para>
///
/// <para>⭐⭐⭐ <b>Asserted on the CONSTRUCTED OBJECT</b> — 📌 <c>R-67</c>: <i>"a rail that builds its
/// own composition root cannot see a composition-root defect."</i> ⛔ And not merely that a delegate
/// is non-null: a stubbed <c>_ =&gt; null</c> forward would satisfy that and leave the panel just as
/// empty, so the rail resolves a real asset through a real catalog.</para>
/// </summary>
public sealed class TheSyncPanelCanResolveItsSubAssetTests
{
    // ── Harness ──────────────────────────────────────────────────────────────

    private static PerspectiveWorkspaceRegistrar MakeRegistrar(string perspective, IAssetCatalog catalog) =>
        new(
            perspectiveName: perspective,
            selectionStore:  new EditorSelectionStore(),
            catalog:         catalog,
            refactorService: new StubRefactor(),
            debugRegistry:   new DebugSessionRegistry());

    private static (AssetCatalog Catalog, FakeSubAsset Asset) CatalogWithOneSubAsset()
    {
        var asset   = new FakeSubAsset();
        var catalog = new AssetCatalog();
        catalog.AddContributor(new OneAssetContributor(asset));
        return (catalog, asset);
    }

    // ── The rails ────────────────────────────────────────────────────────────

    /// <summary>⭐ The cheap half: the production registrar wires a resolver at all.</summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    [InlineData("Blueprint")]
    public void EveryPerspectiveGivesTheParameterSyncViewASubAssetResolver(string perspective)
    {
        var (catalog, _) = CatalogWithOneSubAsset();

        Assert.True(MakeRegistrar(perspective, catalog).ParameterSync.HasSubAssetResolver,
            "without a resolver the PARAMETER SYNCHRONIZATION panel renders "
            + "\"Sub-asset resolver not configured.\" and no sync binding can be authored");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail</b> — the resolver actually answers, through the catalog the registrar was
    /// given. 🔴 RED before this batch (no resolver at all), and ⛔ still red against a
    /// <c>_ =&gt; null</c> stub.
    /// </summary>
    [Fact]
    public void TheResolverAnswersWithTheCatalogsAsset()
    {
        var (catalog, asset) = CatalogWithOneSubAsset();

        var resolved = MakeRegistrar("BTree", catalog).ParameterSync.ResolveSubAsset(asset.AssetId);

        Assert.Same(asset, resolved);
    }

    /// <summary>
    /// ⚠ The negative half: an id the catalog does not know resolves to <c>null</c> rather than
    /// throwing — ⛔ a Subtree node pointing at a DELETED asset is a real state, and the panel's own
    /// null branch is what reports it.
    /// </summary>
    [Fact]
    public void AnUnknownAssetIdResolvesToNullWithoutThrowing()
    {
        var (catalog, _) = CatalogWithOneSubAsset();

        Assert.Null(MakeRegistrar("BTree", catalog).ParameterSync.ResolveSubAsset(Guid.NewGuid()));
    }

    /// <summary>
    /// ⚠ An asset that is NOT blackboard-managed resolves to <c>null</c> — the resolver's contract is
    /// <c>IBlackboardManagedAsset</c>, and ⛔ a cast failure must read as "no sub-asset", not throw.
    /// </summary>
    [Fact]
    public void AnAssetThatIsNotBlackboardManagedResolvesToNull()
    {
        var plain   = new PlainAsset();
        var catalog = new AssetCatalog();
        catalog.AddContributor(new OneAssetContributor(plain));

        Assert.Null(MakeRegistrar("BTree", catalog).ParameterSync.ResolveSubAsset(plain.AssetId));
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class OneAssetContributor : IAssetCatalogContributor
    {
        private readonly IEditableAsset _asset;
        public OneAssetContributor(IEditableAsset asset) => _asset = asset;

        public AssetKind Kind => _asset.Kind;
        public IReadOnlyList<IEditableAsset> Enumerate() => new[] { _asset };
        public event Action? ContributorChanged { add { } remove { } }
    }

    private sealed class PlainAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "PlainAsset";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/plain.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeSubAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars = new();

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "PatrolSubTree";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/sub.btree.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }

    private sealed class StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) =>
            new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) =>
            new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, CancellationToken ct = default) =>
            Task.FromResult(PreviewRename(f, t, o));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default) =>
            Task.FromResult(ApplyRename(p));
    }
}
