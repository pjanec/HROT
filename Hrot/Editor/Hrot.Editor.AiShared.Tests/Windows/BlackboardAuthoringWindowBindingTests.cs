using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

// ──────────────────────────────────────────────────────────────────────────────
// AIE-025: BlackboardAuthoringWindow binds to active asset (retarget tests)
//
// The window's DrawClientArea pulls from EditorSelectionStore.ActiveAsset each
// frame (pure pull model).  "Retargeting" = the composition root wires
// AiDocumentManager.ActiveChanged → store.ActiveAsset so the window always sees
// the right schema. These tests verify:
//   1. BlackboardWindow_BindsActiveAssetSchema
//   2. BlackboardWindow_NoAggregator_ShowsExplicitVarsOnly_NoThrow
//   3. BlackboardWindow_RetargetsOnActiveAssetChange
// ──────────────────────────────────────────────────────────────────────────────

file sealed class _StubRefactorForBinding : IRefactorService
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
    public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default) =>
        Task.FromResult(PreviewRename(f, t, o));
    public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default) =>
        Task.FromResult(ApplyRename(p));
}

/// <summary>
/// A minimal <see cref="IBlackboardManagedAsset"/> whose variable list can be
/// configured at test time.
/// </summary>
file sealed class _BbManagedAsset : IEditableAsset, IBlackboardManagedAsset
{
    public Guid AssetId { get; } = Guid.NewGuid();
    public string Name { get; set; } = "TestAsset";
    public AssetKind Kind { get; init; } = AssetKind.BTree;
    public string SourceFilePath => "/test.cs";
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public bool IsBlackboardEditorManaged => true;
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; set; }
        = Array.Empty<BlackboardVariableEntry>();

    public event Action? Changed { add { } remove { } }

    public void AddVariable(BlackboardVariableEntry e)                                        { }
    public void RemoveVariable(string n)                                                      { }
    public void UpdateVariableComment(string n, string? c)                                    { }
    public void MoveVariable(int s, int d)                                                    { }
    public void RenameVariable(string o, string n)                                            { }
    public int CountNodesReferencingVariable(string n) => 0;
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string v) => Array.Empty<BlackboardAliasBinding>();
    public void AddAlias(string v, BlackboardAliasBinding b)                                  { }
    public void RemoveAlias(string v, Guid ra, Guid re)                                       { }
    public void RemoveVariables(IReadOnlyList<string> ns)                                     { }
}

// ──────────────────────────────────────────────────────────────────────────────

public sealed class BlackboardAuthoringWindowBindingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IRefactorService Refactor() => new _StubRefactorForBinding();

    /// <summary>
    /// Simulates the composition-root wiring:
    ///   AiDocumentManager.ActiveChanged → store.ActiveAsset = active doc's asset (or null).
    /// </summary>
    private static (AiDocumentManager mgr, EditorSelectionStore store) MakeWiredPair(AssetKind perspective)
    {
        var store = new EditorSelectionStore();
        var mgr   = new AiDocumentManager(perspectiveSwitchCallback: _ => { });

        mgr.ActiveChanged += () =>
        {
            var active = mgr.Active;
            store.ActiveAsset = (active?.Kind == perspective) ? active.Asset : null;
        };

        return (mgr, store);
    }

    // ── AIE-025 SC1: window's BuildViewModel lists the active BTree/HSM asset's BB vars ──

    [Fact]
    public void BlackboardWindow_BindsActiveAssetSchema()
    {
        // Arrange: a BTree asset with two blackboard variables.
        var asset = new _BbManagedAsset
        {
            Kind = AssetKind.BTree,
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("speed", typeof(float), null),
                new BlackboardVariableEntry("health", typeof(int), null),
            },
        };

        var (mgr, store) = MakeWiredPair(AssetKind.BTree);

        // Act: open the document → ActiveChanged fires → store.ActiveAsset is set.
        mgr.Open(asset);

        // Assert: the view-model built from the store reflects the asset's schema.
        var vm = BlackboardAuthoringWindow.BuildViewModel(store.ActiveAsset);

        Assert.True(vm.HasActiveAsset);
        Assert.True(vm.IsBlackboardEditorManaged);
        Assert.Equal(2, vm.Variables.Count);
        Assert.Equal("speed",  vm.Variables[0].Name);
        Assert.Equal("health", vm.Variables[1].Name);
    }

    // ── AIE-025 SC2: no aggregator → shows explicit vars only, no throw ──────

    [Fact]
    public void BlackboardWindow_NoAggregator_ShowsExplicitVarsOnly_NoThrow()
    {
        // Arrange: asset with one explicit variable; no AggregationResult passed.
        var asset = new _BbManagedAsset
        {
            Kind = AssetKind.Hsm,
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("state", typeof(int), null),
            },
        };

        var (mgr, store) = MakeWiredPair(AssetKind.Hsm);
        mgr.Open(asset);

        // Act: build with null aggregationResult (no aggregator) — must not throw.
        BlackboardWindowViewModel vm = default!;
        var ex = Record.Exception(() =>
            vm = BlackboardAuthoringWindow.BuildViewModel(
                store.ActiveAsset,
                aggregationResult: null));

        // Assert: no throw and only explicit var visible.
        Assert.Null(ex);
        Assert.Single(vm.Variables);
        Assert.Equal("state", vm.Variables[0].Name);
        Assert.Empty(vm.UnboundRequirements);
    }

    // ── AIE-025 SC3: retargets to newly active asset on ActiveChanged ─────────

    [Fact]
    public void BlackboardWindow_RetargetsOnActiveAssetChange()
    {
        // Arrange: two BTree assets with distinct variable sets.
        var asset1 = new _BbManagedAsset
        {
            Kind = AssetKind.BTree,
            BlackboardVariables = new[] { new BlackboardVariableEntry("x", typeof(float), null) },
        };
        var asset2 = new _BbManagedAsset
        {
            Kind = AssetKind.BTree,
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("a", typeof(int), null),
                new BlackboardVariableEntry("b", typeof(bool), null),
            },
        };

        var (mgr, store) = MakeWiredPair(AssetKind.BTree);

        // Open asset1 first.
        var doc1 = mgr.Open(asset1);
        var vm1  = BlackboardAuthoringWindow.BuildViewModel(store.ActiveAsset);
        Assert.Single(vm1.Variables);
        Assert.Equal("x", vm1.Variables[0].Name);

        // Activate asset2.
        var doc2 = mgr.Open(asset2);
        var vm2  = BlackboardAuthoringWindow.BuildViewModel(store.ActiveAsset);

        // Assert: the store now points to asset2, and BuildViewModel reflects its schema.
        Assert.Same(asset2, store.ActiveAsset);
        Assert.Equal(2, vm2.Variables.Count);
        Assert.Equal("a", vm2.Variables[0].Name);
        Assert.Equal("b", vm2.Variables[1].Name);

        // Switch back to doc1 — retargets again.
        mgr.Activate(doc1);
        var vmBack = BlackboardAuthoringWindow.BuildViewModel(store.ActiveAsset);
        Assert.Same(asset1, store.ActiveAsset);
        Assert.Single(vmBack.Variables);
        Assert.Equal("x", vmBack.Variables[0].Name);
    }
}
