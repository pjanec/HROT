using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

// Alias to disambiguate between the two Breakpoint types in scope.
using DpBreakpoint  = Hrot.Diagnostics.Breakpoints.Breakpoint;
using DpBpId        = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// AIE-034 tests: per-perspective Watch, Breakpoints, and Diagnostics windows.
/// Verifies ids present, correct OwningPerspective, and shared manager reuse.
/// All tests are headless.
/// </summary>
public sealed class AiWatchBreakpointsWindowTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private WindowManager MakeWm() => new(_atlas);

    private static IRefactorService StubRefactor() => new StubRefactorService();

    private sealed class StubRefactorService : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o)
            => new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o)
            => new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }

    /// <summary>
    /// Minimal stub for IDataBreakpointManager providing only what the windows
    /// use (AllBreakpoints for drawing, identity for equality checks).
    /// </summary>
    private sealed class StubBreakpointManager : IDataBreakpointManager
    {
        private readonly List<DpBreakpoint> _bps = new();
        public IReadOnlyList<DpBreakpoint> AllBreakpoints => _bps;

        public bool IsPaused => false;
        public long PausedTick => 0;
        public int PendingMutationsCount => 0;
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public ISimulationView ActiveView => throw new NotImplementedException();

        public DpBpId Add(DpBreakpoint bp) => bp.Id;
        public DpBpId AddBreakpoint(SearchPredicateDto cond, Entity? filter = null,
            int thresh = 1, string name = "", Guid? srcId = null) => DpBpId.Invalid;
        public void Remove(DpBpId id) { }
        public void SetEnabled(DpBpId id, bool e) { }
        public void UpdateCondition(DpBpId id, SearchPredicateDto? c) { }
        public void MarkAsWatch(DpBpId id, bool w) { }
        public void SaveWatches(string p) { }
        public void LoadWatches(string p) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void StageMutation(Entity entity, Type componentType, object componentValue) { }
        public void OnHit(DpBreakpoint bp, Entity e) { }
        public void OnExternalHit(string tag, Entity e) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }

        public IReadOnlyList<(DpBreakpoint Breakpoint, CompiledComponentPredicate Compiled)>
            MountedComponentPredicates => Array.Empty<(DpBreakpoint, CompiledComponentPredicate)>();
        public IReadOnlyList<(DpBreakpoint Breakpoint, CompiledEventScanner Scanner)>
            MountedEventScanners => Array.Empty<(DpBreakpoint, CompiledEventScanner)>();

#pragma warning disable CS0067
        public event Action<DpBreakpoint, Entity>? OnBreakpointHit;
        public event Action<bool>? OnPauseStateChanged;
