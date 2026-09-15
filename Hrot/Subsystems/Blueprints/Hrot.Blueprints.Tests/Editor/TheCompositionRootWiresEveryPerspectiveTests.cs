using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 84 item 0 — the two wiring defects the user's visual check found, and the rail shape
/// that can SEE them.</b>
///
/// <para>🔴🔴 <b><c>R-67</c>, the FOURTH instance — and the reason this file exists rather than another
/// registrar test.</b> 📌 Verbatim: <i>"a rail that builds its own composition root cannot see a
/// composition-root defect."</i> ⛔ Batch 83's dialog rails were ALL GREEN while the production dialog
/// did nothing, because each rail constructed its own registrar and passed <c>facetEditService</c>
/// itself. ⇒ ⭐ <b>every assertion below is made on an object the REAL
/// <see cref="EditorSubsystem"/> built.</b></para>
///
/// <para>📐 <b>The two defects, measured <c>2026-08-18</c>:</b>
/// <list type="number">
///   <item>🔴 <b><c>R-67</c>:</b> <c>facetEditService</c> was passed to the BTree registrar
///   (<c>EditorSubsystem:2134</c>) and the HSM one (<c>:2158</c>) and <b>OMITTED from the Blueprint
///   one</b> (<c>:2162</c>) ⇒ <c>EditGestures</c> null ⇒ "Edit value…" and "Properties…" dead on the
///   perspective the user was looking at.</item>
///   <item>🔴 <b><c>R-66</c>:</b> run state came from <c>IDebugSessionRegistry.ActiveSession</c>, which
///   <c>SyncActiveDebugSession</c> sets from the ACTIVE DOCUMENT'S KIND ⇒ opening any blueprint read as
///   <c>Running</c> ⇒ every unwritten row rendered <c>(pending)</c> forever and the INITIAL arm was
///   <b>unreachable in production</b>.</item>
/// </list></para>
///
/// <para>⭐⭐ <b>The fix is structural, not another passed argument.</b>
/// <see cref="PerspectiveWorkspaceServices"/> holds the shared services once and REQUIRES the three
/// that were being dropped, so ⛔ the omission is no longer a thing a caller can express. ⚠ A rail can
/// only catch a defect that is expressible — 📌 that is why the last three fixes of this shape did not
/// stop the fourth.</para>
/// </summary>
public sealed class TheCompositionRootWiresEveryPerspectiveTests
{
    private static WindowManager MakeWindowManager()
        => new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

    /// <summary>⭐ The variable table the REAL editor registered for a perspective.</summary>
    private static AiVariablesWindow TableOf(string suffix)
        => TableOf(suffix, out _);

    /// <summary>⭐ …and the subsystem that built it, for rails that must drive its state.</summary>
    private static AiVariablesWindow TableOf(string suffix, out EditorSubsystem editor)
    {
        var wm = MakeWindowManager();
        editor = new EditorSubsystem();
        editor.RegisterWindows(wm);

        Assert.True(wm.TryGetWindow($"ai_variable_values_{suffix}", out var win),
            $"Expected 'ai_variable_values_{suffix}' to be registered by the real EditorSubsystem.");
        return Assert.IsType<AiVariablesWindow>(win);
    }

    // ══ R-67 — the gestures reach EVERY perspective ══════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before Batch 84 on <c>blueprint</c></b> — and GREEN on the other two, which is
    /// exactly the shape a per-call-site argument list produces.
    ///
    /// <para>⭐ Asks the WINDOW whether its gestures are attached. ⛔ Not <c>registrar.EditGestures</c>:
    /// a registrar the test built would answer for the test's own wiring.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void EveryPerspectivesVariableTable_HasItsEditGestures(string suffix)
        => Assert.True(TableOf(suffix).HasEditGestures,
            $"The '{suffix}' perspective's variable table has no edit gestures attached, so " +
            "\"Edit value…\" and \"Properties…\" do nothing. This is R-67: the composition root " +
            "holds the edit service and did not pass it to every perspective.");

