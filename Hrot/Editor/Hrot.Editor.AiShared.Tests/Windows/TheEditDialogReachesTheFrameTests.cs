using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 89 item <c>89a</c> — <c>BP-327</c>, REOPENED: the edit dialog reaches the designer.</b>
///
/// <para>🔴🔴 <b>The defect, measured before building.</b>
/// <c>grep -rn "EditModal" --include=*.cs .</c> → <b>4 hits, none of them a <c>Draw()</c> call</b>: the
/// construction, the property, and two asserts that it is non-null. ⇒ ⛔⛔ <b>the gesture opened a
/// session, the modal held it, and NO FRAME RENDERED IT.</b> 📌 <c>BP-327</c>'s own sentence —
/// <i>"the write is COMPLETE and UNREACHABLE BY A DESIGNER"</i> — still described the editor word for
/// word, one level up from where Batch 84 left it.</para>
///
/// <para>⛔⛔ <b><c>TheEditDialogIsDrawnTests</c> WAS NOT EXTENDED, and must not be.</b> 📐 It
/// <b>constructs the modal itself</b> *(<c>new VariableEditModal(binder, …)</c>)*, so it proves
/// <c>Draw()</c> WORKS and can never ask whether anyone CALLS it. 📌 <b><c>R-67</c>:</b> <i>"a rail that
/// builds its own composition root cannot see a composition-root defect."</i> ⭐ It stayed green through
/// the entire life of the defect it is named for.</para>
///
/// <para>⭐⭐⭐ <b>What THESE rails ask: the CONSTRUCTED <see cref="WindowManager"/>.</b> After a real
/// <c>RegisterWindows</c>, is <b>this registrar's modal's <c>Draw</c></b> in the manager's per-frame
/// overlay list? ⭐ A <b>method group</b> is registered rather than a lambda precisely so that question
/// is answerable by identity — ⛔ a closure would make the delegate opaque and the rail would be reduced
/// to counting.</para>
///
/// <para>⚠ <b>What this half CANNOT see</b>, stated rather than implied: that the overlay slot is
/// actually invoked by a frame. ⭐ That is the OTHER half —
/// <c>Fdp.Presentation.Tests.WindowManager.FrameOverlayTests</c>, which drives a real ImGui frame
/// through <c>ImGuiTestFixture</c>. ⛔ <b>Neither half alone is worth anything</b>: a slot nobody fills
/// draws nothing, and a registration into a slot nobody invokes draws nothing. ⚠ The ImGui context is
/// created THERE and not here on purpose — it is process-global native state, serialized by that
/// project's <c>"ImGui Sequential"</c> collection, and this 1400-test suite has no such guard.</para>
/// </summary>
public sealed class TheEditDialogReachesTheFrameTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    /// <summary>⭐ Built as the composition root builds one — ⛔ nothing about the modal is passed in.</summary>
    private static PerspectiveWorkspaceRegistrar AsTheEditorBuildsIt(string perspective)
        => new(
            perspectiveName:  perspective,
            selectionStore:   new EditorSelectionStore(),
            catalog:          new AssetCatalog(),
            refactorService:  new StubRefactor(),
            debugRegistry:    new DebugSessionRegistry(),
            facetEditService: new ComponentEditServiceBuilder().Build());

    // ══ the modal joins the frame ═══════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail</b>, and the only one in the repo that could have seen this defect.
    /// 🔴 RED before this batch on every perspective: the overlay list did not exist, and no call site
    /// put <c>EditModal.Draw</c> anywhere.
    /// </summary>
    [Theory]
    [InlineData("Blueprint")]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheRegistrarsModalIsInTheFramesOverlayList(string perspective)
    {
        var wm  = new WindowManager(_atlas);
        var reg = AsTheEditorBuildsIt(perspective);

        reg.RegisterWindows(wm);

        Assert.NotNull(reg.EditModal);
        Assert.Contains(wm.FrameOverlays, o =>
            ReferenceEquals(o.Target, reg.EditModal)
            && o.Method.Name == nameof(VariableEditModal.Draw));
    }

    /// <summary>
    /// ⛔ <b>The negative control.</b> Before <c>RegisterWindows</c> the manager has no overlay at all —
    /// ⚠ without this the rail above could pass against a manager that pre-seeds one.
    /// </summary>
    [Fact]
    public void BeforeRegisterWindows_TheFrameHasNoOverlay()
    {
        var wm = new WindowManager(_atlas);
        _ = AsTheEditorBuildsIt("Blueprint");

        Assert.Empty(wm.FrameOverlays);
    }

    /// <summary>
    /// ⚠ <b>A headless host with no edit service has no modal, and registers none</b> — ⛔ a null
    /// overlay would throw at registration, so this is the shape that proves the guard is real rather
    /// than incidental.
    /// </summary>
    [Fact]
    public void WithoutAnEditService_NothingIsRegistered()
    {
        var wm  = new WindowManager(_atlas);
        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName: "Blueprint",
            selectionStore:  new EditorSelectionStore(),
            catalog:         new AssetCatalog(),
            refactorService: new StubRefactor(),
            debugRegistry:   new DebugSessionRegistry());

        reg.RegisterWindows(wm);

        Assert.Null(reg.EditModal);
        Assert.Empty(wm.FrameOverlays);
    }

    /// <summary>
    /// ⭐⭐ <b>Three registrars over one manager put THREE distinct modals in the frame</b> — one per
    /// perspective, each its own object. ⛔ Not one shared modal: the binder, the run state and the
    /// selection store are all per-perspective.
    /// </summary>
    [Fact]
    public void ThreeRegistrarsContributeThreeDistinctModals()
    {
        var wm = new WindowManager(_atlas);
        var regs = new[] { "BTree", "HSM", "Blueprint" }.Select(AsTheEditorBuildsIt).ToList();

        foreach (var r in regs) r.RegisterWindows(wm);

        var targets = wm.FrameOverlays.Select(o => o.Target).ToList();
        Assert.Equal(3, targets.Count);
        Assert.Equal(3, targets.Distinct().Count());
        foreach (var r in regs) Assert.Contains(r.EditModal, targets);
    }

    // ══ 89b — three modals, three ImGui ids ═════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>89b</c>: the popup id is PER INSTANCE.</b>
    ///
    /// <para>🔴 <c>Draw()</c> used <see cref="VariableEditModal.Title"/> — a <c>public const</c> — for
    /// BOTH <c>OpenPopup</c> and <c>BeginPopupModal</c>. ⇒ once <c>89a</c> lands, three modals draw
    /// every frame under ONE ImGui id, correct only because <c>if (!IsOpen) return</c> fires first for
    /// the other two. ⛔ <b>An undocumented guard standing between two popups with the same id.</b></para>
    ///
    /// <para>📌 <b>This repo has already paid for popup-id confusion once</b> —
    /// <c>AssetPickerModal:185-189</c>: <i>"the popup opens under one id while <c>BeginPopupModal</c>
    /// waits on another, so it never renders."</i></para>
    /// </summary>
    [Fact]
    public void TheThreeModalsHaveDistinctPopupIds()
    {
        var ids = new[] { "BTree", "HSM", "Blueprint" }
            .Select(p => AsTheEditorBuildsIt(p).EditModal!.PopupId)
            .ToList();

        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// ⭐⭐ <b>And the designer still reads the same words.</b> Everything before <c>##</c> is what ImGui
    /// DISPLAYS — ⛔ so scoping the id must not have renamed the dialog on two of three hosts, which
    /// would be a visible regression traded for an invisible fix.
    /// </summary>
    [Theory]
    [InlineData("Blueprint")]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void ThePopupIdStillDisplaysTheOneTitle(string perspective)
    {
        var id = AsTheEditorBuildsIt(perspective).EditModal!.PopupId;

        Assert.StartsWith(VariableEditModal.Title, id, StringComparison.Ordinal);
        Assert.Equal(VariableEditModal.Title, id.Split("##")[0]);
    }

    /// <summary>⭐ A modal with no scope keeps the bare title — the headless single-instance shape, and
    /// what keeps the existing <c>TheEditDialogIsDrawnTests</c> harness meaningful.</summary>
    [Fact]
    public void AnUnscopedModalKeepsTheBareTitle()
    {
        var reg = AsTheEditorBuildsIt("Blueprint");
        var solo = new VariableEditModal(reg.EditGestures!, () => VariableRunState.Planning);

        Assert.Equal(VariableEditModal.Title, solo.PopupId);
    }

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
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
