using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// Tests for <see cref="PerspectiveWorkspaceRegistrar"/> and
/// <see cref="WindowManagerPerspectiveSwitcher"/> (AIE-014).
/// </summary>
public class PerspectiveWorkspaceRegistrarTests : IDisposable
{
    // ── Shared infrastructure ──────────────────────────────────────────────────

    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private WindowManager MakeWm() => new(_atlas);

    private static IRefactorService StubRefactor() => new _StubRefactorService();

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

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name)
        {
            AssetId = Guid.NewGuid();
            Kind    = kind;
            Name    = name;
        }
        public Guid AssetId { get; }
        public string Name { get; }
        public AssetKind Kind { get; }
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private static PerspectiveWorkspaceRegistrar MakeRegistrar(string perspective) =>
        new(
            perspectiveName:   perspective,
            selectionStore:    new EditorSelectionStore(),
            catalog:           new AssetCatalog(),
            refactorService:   StubRefactor(),
            debugRegistry:     new DebugSessionRegistry());

    // ── AIE-014 SC1: All windows have OwningPerspective == kind and unique ids ─

    [Fact]
    public void PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds_BTree()
    {
        var wm   = MakeWm();
        var reg  = MakeRegistrar("BTree");
        reg.RegisterWindows(wm);

        var windows = reg.RegisteredWindows;
        Assert.NotEmpty(windows);

        // Every window must belong to the BTree perspective.
        foreach (var w in windows)
            Assert.Equal("BTree", w.OwningPerspective);

        // All ids must be unique.
        var ids = windows.Select(w => w.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds_HSM()
    {
        var wm  = MakeWm();
        var reg = MakeRegistrar("HSM");
        reg.RegisterWindows(wm);

        foreach (var w in reg.RegisteredWindows)
            Assert.Equal("HSM", w.OwningPerspective);

        var ids = reg.RegisteredWindows.Select(w => w.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds_Blueprint()
    {
        var wm  = MakeWm();
        var reg = MakeRegistrar("Blueprint");
        reg.RegisterWindows(wm);

        foreach (var w in reg.RegisteredWindows)
            Assert.Equal("Blueprint", w.OwningPerspective);

        var ids = reg.RegisteredWindows.Select(w => w.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>
    /// Three registrars over the same WindowManager produce 3× 6 windows with no id collisions.
    /// </summary>
    [Fact]
    public void ThreeRegistrars_ShareWindowManager_ProduceDistinctIdSets()
    {
        var wm    = MakeWm();
        var btree = MakeRegistrar("BTree");
        var hsm   = MakeRegistrar("HSM");
        var bp    = MakeRegistrar("Blueprint");

        btree.RegisterWindows(wm);
        hsm.RegisterWindows(wm);
        bp.RegisterWindows(wm);

        var allIds = btree.RegisteredWindows
            .Concat(hsm.RegisteredWindows)
            .Concat(bp.RegisteredWindows)
            .Select(w => w.Id)
            .ToList();

        // 3 perspectives × 6 windows = 18 distinct ids.
        // ⚠ 18 → 21 (Batch 79: the Variables table) → 23 (Batch 80: the derived outline, on BTree and
        //    HSM only — +2, not +3, because Blueprint keeps BlueprintMyBlueprintWindow).
        // ⚠ 23 → 25 (Batch 88b: the AI Details panel, again BTree and HSM only — +2, not +3, because
        //    Blueprint keeps BlueprintDetailsWindow. ⭐ A second Details there would be two panels for
        //    one concept AND an id collision, which RegisterCore now refuses at startup).
        // ⛔⛔ 25 → 26 (S1 / BP-399, 2026-08-22: DESIGN_Details_Panel_View_Switching.md §7.3 ① — the
        //    shell is built for EVERY perspective, so Blueprint gets the third AI Details panel and the
        //    88b note above is SUPERSEDED. ⭐ It is still ONE panel per perspective: BlueprintDetailsWindow
        //    is retired in the same commit — it HAD to be, because it claims the same
        //    `ai_details_blueprint` id and RegisterCore throws on a duplicate).
        //    ⭐ The property under test is distinctness, and it still holds.
        Assert.Equal(26, allIds.Count);
        Assert.Equal(26, allIds.Distinct().Count());
    }

    /// <summary>
    /// Registrar exposes the six core side-panel windows as typed properties.
    /// </summary>
    [Fact]
    public void PerspectiveRegistrar_ExposesNamedWindows()
    {
        var reg = MakeRegistrar("BTree");

        Assert.NotNull(reg.FindResults);
        Assert.NotNull(reg.Inspector);
        Assert.NotNull(reg.RuntimeInspector);
        Assert.NotNull(reg.TraceTimeline);
        Assert.NotNull(reg.BlackboardAuthoring);
        Assert.NotNull(reg.Diagnostics);
    }

    // ── SE1 live-wiring: facetEditService forwarded to the Inspector ──────────

    /// <summary>
    /// SE1: when the composition root passes a <c>facetEditService</c> to the registrar
    /// ctor, it must flow through to the Inspector so facets render as live editable fields
    /// (not the fallback stub). This mirrors the exact wiring done in EditorSubsystem.
    /// </summary>
    [Fact]
    public void PerspectiveRegistrar_ForwardsFacetEditService_ToInspector()
    {
        var editSvc = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();

        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName:  "BTree",
            selectionStore:   new EditorSelectionStore(),
            catalog:          new AssetCatalog(),
            refactorService:  StubRefactor(),
            debugRegistry:    new DebugSessionRegistry(),
            facetEditService: editSvc);

        Assert.True(reg.Inspector.HasFacetEditService,
            "the facetEditService passed to the registrar ctor must reach the Inspector");
    }

    /// <summary>
    /// SE1 negative control: without a facetEditService the Inspector falls back to the stub
    /// (HasFacetEditService == false), confirming the wiring is what enables live rendering.
    /// </summary>
    [Fact]
    public void PerspectiveRegistrar_WithoutFacetEditService_InspectorHasNone()
    {
        var reg = MakeRegistrar("BTree");
        Assert.False(reg.Inspector.HasFacetEditService);
    }

    /// <summary>
    /// RegisterExtraWindow adds to the RegisteredWindows list and registers in WM.
    /// </summary>
    [Fact]
    public void PerspectiveRegistrar_RegisterExtraWindow_IsTrackedAndRegisteredInWm()
    {
        var wm  = MakeWm();
        var reg = MakeRegistrar("BTree");
        reg.RegisterWindows(wm);

        int before = reg.RegisteredWindows.Count;

        // Create a dummy extra window (e.g. future canvas window).
        var extra = new _DummyWindow("ai_canvas_btree", "BTree");
        reg.RegisterExtraWindow(wm, extra);

        Assert.Equal(before + 1, reg.RegisteredWindows.Count);
        Assert.Contains(extra, reg.RegisteredWindows);

        // Must also appear in the WindowManager.
        Assert.True(wm.TryGetWindow("ai_canvas_btree", out _));
    }

    private sealed class _DummyWindow : ManagedWindow
    {
        public _DummyWindow(string id, string perspective)
            : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

    // ── AIE-014 SC2: WindowManagerPerspectiveSwitcher ─────────────────────────

    [Fact]
    public void WindowManagerPerspectiveSwitcher_Switch_CallsWindowManagerSwitchPerspective()
    {
        var wm      = MakeWm();
        var switcher = new WindowManagerPerspectiveSwitcher(wm);

        // Register a window so the perspective is known.
        wm.RegisterWindow(new _DummyWindow("win_btree", "BTree"));

        switcher.SwitchPerspective("BTree");

        Assert.Equal("BTree", wm.CurrentPerspective);
    }

    [Fact]
    public void WindowManagerPerspectiveSwitcher_Switch_IsSameAsPerspective_NoOp()
    {
        var wm       = MakeWm();
        var switcher = new WindowManagerPerspectiveSwitcher(wm);

        wm.RegisterWindow(new _DummyWindow("w1", "Alpha"));
        wm.SwitchPerspective("Alpha");

        int fireCount = 0;
        wm.OnPerspectiveChanged += (_, _) => fireCount++;

        // Switching to the same perspective is a no-op.
        switcher.SwitchPerspective("Alpha");

        Assert.Equal(0, fireCount);
    }

    // ── AIE-014 SC3: PerspectiveSwitch with open doc → activates most-recent ───

    [Fact]
    public void PerspectiveSwitch_WithOpenDocOfKind_ActivatesMostRecent()
    {
        var wm      = MakeWm();
        var switcher = new WindowManagerPerspectiveSwitcher(wm);

        var switchLog = new List<string>();
        var mgr = new AiDocumentManager(
            perspectiveSwitchCallback: k => { switchLog.Add(k); wm.SwitchPerspective(k); });

        switcher.SetDocumentManager(mgr);

        // Register windows for BTree and HSM so SwitchPerspective is not a no-op.
        wm.RegisterWindow(new _DummyWindow("w_btree", "BTree"));
        wm.RegisterWindow(new _DummyWindow("w_hsm",   "HSM"));

        // Open two BTree documents and one HSM document.
        var bt1 = mgr.Open(new FakeAsset(AssetKind.BTree, "BTree1")); // activates → "BTree"
        var bt2 = mgr.Open(new FakeAsset(AssetKind.BTree, "BTree2")); // activates → "BTree"
        var hsm = mgr.Open(new FakeAsset(AssetKind.Hsm,   "Hsm1"));   // activates → "Hsm"

        // Current: hsm is active, perspective is "Hsm".
        Assert.Same(hsm, mgr.Active);

        // Manually switch to "BTree" perspective (simulates user clicking the toolbar).
        // This fires OnPerspectiveChanged → switcher should activate the most-recent BTree doc (bt2).
        wm.SwitchPerspective("BTree");

        // The most-recent BTree document (bt2, the last one opened) should now be active.
        Assert.Same(bt2, mgr.Active);
    }

    [Fact]
    public void PerspectiveSwitch_NoDocOfKind_NoThrow()
    {
        var wm       = MakeWm();
        var switcher = new WindowManagerPerspectiveSwitcher(wm);

        var mgr = new AiDocumentManager(
            perspectiveSwitchCallback: k => wm.SwitchPerspective(k));
        switcher.SetDocumentManager(mgr);

        wm.RegisterWindow(new _DummyWindow("w_btree", "BTree"));

        // Open only an HSM document; switch to BTree — no BTree docs open.
        mgr.Open(new FakeAsset(AssetKind.Hsm, "Hsm1"));

        // Must not throw; BTree canvas shows empty state.
        var ex = Record.Exception(() => wm.SwitchPerspective("BTree"));
        Assert.Null(ex);

        // Active doc is still the HSM one (the switcher didn't change it since no BTree docs exist).
        Assert.Equal(AssetKind.Hsm, mgr.Active!.Kind);
    }

    [Fact]
    public void PerspectiveSwitch_AlreadyActiveDocOfKind_DoesNotReactivate()
    {
        var wm       = MakeWm();
        var switcher = new WindowManagerPerspectiveSwitcher(wm);

        var mgr = new AiDocumentManager(
            perspectiveSwitchCallback: k => wm.SwitchPerspective(k));

        switcher.SetDocumentManager(mgr);

        wm.RegisterWindow(new _DummyWindow("w_btree", "BTree"));
        wm.RegisterWindow(new _DummyWindow("w_hsm",   "HSM"));

        var bt = mgr.Open(new FakeAsset(AssetKind.BTree, "BTree1"));
        var hs = mgr.Open(new FakeAsset(AssetKind.Hsm,   "Hsm1"));

        // Activate the BTree doc (perspective already "BTree").
        mgr.Activate(bt);
        // Now switch to BTree again — no perspective change event fires.
        int fires = 0;
        wm.OnPerspectiveChanged += (_, _) => fires++;

        wm.SwitchPerspective("BTree"); // already "BTree", so no-op
        Assert.Equal(0, fires);
    }

    // ── DEBT-AIB-009 (Batch 69) — the schema exporter's forwarding ──────────────

    private sealed class StubSchemaExporter : Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter
    {
        public IReadOnlyDictionary<string, Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry> All { get; }
            = new Dictionary<string, Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry>();
        public Hrot.Editor.AiShared.Blackboard.ActionSchemaEntry? Lookup(string fqn) => null;
        public void Rebuild() { }
        public event Action? Changed { add { } remove { } }
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>DEBT-AIB-009</c>: an exporter handed to the registrar must reach the authoring
    /// window.</b>
    ///
    /// <para>
    /// 📄 The debt, verbatim: <i>"hardcoded-DTO reflection <b>not wired in production DI</b>"</i>.
    /// 📐 Measured on <c>HEAD</c> and true — this registrar <b>held</b> the exporter and handed it to
    /// the validator two lines above the window it did not hand it to. ⇒ ⛔ <b>a value column over a
    /// schema nothing supplies.</b>
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Deliberately placed beside <c>PerspectiveRegistrar_ForwardsFacetEditService_ToInspector</c></b>
    /// — the same question about the same registrar, and the precedent that shows the right way to ask
    /// it. 🔴 <b>An earlier draft scanned the caller's IL and was VACUOUS</b>: it checked whether the
    /// caller's SIGNATURE mentioned the type, which this registrar satisfies whether or not it passes
    /// the argument on, so the revert probe did not redden. ⭐ Asking the OBJECT cannot be fooled that
    /// way.
    /// </para>
    /// </summary>
    [Fact]
    public void PerspectiveRegistrar_ForwardsSchemaExporter_ToBlackboardAuthoring()
    {
        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName: "BTree",
            selectionStore:  new EditorSelectionStore(),
            catalog:         new AssetCatalog(),
            refactorService: StubRefactor(),
            debugRegistry:   new DebugSessionRegistry(),
            schemaExporter:  new StubSchemaExporter());

        Assert.True(reg.BlackboardAuthoring.HasSchemaExporter,
            "the schemaExporter passed to the registrar must reach BlackboardAuthoringWindow, "
            + "or its hardcoded-DTO reflection contributes nothing in production");
    }

    /// <summary>⭐ Negative control, mirroring the facet-edit-service pair: without one the window has
    /// none. ⛔ Without it the test above would also pass against a window that fabricates its own.</summary>
    [Fact]
    public void PerspectiveRegistrar_WithoutSchemaExporter_AuthoringWindowHasNone()
        => Assert.False(MakeRegistrar("BTree").BlackboardAuthoring.HasSchemaExporter);

    // ── 88a — the live-value provider's forwarding ──────────────────────────────

    private sealed class StubLiveValues : Hrot.Editor.AiShared.Blackboard.ILiveBlackboardValueProvider
    {
        public IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset)
            => new Dictionary<string, string>();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>88a</c>: a live-value provider handed to the registrar must reach the window that
    /// CONSUMES it.</b>
    ///
    /// <para>📐 <b>Measured, and it is the whole point of this rail:</b>
    /// <see cref="Hrot.Editor.AiShared.Blackboard.ILiveBlackboardValueProvider"/> has ⭐ <b>exactly one
    /// consumer</b> — <c>BlackboardAuthoringWindow</c> (<c>:514</c>, <c>GetLiveVariableValues</c> once
    /// per frame). ⇒ if the registrar drops it here, the argument the composition root passes goes
    /// nowhere and every Value cell shows <c>—</c>.</para>
    ///
    /// <para>⭐⭐ <b>Placed beside the schema-exporter pair deliberately</b> — 📌 <c>R-67</c> is the same
    /// ruling and <b>this registrar is the one that has forgotten a service four times</b>. ⛔ Asking
    /// the OBJECT, never the call site's signature: that mistake is recorded two rails above.</para>
    /// </summary>
    [Fact]
    public void PerspectiveRegistrar_ForwardsLiveValueProvider_ToBlackboardAuthoring()
    {
        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName:   "Blueprint",
            selectionStore:    new EditorSelectionStore(),
            catalog:           new AssetCatalog(),
            refactorService:   StubRefactor(),
            debugRegistry:     new DebugSessionRegistry(),
            liveValueProvider: new StubLiveValues());

        Assert.True(reg.BlackboardAuthoring.HasLiveValueProvider,
            "the liveValueProvider passed to the registrar must reach BlackboardAuthoringWindow, "
            + "or the Value column has no source and renders '—' for every row");
    }

    /// <summary>⛔ Negative control. ⚠ <b>This is the state Blueprint actually shipped in</b> — not a
    /// hypothetical: <c>EditorSubsystem</c> passed a provider for BTree and HSM and none for Blueprint,
    /// so the column was sourceless on exactly one host.</summary>
    [Fact]
    public void PerspectiveRegistrar_WithoutLiveValueProvider_AuthoringWindowHasNone()
        => Assert.False(MakeRegistrar("Blueprint").BlackboardAuthoring.HasLiveValueProvider);
}