    // ══ R-66 — the run state is a CLOCK, not an open document ════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before Batch 84.</b> With no simulation running, every variable surface must read
    /// <c>Planning</c> — that is the state in which ruling 3's INITIAL arm renders and in which
    /// <c>VariableEditCommit</c> writes the declared default.
    ///
    /// <para>⛔ Before the fix, run state was derived from <c>ActiveSession</c>, which means "a document
    /// is open"; the editor here has no sim at all and would still have claimed <c>Running</c> the
    /// moment a blueprint was opened.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void WithNoSimulationRunning_EveryPerspectiveReadsPlanning(string suffix)
    {
        var table = TableOf(suffix);
        table.SyncRunState();

        Assert.Equal(VariableRunState.Planning, table.RunState);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail for <c>R-66</c> — and the one the obvious version could not be.</b>
    ///
    /// <para>⚠⚠ <b>Found by running the anti-vacuity probe, not by writing the test.</b> The theory
    /// above passes an editor with NO document open, where <c>ActiveSession</c> is null anyway — so
    /// reverting the fix left it GREEN. ⛔ It asserts the right thing about the wrong state.</para>
    ///
    /// <para>⭐ This one puts the editor in the state <c>R-66</c> actually describes: <b>a session
    /// active</b> — which is all "a blueprint document is open" ever meant — <b>and the simulation
    /// down</b>. 📌 That is the combination that made the INITIAL arm unreachable in production, and it
    /// is the only combination that can tell the two premises apart.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void WithADocumentOpenButTheSimDown_TheRunStateIsStillPlanning(string suffix)
    {
        var table = TableOf(suffix, out var editor);

        var registry = editor.AiDebugRegistry;
        Assert.NotNull(registry);
        registry!.SetActiveSession(new SimDownSession());
        Assert.NotNull(registry.ActiveSession);      // ⭐ the premise the old code read as "Running"

        table.SyncRunState();

        Assert.Equal(VariableRunState.Planning, table.RunState);
    }

    /// <summary>
    /// ⭐ An attached session and nothing else. ⛔ Nothing here needs breakpoints — only
    /// <c>ActiveSession</c>'s PRESENCE is the point, because presence is exactly what the old premise
    /// mistook for a running simulation.
    /// </summary>
    private sealed class SimDownSession : Hrot.Editor.AiShared.Debug.IAiDebugSession
    {
        public bool IsAttached { get; private set; } = true;
        public bool IsAnyBreakpointActive => false;
        public bool IsPaused => false;
        public Hrot.Editor.AiShared.Debug.Breakpoint? PausedAt => null;
        public Fdp.Core.Entity? PausedOnEntity => null;
        public event Action? OnSessionStateChanged { add { } remove { } }
        public void Attach(Guid assetId) { IsAttached = true; }
        public void Detach() { IsAttached = false; }
        public Hrot.Editor.AiShared.Debug.BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
        public void ClearBreakpoint(Hrot.Editor.AiShared.Debug.BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Hrot.Editor.AiShared.Debug.Breakpoint> GetBreakpoints()
            => Array.Empty<Hrot.Editor.AiShared.Debug.Breakpoint>();
        public void Continue() { }
        public void Pause() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }
        public void BeginObservingAsset(Guid assetId, Hrot.Editor.AiShared.Debug.TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public IReadOnlyList<Fdp.Core.Entity> GetActiveEntities(Guid assetId)
            => Array.Empty<Fdp.Core.Entity>();
    }

    /// <summary>
    /// ⭐⭐ <b>And the source is INSTALLED, not merely defaulted.</b> ⚠ Without this, the assertion above
    /// would also pass on a window whose run state nobody ever set — 📌 Batch 79's exact failure, where
    /// a settable <c>RunState</c> sat at its <c>Planning</c> default and looked correct.
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void EveryPerspectivesVariableTable_HasARunStateSource(string suffix)
        => Assert.True(TableOf(suffix).HasRunStateSource);

    // ══ the structural half — the omission is inexpressible ══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The service bundle REFUSES to be built without the edit service.</b>
    ///
    /// <para>⛔ This is the half a rail cannot provide. Batches 80, 82 and 83 each fixed one instance by
    /// passing one more argument, and the fourth instance happened anyway — because "forgot to pass it"
    /// stayed expressible. ⭐ Now it throws at the composition root, before any window exists.</para>
    /// </summary>
    [Fact]
    public void TheServicesBundle_RefusesAMissingEditService()
        => Assert.Throws<ArgumentNullException>(() => new PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            new NoRefactor(),
            new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(),
            facetEditService: null!,
            isSimUp:  () => false,
            isFrozen: () => false));

    /// <summary>
    /// ⭐⭐ <b>And without either clock signal</b> — 📌 <c>R-66</c>. ⚠ The old code made these OPTIONAL
    /// and defaulted them to a signal that was always present, which is why the defect read as a
    /// working feature for four batches.
    /// </summary>
    [Fact]
    public void TheServicesBundle_RefusesAMissingClock()
    {
        var edit = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => new PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(), new NoRefactor(),
            new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(), edit,
            isSimUp: null!, isFrozen: () => false));

        Assert.Throws<ArgumentNullException>(() => new PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(), new NoRefactor(),
            new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(), edit,
            isSimUp: () => false, isFrozen: null!));
    }

    /// <summary>
    /// ⭐⭐ <b>Every registrar the bundle makes carries the SAME shared services</b> — that is the
    /// property "one construction path" buys, and it is what three hand-written argument lists could
    /// not give however carefully they were reviewed.
    /// </summary>
    [Fact]
    public void EveryRegistrarFromOneBundle_GetsTheSharedServices()
    {
        var services = new PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            new NoRefactor(),
            new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp:  () => true,
            isFrozen: () => false);

        foreach (var name in new[] { "BTree", "HSM", "Blueprint" })
        {
            var reg = services.CreateRegistrar(
                name,
                new Hrot.Editor.AiShared.Selection.EditorSelectionStore(),
                validators: Array.Empty<Hrot.Editor.AiShared.Validation.IAssetValidator>());

            Assert.True(reg.Variables.HasEditGestures, $"{name} lost the edit service.");

            reg.Variables.SyncRunState();
            Assert.Equal(VariableRunState.Running, reg.Variables.RunState);
        }
    }

    /// <summary>⭐ Minimal refactor service — nothing here exercises it.</summary>
    private sealed class NoRefactor : Hrot.Editor.AiShared.Refactor.IRefactorService
    {
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public Hrot.Editor.AiShared.Refactor.RefactorPreview PreviewRename(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o)
            => new(f, t, Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorFileEdit>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyRename(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public Hrot.Editor.AiShared.Refactor.DeletePreview PreviewDelete(
            Guid id, Hrot.Editor.AiShared.Refactor.DeleteOptions o)
            => new(id, Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyDelete(
            Hrot.Editor.AiShared.Refactor.DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorPreview> PreviewRenameAsync(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o,
            System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorResult> ApplyRenameAsync(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
