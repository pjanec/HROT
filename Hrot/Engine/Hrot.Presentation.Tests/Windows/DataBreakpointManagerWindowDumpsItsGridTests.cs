using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Presentation.Panels.Breakpoints;
using Hrot.Presentation.Windows;
using Xunit;

using DpBreakpoint = Hrot.Diagnostics.Breakpoints.Breakpoint;
using DpBpId       = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>DataBreakpointManagerPanel</c>/<c>DataBreakpointManagerWindow</c> converted to
/// the <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 5. Mirrors
/// <c>AiBreakpointsWindowDumpsItsCountsTests</c>'s <c>StubBreakpointManager</c> shape (only the
/// non-default-implemented interface members).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class DataBreakpointManagerWindowDumpsItsGridTests : IDisposable
{
    public DataBreakpointManagerWindowDumpsItsGridTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class StubBreakpointManager : IDataBreakpointManager
    {
        public readonly List<DpBreakpoint> Bps = new();
        public IReadOnlyList<DpBreakpoint> AllBreakpoints => Bps;

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

    private static DataBreakpointManagerWindow MakeWindow(string id, StubBreakpointManager manager) =>
        new(id, "test-perspective", new DataBreakpointManagerPanel(manager, new TemporalStatusBannerState()));

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("data_bp_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow("data_bp_test", new StubBreakpointManager());

        Assert.Contains("data_bp_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("data_bp_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("data_bp_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheBreakpointRow()
    {
        PanelSnapshot.CaptureEnabled = true;
        var manager = new StubBreakpointManager();
        manager.Bps.Add(new DpBreakpoint { Id = DpBpId.Invalid, Enabled = true, HitCount = 3 });
        var window = MakeWindow("data_bp_test", manager);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("data_bp_test");
        Assert.NotNull(vm);
        Assert.Equal("data_bp_test", vm!.PanelId);
        Assert.Equal(DataBreakpointManagerWindow.Kind, vm.PanelKind);

        var rows = vm.Dump()["breakpoints"]!.AsArray();
        Assert.Single(rows);
        Assert.True(rows[0]!["enabled"]!.GetValue<bool>());
        Assert.Equal(3, rows[0]!["hitCount"]!.GetValue<int>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var manager = new StubBreakpointManager();
        manager.Bps.Add(new DpBreakpoint { Id = DpBpId.Invalid, Enabled = true });
        var window = MakeWindow("data_bp_test", manager);   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("data_bp_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.Single(vm.Breakpoints);
    }
}