#pragma warning restore CS0067
    }

    private static PerspectiveWorkspaceRegistrar MakeRegistrar(
        string perspective, IDataBreakpointManager? manager = null) =>
        new(
            perspectiveName:   perspective,
            selectionStore:    new EditorSelectionStore(),
            catalog:           new AssetCatalog(),
            refactorService:   StubRefactor(),
            debugRegistry:     new DebugSessionRegistry(),
            breakpointManager: manager);

    // ── AIE-034 SC1: Perspective registers Watch + Breakpoints + Diagnostics ──

    [Fact]
    public void Perspective_RegistersWatchAndBreakpointsAndDiagnostics_WithOwningPerspective()
    {
        var wm      = MakeWm();
        var manager = new StubBreakpointManager();

        foreach (var perspective in new[] { "BTree", "HSM", "Blueprint" })
        {
            var reg = MakeRegistrar(perspective, manager);
            reg.RegisterWindows(wm);

            // All windows must carry the correct perspective.
            foreach (var w in reg.RegisteredWindows)
                Assert.Equal(perspective, w.OwningPerspective);

            // Diagnostics, Watch, Breakpoints must all be present.
            Assert.NotNull(reg.Diagnostics);
            Assert.Equal(perspective, reg.Diagnostics.OwningPerspective);

            Assert.NotNull(reg.Watch);
            Assert.NotNull(reg.Breakpoints);

            Assert.Equal(perspective, reg.Watch!.OwningPerspective);
            Assert.Equal(perspective, reg.Breakpoints!.OwningPerspective);

            Assert.Contains(reg.Diagnostics, reg.RegisteredWindows);
            Assert.Contains(reg.Watch,        reg.RegisteredWindows);
            Assert.Contains(reg.Breakpoints,  reg.RegisteredWindows);
        }
    }

    // ── AIE-034 SC2: Watch and Breakpoints share the same manager instance ────

    [Fact]
    public void Perspective_WatchAndBreakpoints_ShareSameManagerInstance()
    {
        var manager = new StubBreakpointManager();
        var reg     = MakeRegistrar("BTree", manager);

        Assert.NotNull(reg.Watch);
        Assert.NotNull(reg.Breakpoints);

        // Both windows must reference the SAME object — no duplication.
        Assert.Same(manager, reg.Watch!.Manager);
        Assert.Same(manager, reg.Breakpoints!.Manager);
    }

    // ── AIE-034 SC3: Without manager, Watch/Breakpoints are null ─────────────

    [Fact]
    public void Perspective_NoManager_WatchAndBreakpointsAreNull_NoThrow()
    {
        var wm  = MakeWm();
        var reg = MakeRegistrar("HSM", manager: null);

        // Must not throw.
        var ex = Record.Exception(() => reg.RegisterWindows(wm));
        Assert.Null(ex);

        Assert.Null(reg.Watch);
        Assert.Null(reg.Breakpoints);

        // ⚠ Batch 79 (Track C wiring): the Variables table is now a CORE window, registered by the
        //    registrar rather than left to the host — the whole point of that batch is that a surface
        //    the host must remember to attach is how five of them became unreachable. This registrar
        //    is built with no hostKind, so it gets no My Blueprint outline.
        Assert.Equal(7, reg.RegisteredWindows.Count);
    }

    // ── AIE-034 SC4: With manager, 8 windows per perspective (+1 in Batch 79) ─

    [Fact]
    public void Perspective_WithManager_RegistersEightWindows()
    {
        var wm      = MakeWm();
        var manager = new StubBreakpointManager();
        var reg     = MakeRegistrar("BTree", manager);
        reg.RegisterWindows(wm);

        // ⚠ 8 → 9: Batch 79 added the Variables table to the core set. ⭐ The name of this test is
        //    kept so the AIE-034 scenario stays traceable; the count is what moved, deliberately.
        Assert.Equal(9, reg.RegisteredWindows.Count);
    }

    // ── AIE-034 SC5: All ids are distinct across three perspectives ───────────

    [Fact]
    public void WatchAndBreakpointsWindowIds_AreDistinctAcrossPerspectives()
    {
        var wm      = MakeWm();
        var manager = new StubBreakpointManager();

        var btree     = MakeRegistrar("BTree",     manager);
        var hsm       = MakeRegistrar("HSM",       manager);
        var blueprint = MakeRegistrar("Blueprint", manager);

        btree.RegisterWindows(wm);
        hsm.RegisterWindows(wm);
        blueprint.RegisterWindows(wm);

        var allIds = btree.RegisteredWindows
            .Concat(hsm.RegisteredWindows)
            .Concat(blueprint.RegisteredWindows)
            .Select(w => w.Id)
            .ToList();

        // 3 × 8 = 24 distinct ids.
        // ⚠ 24 → 27: three perspectives × the Variables table Batch 79 added. ⭐ What this test is
        //    ABOUT is unchanged — every id is still distinct across perspectives, which is the
        //    property that would break if a new window forgot its perspective suffix.
        Assert.Equal(27, allIds.Count);
        Assert.Equal(27, allIds.Distinct().Count());
    }

    // ── AIE-034 SC6: Diagnostics window carries the correct id suffix ─────────

    [Fact]
    public void Diagnostics_WindowId_HasCorrectSuffix()
    {
        var reg = MakeRegistrar("BTree");
        Assert.Equal("ai_diagnostics_btree", reg.Diagnostics.Id);
        Assert.Equal("BTree", reg.Diagnostics.OwningPerspective);
    }

    // ── AIE-034 SC7: Watch/Breakpoints window ids contain the perspective suffix ──

    [Fact]
    public void WatchAndBreakpoints_WindowIds_ContainPerspectiveSuffix()
    {
        var manager = new StubBreakpointManager();
        var reg     = MakeRegistrar("HSM", manager);

        Assert.NotNull(reg.Watch);
        Assert.NotNull(reg.Breakpoints);

        Assert.Contains("hsm", reg.Watch!.Id,       StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hsm", reg.Breakpoints!.Id, StringComparison.OrdinalIgnoreCase);
    }
}
