using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

// ---- Stubs — same shape as BlackboardAuthoringWindowTests, kept local (file-scoped there) ----------

file sealed class StubRefactorServiceBbDump : IRefactorService
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

file sealed class StubBlackboardAssetForDump : IEditableAsset, IBlackboardManagedAsset
{
    public Guid   AssetId        { get; } = Guid.NewGuid();
    public string Name           { get; set; } = "StubAsset";
    public AssetKind Kind        => AssetKind.BTree;
    public string SourceFilePath => "/stub.cs";
    public bool   IsDirty        => false;
    public bool   IsEditorOwned  => true;

    public bool IsBlackboardEditorManaged { get; set; } = true;
    public void SetBlackboardEditorManaged(bool managed) => IsBlackboardEditorManaged = managed;
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

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>BlackboardAuthoringWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> IN-FLIGHT trio.
///
/// <para>⭐⭐ Mirrors <c>ThePilotPanelDumpsWhatItDrawsTests</c>. ⚠ <c>PanelSnapshot</c> is process-global
/// static state; every case resets it.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class BlackboardAuthoringWindowDumpsItsVariablesTests : IDisposable
{
    public BlackboardAuthoringWindowDumpsItsVariablesTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static BlackboardAuthoringWindow MakeWindow(string id)
        => new(new EditorSelectionStore(), new StubRefactorServiceBbDump(), idOverride: id);

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_blackboard_variables_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheVariable()
    {
        const string id = "ai_blackboard_variables_rail2";
        PanelSnapshot.CaptureEnabled = true;

        var store = new EditorSelectionStore();
        var asset = new StubBlackboardAssetForDump
        {
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("Health", typeof(int), Comment: null),
            },
        };
        store.ActiveAsset = asset;

        var window = new BlackboardAuthoringWindow(store, new StubRefactorServiceBbDump(), idOverride: id);

        window.SimulateDrawContent();   // ⭐ no ImGui context — headless on purpose

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(BlackboardAuthoringWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["hasActiveAsset"]!.GetValue<bool>());
        Assert.True(dump["isBlackboardEditorManaged"]!.GetValue<bool>());
        Assert.Equal(1, dump["variableCount"]!.GetValue<int>());
        var rows = dump["variables"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("Health", rows[0]!["name"]!.GetValue<string>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "ai_blackboard_variables_rail3";
        var window = MakeWindow(id);   // CaptureEnabled stays false

        var vm = window.SimulateDrawContent();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);            // ⭐ the BUILD is unaffected by the flag
        Assert.False(vm.HasActiveAsset);
    }
}
