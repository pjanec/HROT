using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// Tests for the optional idOverride / owningPerspective ctor parameters
/// added to the per-perspective shared windows (AIE-013 SC: SharedWindow_IdOverride_ProducesDistinctId).
/// </summary>
public class SharedWindowIdOverrideTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IRefactorService StubRefactor()
    {
        return new _StubRefactorService();
    }

    private sealed class _StubRefactorService : IRefactorService
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

    // ── InspectorWindow ───────────────────────────────────────────────────────

    [Fact]
    public void InspectorWindow_DefaultCtor_UsesDefaultIdAndPerspective()
    {
        var w = new InspectorWindow(
            new EditorSelectionStore(),
            StubRefactor(),
            new FindResultsWindow());

        Assert.Equal("ai_inspector", w.Id);
        Assert.Equal("Authoring", w.OwningPerspective);
    }

    [Fact]
    public void InspectorWindow_IdOverride_ProducesDistinctId()
    {
        var w1 = new InspectorWindow(
            new EditorSelectionStore(), StubRefactor(), new FindResultsWindow(),
            idOverride: "ai_inspector_btree", owningPerspective: "BTree");

        var w2 = new InspectorWindow(
            new EditorSelectionStore(), StubRefactor(), new FindResultsWindow(),
            idOverride: "ai_inspector_hsm", owningPerspective: "HSM");

        Assert.Equal("ai_inspector_btree", w1.Id);
        Assert.Equal("BTree",              w1.OwningPerspective);

        Assert.Equal("ai_inspector_hsm", w2.Id);
        Assert.Equal("HSM",              w2.OwningPerspective);

        Assert.NotEqual(w1.Id, w2.Id);
    }

    // ── SharedWindow_IdOverride_ProducesDistinctId (all window types) ─────────

    [Fact]
    public void SharedWindow_IdOverride_ProducesDistinctId_InspectorWindow()
    {
        var registry = new DebugSessionRegistry();
        var w1 = new InspectorWindow(
            new EditorSelectionStore(), StubRefactor(), new FindResultsWindow(),
            idOverride: "ai_inspector_btree", owningPerspective: "BTree");
        var w2 = new InspectorWindow(
            new EditorSelectionStore(), StubRefactor(), new FindResultsWindow(),
            idOverride: "ai_inspector_hsm", owningPerspective: "HSM");

        Assert.NotEqual(w1.Id, w2.Id);
        Assert.Equal("BTree", w1.OwningPerspective);
        Assert.Equal("HSM",   w2.OwningPerspective);
        Assert.Equal(WindowScope.PerspectiveBound, w1.Scope);
        Assert.Equal(WindowScope.PerspectiveBound, w2.Scope);
    }

    [Fact]
    public void SharedWindow_IdOverride_ProducesDistinctId_RuntimeInspectorWindow()
    {
        var registry = new DebugSessionRegistry();
        var w1 = new RuntimeInspectorWindow(
            new EditorSelectionStore(), registry,
            idOverride: "ai_runtime_inspector_btree", owningPerspective: "BTree");
        var w2 = new RuntimeInspectorWindow(
            new EditorSelectionStore(), registry,
            idOverride: "ai_runtime_inspector_hsm", owningPerspective: "HSM");

        Assert.NotEqual(w1.Id, w2.Id);
        Assert.Equal("BTree", w1.OwningPerspective);
        Assert.Equal("HSM",   w2.OwningPerspective);
    }

    [Fact]
    public void SharedWindow_IdOverride_ProducesDistinctId_TraceTimelineWindow()
    {
        var registry = new DebugSessionRegistry();
        var w1 = new TraceTimelineWindow(
            new EditorSelectionStore(), registry,
            idOverride: "ai_trace_timeline_btree", owningPerspective: "BTree");
        var w2 = new TraceTimelineWindow(
            new EditorSelectionStore(), registry,
            idOverride: "ai_trace_timeline_hsm", owningPerspective: "HSM");

        Assert.NotEqual(w1.Id, w2.Id);
        Assert.Equal("BTree", w1.OwningPerspective);
        Assert.Equal("HSM",   w2.OwningPerspective);
    }

    [Fact]
    public void SharedWindow_IdOverride_ProducesDistinctId_FindResultsWindow()
    {
        var w1 = new FindResultsWindow(
            idOverride: "ai_find_results_btree", owningPerspective: "BTree");
        var w2 = new FindResultsWindow(
            idOverride: "ai_find_results_hsm", owningPerspective: "HSM");

        Assert.NotEqual(w1.Id, w2.Id);
        Assert.Equal("BTree", w1.OwningPerspective);
        Assert.Equal("HSM",   w2.OwningPerspective);
    }

    [Fact]
    public void SharedWindow_IdOverride_ProducesDistinctId_BlackboardAuthoringWindow()
    {
        var w1 = new BlackboardAuthoringWindow(
            new EditorSelectionStore(), StubRefactor(),
            idOverride: "ai_blackboard_variables_btree", owningPerspective: "BTree");
        var w2 = new BlackboardAuthoringWindow(
            new EditorSelectionStore(), StubRefactor(),
            idOverride: "ai_blackboard_variables_hsm", owningPerspective: "HSM");

        Assert.NotEqual(w1.Id, w2.Id);
        Assert.Equal("BTree", w1.OwningPerspective);
        Assert.Equal("HSM",   w2.OwningPerspective);
    }

    [Fact]
    public void SharedWindow_IdOverride_ProducesDistinctId_DiagnosticsWindow()
    {
        var catalog = new AssetCatalog();
        var w1 = new DiagnosticsWindow(
            catalog, Array.Empty<IAssetValidator>(),
            idOverride: "ai_diagnostics_btree", owningPerspective: "BTree");
        var w2 = new DiagnosticsWindow(
            catalog, Array.Empty<IAssetValidator>(),
            idOverride: "ai_diagnostics_hsm", owningPerspective: "HSM");

        Assert.NotEqual(w1.Id, w2.Id);
        Assert.Equal("BTree", w1.OwningPerspective);
        Assert.Equal("HSM",   w2.OwningPerspective);
    }

    // ── Back-compat: no overrides → same defaults as before ──────────────────

    [Fact]
    public void SharedWindow_NoOverrides_DefaultsAreBackwardCompat()
    {
        var registry = new DebugSessionRegistry();
        var catalog  = new AssetCatalog();

        var inspector  = new InspectorWindow(new EditorSelectionStore(), StubRefactor(), new FindResultsWindow());
        var runtime    = new RuntimeInspectorWindow(new EditorSelectionStore(), registry);
        var timeline   = new TraceTimelineWindow(new EditorSelectionStore(), registry);
        var findResult = new FindResultsWindow();
        var blackboard = new BlackboardAuthoringWindow(new EditorSelectionStore(), StubRefactor());
        var diag       = new DiagnosticsWindow(catalog, Array.Empty<IAssetValidator>());

        Assert.Equal("ai_inspector",            inspector.Id);
        Assert.Equal("Authoring",               inspector.OwningPerspective);

        Assert.Equal("ai_runtime_inspector",    runtime.Id);
        Assert.Equal("Authoring",               runtime.OwningPerspective);

        Assert.Equal("ai_trace_timeline",       timeline.Id);
        Assert.Equal("Authoring",               timeline.OwningPerspective);

        Assert.Equal("ai_find_results",         findResult.Id);
        Assert.Equal("Authoring",               findResult.OwningPerspective);

        Assert.Equal("ai_blackboard_variables", blackboard.Id);
        Assert.Equal("Authoring",               blackboard.OwningPerspective);

        Assert.Equal("ai_diagnostics",          diag.Id);
        Assert.Equal("Authoring",               diag.OwningPerspective);
    }
}
