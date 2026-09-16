using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>DiagnosticsWindow</c> (AiShared) converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> IN-FLIGHT trio.
///
/// <para>⭐⭐ Mirrors <c>ThePilotPanelDumpsWhatItDrawsTests</c> — same BUILD/CAPTURE shape, same headless
/// rationale: <see cref="DiagnosticsWindow.Collect"/> was already pure, so capture requires no ImGui at
/// all. ⚠ <c>PanelSnapshot</c> is process-global static state; every case resets it.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class DiagnosticsWindowDumpsItsIssuesTests : IDisposable
{
    public DiagnosticsWindowDumpsItsIssuesTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DiagnosticsWindow MakeWindow(string id, Func<IReadOnlyList<AssetDiagnostic>>? schema = null)
        => new(new AssetCatalog(), Array.Empty<IAssetValidator>(), idOverride: id, schemaDiagnostics: schema);

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_diagnostics_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheSchemaDiagnostic()
    {
        const string id = "ai_diagnostics_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var diag = new AssetDiagnostic(Guid.Empty, "—", AssetDiagnosticSeverity.Warning, "SHORTNAME", "collision on Foo");
        var window = MakeWindow(id, () => new[] { diag });

        window.SimulateDrawContent();   // ⭐ no ImGui context — headless on purpose

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(DiagnosticsWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal(1, dump["totalCount"]!.GetValue<int>());
        var rows = dump["diagnostics"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("Warning",    rows[0]!["severity"]!.GetValue<string>());
        Assert.Equal("SHORTNAME",  rows[0]!["code"]!.GetValue<string>());
        Assert.Equal("collision on Foo", rows[0]!["message"]!.GetValue<string>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "ai_diagnostics_rail3";
        var diag = new AssetDiagnostic(Guid.Empty, "—", AssetDiagnosticSeverity.Error, "X", "y");
        var window = MakeWindow(id, () => new[] { diag });   // CaptureEnabled stays false

        var vm = window.SimulateDrawContent();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);            // ⭐ the BUILD is unaffected by the flag
        Assert.Equal(1, vm.TotalCount);
    }
}
