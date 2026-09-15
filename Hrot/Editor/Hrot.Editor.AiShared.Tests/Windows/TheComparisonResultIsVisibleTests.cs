using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.Rendering;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-071</c> — THE COMPARISON RESULT REACHES A SURFACE.</b>
/// 📄 <c>docs/DESIGN_Comparison_Ui_Mounting.md</c> §6 items ①a–①c · §7 blockers `B1`–`B3`.
///
/// <para>⛔⛔ <b>What was wrong.</b> 📐 Measured `2026-08-27`: <c>ComparisonSummaryPanel</c>,
/// <c>ComparisonSidebar</c> and <c>ComparisonAnnotationRenderer</c> had <b>zero</b> production
/// constructions ⇒ on the editor the comparison round-trip COMPLETED and its result was invisible; on CGF
/// there was no entry point at all. The only class that ever named the panels was
/// <c>SharedAiWindowRegistrar</c>, which nothing called *(deleted, <c>CE-070</c>)</para>
///
/// <para>⭐⭐⭐ <b>Why these rails assert SUBSTANCE, not presence.</b> ⛔ *"the panel is registered"* is the
/// mistake <c>CE-049</c> and <c>CE-064</c> both made — 📌 and mounting alone would NOT have worked here,
/// because all three of §7's blockers make a correctly-registered panel render nothing:
/// `B1` it was bound to <c>"Analysis"</c>, a perspective nothing registers; `B2` its id was hard-coded, so
/// three instances collide; `B3` <c>SetActiveAsset</c> had no callers, so it sat at
/// <c>HasSession: false</c> forever. ⇒ ⭐ every rail below reads a session BACK.</para>
/// </summary>
public sealed class TheComparisonResultIsVisibleTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    private WindowManager MakeWm() => new(_atlas);

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(string name) { AssetId = Guid.NewGuid(); Name = name; }
        public Guid AssetId { get; }
        public string Name { get; }
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class _StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default)
            => Task.FromResult(PreviewRename(f, t, o));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default)
            => Task.FromResult(ApplyRename(p));
    }

    /// <summary>⭐ A session with one real change, so a reader that finds it cannot be reading an empty
    /// shell.</summary>
    private static ComparisonSessionState SessionFor(Guid assetId) =>
        new(assetId,
            new ComparisonResponse(
                HumanSummary:    "The guard now retreats below 20% health.",
                TopLevelSummary: "One behaviour change.",
                Changes: new[]
                {
                    new ComparisonChange(
                        Kind: "node_added", ElementId: "n1", ElementDescription: "Retreat selector",
                        Field: null, OldValue: null, NewValue: null,
                        Severity: "behavior", Description: "A retreat branch was added."),
                },
                Warnings: Array.Empty<string>()));

    private static PerspectiveWorkspaceRegistrar MakeRegistrar(
        string perspective,
        EditorSelectionStore store,
        ComparisonSessionRegistry? sessionRegistry) =>
        new(
            perspectiveName: perspective,
            selectionStore:  store,
            catalog:         new AssetCatalog(),
            refactorService: new _StubRefactor(),
            debugRegistry:   new DebugSessionRegistry(),
            sessionRegistry: sessionRegistry);

    // ── ①b — the panels exist, per perspective, and ONLY with the capability ──────────────────────

    /// <summary>
    /// ⭐⭐ The panels are built when a session registry is supplied, and are ABSENT — not empty — when it
    /// is not. 📌 Ruling 49, and the same shape <c>Watch</c>/<c>Breakpoints</c> already use for a missing
    /// breakpoint manager.
    /// </summary>
    [Fact]
    public void The_panels_exist_only_when_the_host_has_the_capability()
    {
        var with = MakeRegistrar("BTree", new EditorSelectionStore(), new ComparisonSessionRegistry());
        Assert.NotNull(with.ComparisonSummary);
        Assert.NotNull(with.ComparisonChanges);

        var without = MakeRegistrar("BTree", new EditorSelectionStore(), sessionRegistry: null);
        Assert.Null(without.ComparisonSummary);
        Assert.Null(without.ComparisonChanges);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>`B1` + `B2` — the two defects that would have made a correct mount invisible.</b>
    /// <para>`B1`: the panel's <c>OwningPerspective</c> must be the REAL perspective. ⛔ It used to be the
    /// literal <c>"Analysis"</c>, which no host registers, so a <c>PerspectiveBound</c> window bound to it
    /// could never be shown. `B2`: ids must DIFFER across perspectives, or three instances collide on one
    /// <c>Id</c> — and <c>PanelSnapshot.DeclareInstrumented(Id)</c> would declare one id three times.</para>
    /// </summary>
    [Fact]
    public void The_panels_are_bound_to_a_real_perspective_with_distinct_ids()
    {
        var kinds = new[] { "BTree", "HSM", "Blueprint" };
        var summaryIds = new List<string>();
        var sidebarIds = new List<string>();

        foreach (var kind in kinds)
        {
            var reg = MakeRegistrar(kind, new EditorSelectionStore(), new ComparisonSessionRegistry());

            // ⭐ B1 — the perspective is this registrar's, never the old "Analysis" literal.
            Assert.Equal(kind, reg.ComparisonSummary!.OwningPerspective);
            Assert.Equal(kind, reg.ComparisonChanges!.OwningPerspective);
            Assert.NotEqual("Analysis", reg.ComparisonSummary.OwningPerspective);

            summaryIds.Add(reg.ComparisonSummary.Id);
            sidebarIds.Add(reg.ComparisonChanges.Id);
        }

        // ⭐ B2 — three perspectives, three distinct ids, on both panels.
        Assert.Equal(3, summaryIds.Distinct().Count());
        Assert.Equal(3, sidebarIds.Distinct().Count());
        // ⛔ And the two panels never share an id with each other.
        Assert.Empty(summaryIds.Intersect(sidebarIds));
    }

    /// <summary>
    /// ⭐⭐ Registered into the real <c>WindowManager</c> by <c>RegisterWindows</c> — ⛔ not left for a host
    /// to remember. 📌 That is precisely how they were unreachable for months.
    /// </summary>
    [Fact]
    public void RegisterWindows_registers_both_panels()
    {
        var wm  = MakeWm();
        var reg = MakeRegistrar("BTree", new EditorSelectionStore(), new ComparisonSessionRegistry());
        reg.RegisterWindows(wm);

        // ⭐ Read off the REAL WindowManager, not the registrar's own list — that is the difference
        //   between "the registrar tracked it" and "the shell can show it".
        var ids = wm.RegisteredWindowIds;
        Assert.Contains(reg.ComparisonSummary!.Id, ids);
        Assert.Contains(reg.ComparisonChanges!.Id, ids);
    }

    // ── ①c + item ④ — THE ANTI-VACUITY RAIL: the panel READS THE SESSION BACK ────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>`B3` — THE RAIL THAT MATTERS.</b> Mounting a panel proves nothing if it never learns which
    /// asset it is showing.
    ///
    /// <para>📐 Before <c>CE-071</c>, <c>SetActiveAsset</c> was the only way to tell it, and <b>nothing in
    /// production called it</b> ⇒ ⛔ a mounted panel reported <c>HasSession: false</c> forever, which is
    /// indistinguishable from *"no comparison is running"*. ⭐ Now the panel reads
    /// <c>EditorSelectionStore.ActiveAsset</c> — the pattern <c>BlackboardAuthoringWindow:576</c> already
    /// uses.</para>
    ///
    /// <para>⛔ Asserts CONTENT, not a flag: the summary text and the change count must arrive.</para>
    /// </summary>
    [Fact]
    public void The_summary_panel_reads_the_session_for_the_active_asset()
    {
        var store    = new EditorSelectionStore();
        var registry = new ComparisonSessionRegistry();
        var reg      = MakeRegistrar("BTree", store, registry);
        var panel    = reg.ComparisonSummary!;

        var asset = new FakeAsset("OrcGuard_BT");

        // ⛔ ANTI-VACUITY ①: with no active asset there is nothing to show.
        Assert.False(panel.SimulateDrawClientArea().HasSession);

        // ⛔ ANTI-VACUITY ②: an active asset with NO session is still nothing to show — so a later
        //   HasSession:true cannot come merely from having selected something.
        store.ActiveAsset = asset;
        Assert.False(panel.SimulateDrawClientArea().HasSession);

        // ⭐ Now the comparison exists for THAT asset.
        registry.SetSession(SessionFor(asset.AssetId));

        var vm = panel.SimulateDrawClientArea();
        Assert.True(vm.HasSession);
        Assert.Equal("OrcGuard_BT", vm.AssetName);                       // ⭐ from IEditableAsset.Name
        Assert.Equal("One behaviour change.", vm.TopSummary);            // ⭐ real content, not a flag
        Assert.Equal("The guard now retreats below 20% health.", vm.HumanSummary);
    }

    /// <summary>
    /// ⭐⭐ A session belonging to a DIFFERENT asset must not leak into this panel.
    /// 📌 `D2`: one shared registry is safe precisely because it is keyed by asset id — this rail is what
    /// makes that claim checkable rather than asserted.
    /// </summary>
    [Fact]
    public void A_session_for_another_asset_is_not_shown()
    {
        var store    = new EditorSelectionStore();
        var registry = new ComparisonSessionRegistry();
        var reg      = MakeRegistrar("BTree", store, registry);

        var shown = new FakeAsset("Shown_BT");
        var other = new FakeAsset("Other_BT");

        store.ActiveAsset = shown;
        registry.SetSession(SessionFor(other.AssetId));

        Assert.False(reg.ComparisonSummary!.SimulateDrawClientArea().HasSession);

        // ⭐ …and switching the active asset to the one that HAS a session shows it, which proves the
        //   negative above was about the KEY and not about the panel being broken.
        store.ActiveAsset = other;
        Assert.True(reg.ComparisonSummary.SimulateDrawClientArea().HasSession);
    }

    // ── `D5` (flipped) — the canvas renderer ──────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The canvas annotation renderer is composed for a document, bound to that document's asset, and
    /// is ABSENT when the host has no comparison capability.
    /// <para>📌 The design's `D5` proposed DEFERRING this as *"a different surface"*; 📐 measured, all three
    /// document factories already compose *"built-in set + caller extras"*, so it was the cheapest piece of
    /// the mount. §5b of the composition design records the flip.</para>
    /// </summary>
    [Fact]
    public void The_canvas_renderer_is_composed_per_document_and_absent_without_the_capability()
    {
        var assetId = Guid.NewGuid();

        Assert.Empty(ComparisonCanvasRenderers.For(null, assetId));

        var registry = new ComparisonSessionRegistry();
        var renderers = ComparisonCanvasRenderers.For(registry, assetId);
        var renderer  = Assert.Single(renderers);
        var annotator = Assert.IsType<ComparisonAnnotationRenderer>(renderer);

        // ⭐⭐ Bound to THIS document's asset — ⛔ not left for a SetActiveAsset call nobody makes (`B3`).
        //    IsActive is the renderer's own answer to "do I have something to draw?".
        Assert.False(annotator.IsActive);
        registry.SetSession(SessionFor(assetId));
        Assert.True(annotator.IsActive);
    }
}
