using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Fdp.Presentation.WindowManager;
using Fdp.Presentation.Icons;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 87 item 2b — EVERY table host a perspective builds has its row gestures bound.</b>
///
/// <para>🔴🔴 <b>The defect.</b> 📐 The registrar said <c>EditGestures.Attach(Variables.Control)</c> —
/// <b>one of four tables</b>. ⛔ The Details panel and both Watch surfaces drew rows with no menu and
/// no double-click, and the visual check read that as <c>BP-327</c> *("the dialog has no OK
/// button")*. ⚠⚠ <b>Two defects wearing one name</b>, and fixing <c>BP-327</c> alone would have
/// produced a dialog nobody could open.</para>
///
/// <para>⭐⭐⭐ <b>Why these rails ask the CONSTRUCTED objects.</b> 📌 <c>R-67</c>: <i>"a rail that
/// builds its own composition root cannot see a composition-root defect."</i> ⛔ A rail that
/// re-attached the binder itself would pass on a registrar that attaches nothing —
/// <b>that is exactly how Batch 83's dialog rails stayed green while the production dialog did
/// nothing.</b> ⇒ ⭐ every assertion below reads <see cref="VariableTableControl.HasEditGestures"/> off
/// the object the REGISTRAR built.</para>
///
/// <para>⚠ <b>What this cannot see</b>, stated rather than implied: that ImGui routes a real
/// right-click or double-click to the bound handler. It proves the subscription exists on the object
/// that draws — which is the half that was missing.</para>
/// </summary>
public sealed class EveryTableHostIsGestureBoundTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    private static IComponentEditService EditService() => new ComponentEditServiceBuilder().Build();

    /// <summary>⭐ Built the way the composition root builds one — plus the edit service, without which
    /// there is no binder at all.</summary>
    private static PerspectiveWorkspaceRegistrar MakeRegistrar(EditorSelectionStore store)
        => new(
            perspectiveName:  "Blueprint",
            selectionStore:   store,
            catalog:          new AssetCatalog(),
            refactorService:  new StubRefactorService(),
            debugRegistry:    new DebugSessionRegistry(),
            facetEditService: EditService());

    // ══ the binder and its dialog both exist ════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b><c>BP-327</c>: a binder with no dialog is a gesture that opens nothing.</b> Batch 84
    /// built the whole commit path and ⛔ <b>no surface drew the session</b>, so the two must arrive
    /// together or the batch reproduces the defect it exists to fix.
    /// </summary>
    [Fact]
    public void AnEditServiceYieldsBothTheBinderAndTheDialog()
    {
        var reg = MakeRegistrar(new EditorSelectionStore());

        Assert.NotNull(reg.EditGestures);
        Assert.NotNull(reg.EditModal);
    }

    /// <summary>⛔ The negative control — no edit service, no session, so neither exists. ⚠ Without
    /// this the pair above could be unconditionally constructed and prove nothing.</summary>
    [Fact]
    public void WithoutAnEditService_ThereIsNoBinderAndNoDialog()
    {
        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName: "Blueprint",
            selectionStore:  new EditorSelectionStore(),
            catalog:         new AssetCatalog(),
            refactorService: new StubRefactorService(),
            debugRegistry:   new DebugSessionRegistry());

        Assert.Null(reg.EditGestures);
        Assert.Null(reg.EditModal);
    }

    // ══ the tables ══════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail.</b> Every table the registrar bound reports its gestures live — asked of the
    /// control, not of the registrar's source.
    /// </summary>
    [Fact]
    public void EveryBoundTableReportsItsGestures()
    {
        var reg = MakeRegistrar(new EditorSelectionStore());

        Assert.NotEmpty(reg.BoundTables);
        Assert.All(reg.BoundTables, t => Assert.True(t.HasEditGestures,
            "a table the registrar bound reports no gestures — the subscription did not land on the "
            + "object that draws."));
    }

    /// <summary>
    /// ⭐⭐ <b>The standalone Variables window is bound</b> — the one host that was already correct,
    /// asserted so a refactor cannot lose it while fixing the others.
    /// </summary>
    [Fact]
    public void TheStandaloneVariablesTableIsBound()
        => Assert.True(MakeRegistrar(new EditorSelectionStore()).Variables.Control.HasEditGestures);

    /// <summary>
    /// ⭐⭐⭐ <b>A Details section handed to the registrar as an extra IS bound.</b>
    ///
    /// <para>🔴 This is the defect itself: the Details panel arrives through
    /// <c>RegisterExtraWindow</c>, LONG after the constructor's single <c>Attach</c> ran, so the
    /// constructor could never have reached it. ⭐ Stated over
    /// <see cref="IVariableTableHost"/> so a host added later binds itself with no new line
    /// anywhere.</para>
    /// </summary>
    [Fact]
    public void AHostRegisteredAsAnExtraIsBound()
    {
        var wm  = new WindowManager(_atlas);
        var reg = MakeRegistrar(new EditorSelectionStore());
        reg.RegisterWindows(wm);

        var host = new TableHostWindow();
        Assert.False(host.Table.HasEditGestures);   // ⭐ RED before the production call runs

        // ⭐⭐⭐ Through the PRODUCTION path — RegisterExtraWindow, exactly as the composition root
        //    calls it. 🔴 An earlier version of this rail called the internal bind helper directly and
        //    STAYED GREEN under the revert probe that removed the RegisterExtraWindow line — 📌 R-67
        //    in miniature, in the rail written to catch R-67. The probe is what exposed it.
        reg.RegisterExtraWindow(wm, host);

        Assert.True(host.Table.HasEditGestures);
        Assert.Contains(host.Table, reg.BoundTables);
    }

    /// <summary>⭐ A window that is also a table host — the shape the Details panel and the Blueprint
    /// Watch both have.</summary>
    private sealed class TableHostWindow : ManagedWindow, IVariableTableHost
    {
        public TableHostWindow()
            : base("b87_table_host", "b87_table_host", "Blueprint", WindowScope.PerspectiveBound) { }

        public VariableTableControl Table { get; } =
            new(new VariableValueFormatter(RawValueDecoder.Instance));

        VariableTableControl? IVariableTableHost.VariableTable => Table;

        protected override void DrawClientArea() { }
    }

    /// <summary>
    /// ⭐⭐ <b>Binding twice does not subscribe twice.</b> ⛔ <c>RegisterExtraWindow</c> can be called
    /// more than once for one window, and a double subscription opens TWO sessions per double-click
    /// and leaks the first — a defect that would look like "the dialog flickers".
    /// </summary>
    [Fact]
    public void BindingTheSameHostTwiceIsIdempotent()
    {
        var reg     = MakeRegistrar(new EditorSelectionStore());
        var section = new VariableDetailsSection(new VariableValueFormatter(RawValueDecoder.Instance));

        reg.BindTableHostForTest(section);
        var afterFirst = reg.BoundTables.Count;
        reg.BindTableHostForTest(section);
        // ⚠ Idempotence is a property of the bind helper itself, so this one legitimately calls it
        //   directly — the PRODUCTION-path claim is the rail above.

        Assert.Equal(afterFirst, reg.BoundTables.Count);
    }

    /// <summary>
    /// ⚠ <b>A host with no table is a SHAPE, not a defect.</b> A Watch built without a variable panel
    /// has nothing to bind — ⛔ and it must not land in <see cref="PerspectiveWorkspaceRegistrar.BoundTables"/>,
    /// or the "all bound tables report gestures" rail above could pass vacuously on a null.
    /// </summary>
    [Fact]
    public void AHostWithNoTableContributesNothing()
    {
        var reg    = MakeRegistrar(new EditorSelectionStore());
        var before = reg.BoundTables.Count;

        reg.BindTableHostForTest(new TablelessHost());

        Assert.Equal(before, reg.BoundTables.Count);
    }

    private sealed class TablelessHost : IVariableTableHost
    {
        public VariableTableControl? VariableTable => null;
    }

    private sealed class StubRefactorService : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
