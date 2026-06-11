using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

// ---- Stubs ------------------------------------------------------------------

file sealed class StubRefactorServiceBbWin : IRefactorService
{
    public IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey) => Array.Empty<AssetReferenceInfo>();
    public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId) => Array.Empty<AssetReferenceInfo>();
    public RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options) =>
        new(fromKey, toKey, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
    public RefactorResult ApplyRename(RefactorPreview preview) =>
        new(true, Array.Empty<string>(), null);
    public DeletePreview PreviewDelete(Guid assetId, DeleteOptions options) =>
        new(assetId, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
    public RefactorResult ApplyDelete(DeletePreview preview) =>
        new(true, Array.Empty<string>(), null);
    public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default) =>
        Task.FromResult(PreviewRename(fromKey, toKey, options));
    public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default) =>
        Task.FromResult(ApplyRename(preview));
}

// ---- Stubs ------------------------------------------------------------------

file sealed class StubBlackboardAsset : IEditableAsset, IBlackboardManagedAsset
{
    public Guid   AssetId        { get; } = Guid.NewGuid();
    public string Name           { get; set; } = "StubAsset";
    public AssetKind Kind        => AssetKind.BTree;
    public string SourceFilePath => "/stub.cs";
    public bool   IsDirty        => false;
    public bool   IsEditorOwned  => true;

    public bool IsBlackboardEditorManaged { get; set; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; set; }
        = Array.Empty<BlackboardVariableEntry>();

    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();

    public void AddVariable(BlackboardVariableEntry entry)                          { }
    public void RemoveVariable(string name)                                         { }
    public void UpdateVariableComment(string name, string? comment)                 { }
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson) { }
    public void MoveVariable(int sourceIndex, int destIndex)                        { }
    public void RenameVariable(string oldName, string newName)                      { }
    public int  CountNodesReferencingVariable(string name) => 0;
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) => Array.Empty<BlackboardAliasBinding>();
    public void AddAlias(string variableName, BlackboardAliasBinding binding)       { }
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
    public void RemoveVariables(IReadOnlyList<string> names)                        { }
}

file sealed class StubNonBlackboardAsset : IEditableAsset
{
    public Guid   AssetId        { get; } = Guid.NewGuid();
    public string Name           { get; set; } = "NonBB";
    public AssetKind Kind        => AssetKind.BTree;
    public string SourceFilePath => "/stub.cs";
    public bool   IsDirty        => false;
    public bool   IsEditorOwned  => true;
    public event Action? Changed { add { } remove { } }
}

// ---- Tests ------------------------------------------------------------------

public sealed class BlackboardAuthoringWindowTests
{
    // ---- BB-1a-03: window identity ------------------------------------------

    [Fact]
    public void Constructor_SetsId()
    {
        var window = new BlackboardAuthoringWindow(new EditorSelectionStore(), new StubRefactorServiceBbWin());
        Assert.Equal("ai_blackboard_variables", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = new BlackboardAuthoringWindow(new EditorSelectionStore(), new StubRefactorServiceBbWin());
        Assert.Equal("Blackboard Variables", window.Title);
    }

    [Fact]
    public void Constructor_SetsOwningPerspective()
    {
        var window = new BlackboardAuthoringWindow(new EditorSelectionStore(), new StubRefactorServiceBbWin());
        Assert.Equal("Authoring", window.OwningPerspective);
    }

    [Fact]
    public void Constructor_SetsScope_PerspectiveBound()
    {
        var window = new BlackboardAuthoringWindow(new EditorSelectionStore(), new StubRefactorServiceBbWin());
        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
    }

    // ---- BB-1a-03: BuildViewModel with null asset ---------------------------

    [Fact]
    public void BuildViewModel_null_asset_HasActiveAsset_false()
    {
        var vm = BlackboardAuthoringWindow.BuildViewModel(null);

        Assert.False(vm.HasActiveAsset);
    }

    [Fact]
    public void BuildViewModel_null_asset_Variables_empty()
    {
        var vm = BlackboardAuthoringWindow.BuildViewModel(null);

        Assert.Empty(vm.Variables);
    }

    [Fact]
    public void BuildViewModel_null_asset_TotalInlineBytes_zero()
    {
        var vm = BlackboardAuthoringWindow.BuildViewModel(null);

        Assert.Equal(0, vm.TotalInlineBytes);
    }

    // ---- BB-1a-03: BuildViewModel with non-blackboard asset ------------------

    [Fact]
    public void BuildViewModel_non_blackboard_asset_HasActiveAsset_true_not_managed()
    {
        var asset = new StubNonBlackboardAsset();
        var vm    = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.True(vm.HasActiveAsset);
        Assert.False(vm.IsBlackboardEditorManaged);
        Assert.Empty(vm.Variables);
    }

    // ---- BB-1a-03: BuildViewModel with managed=false asset ------------------

    [Fact]
    public void BuildViewModel_asset_with_managed_false_shows_not_managed()
    {
        var asset = new StubBlackboardAsset { IsBlackboardEditorManaged = false };
        var vm    = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.True(vm.HasActiveAsset);
        Assert.False(vm.IsBlackboardEditorManaged);
    }

    // ---- BB-1a-03: BuildViewModel with 3 variables --------------------------

    [Fact]
    public void BuildViewModel_managed_asset_with_3_vars_returns_3_rows_in_order()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("speed",  typeof(float), null),
                new BlackboardVariableEntry("count",  typeof(int),   null),
                new BlackboardVariableEntry("active", typeof(bool),  null),
            },
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Equal(3, vm.Variables.Count);
        Assert.Equal("speed",  vm.Variables[0].Name);
        Assert.Equal("count",  vm.Variables[1].Name);
        Assert.Equal("active", vm.Variables[2].Name);
    }

    [Fact]
    public void BuildViewModel_is_blackboard_editor_managed_true_when_managed()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables = new[] { new BlackboardVariableEntry("x", typeof(float), null) },
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.True(vm.IsBlackboardEditorManaged);
    }

    // ---- BB-1a-03: memory budget --------------------------------------------

    [Fact]
    public void BuildViewModel_int_and_bool_total_five_bytes()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("n", typeof(int),  null),
                new BlackboardVariableEntry("b", typeof(bool), null),
            },
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        // int (4 bytes) + bool (1 byte) = 5 bytes (no padding between them due to int-first ordering)
        Assert.Equal(5, vm.TotalInlineBytes);
    }

    [Fact]
    public void BuildViewModel_variable_TypeName_matches_CLR_short_name()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables = new[] { new BlackboardVariableEntry("x", typeof(float), null) },
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Equal("float", vm.Variables[0].TypeName);
    }

    // ---- BB-1a-03: comment surfacing ----------------------------------------

    [Fact]
    public void BuildViewModel_variable_with_comment_preserved_in_view_model()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("hp", typeof(int), "Current hit points."),
            },
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Equal("Current hit points.", vm.Variables[0].Comment);
    }

    [Fact]
    public void BuildViewModel_variable_without_comment_is_null()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables = new[] { new BlackboardVariableEntry("x", typeof(float), null) },
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Null(vm.Variables[0].Comment);
    }

    // ---- BB-1a-03: empty variable list edge case ----------------------------

    [Fact]
    public void BuildViewModel_managed_asset_with_no_vars_returns_empty_rows()
    {
        var asset = new StubBlackboardAsset
        {
            IsBlackboardEditorManaged = true,
            BlackboardVariables       = Array.Empty<BlackboardVariableEntry>(),
        };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.True(vm.IsBlackboardEditorManaged);
        Assert.Empty(vm.Variables);
        Assert.Equal(0, vm.TotalInlineBytes);
    }
}
