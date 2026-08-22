using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-1</c> — the snapshot spine.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §APIs + §"Perf &amp; correctness".
///
/// <para>⚠ <b>ONE class on purpose.</b> <c>PanelSnapshot</c> is process-global static state and xunit runs
/// separate CLASSES in parallel ⇒ ⛔ splitting these across classes would make them flake against each
/// other. ⭐ Every case opens with <c>Clear()</c> and restores <c>CaptureEnabled</c>.</para>
/// </summary>
public class PanelSnapshotTests
{
    // ── A minimal view-model, standing in for a converted panel ────────────────────────────────

    private sealed class FakePanelVm : IPanelViewModel
    {
        public FakePanelVm(string panelId, string title = "", int count = 0)
        { PanelId = panelId; Title = title; Count = count; }

        public string PanelId { get; }
        public string Title   { get; }
        public int    Count   { get; }

        public JsonNode Dump() => PanelDump.Of(this);
    }

    private static void Reset()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ── U1a — the round trip ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ARegisteredModel_RoundTripsThroughDumpAll()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.Register(new FakePanelVm("alpha", "Alpha Panel", 3));

        var all = PanelSnapshot.DumpAll();
        Assert.True(all.ContainsKey("alpha"));
        Assert.Equal("Alpha Panel", all["alpha"]!["title"]!.GetValue<string>());
        Assert.Equal(3,             all["alpha"]!["count"]!.GetValue<int>());

        Reset();
    }

    [Fact]
    public void TryGet_ReturnsTheLatestModelForThatPanel()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.Register(new FakePanelVm("alpha", "first"));
        PanelSnapshot.Register(new FakePanelVm("alpha", "second"));

        var vm = Assert.IsType<FakePanelVm>(PanelSnapshot.TryGet("alpha"));
        Assert.Equal("second", vm.Title);

        Reset();
    }

    /// <summary>⭐ camelCase, because the design's §Example payload and the rest of the MCP surface are.</summary>
    [Fact]
    public void TheDefaultDump_IsCamelCasedOverTheViewModelsOwnShape()
    {
        var json = new FakePanelVm("alpha", "Alpha", 7).Dump();

        Assert.Equal("alpha", json["panelId"]!.GetValue<string>());
        Assert.Equal("Alpha", json["title"]!.GetValue<string>());
        Assert.Equal(7,       json["count"]!.GetValue<int>());
    }

    // ── U1b — the OPT-IN REGISTRY: instrumented vs merely-empty ────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The false-green guard, stated as the design states it.</b>
    /// ⛔ An un-instrumented panel must be <b>ABSENT</b> — ⚠ not an empty model, which a reader would take
    /// for <i>"the panel showed nothing"</i>.
    /// </summary>
    [Fact]
    public void AnUninstrumentedPanel_IsAbsentRatherThanEmpty()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.Register(new FakePanelVm("alpha"));

        Assert.Null(PanelSnapshot.TryGet("never-converted"));
        Assert.DoesNotContain("never-converted", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("never-converted", PanelSnapshot.CapturedPanels);
        Assert.False(PanelSnapshot.DumpAll().ContainsKey("never-converted"));

        Reset();
    }

    /// <summary>
    /// ⭐⭐ <b>The OTHER half, and it is what makes the first half meaningful:</b> a panel that IS converted
    /// but drew an empty model is <b>present</b> with an empty model. ⇒ ⛔ the two states are distinguishable,
    /// which is the entire content of <c>U1b</c>.
    /// </summary>
    [Fact]
    public void AnInstrumentedPanelThatDrewNothing_IsPresentWithAnEmptyModel()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.Register(new FakePanelVm("beta"));   // no title, no rows — an EMPTY model

        Assert.NotNull(PanelSnapshot.TryGet("beta"));
        Assert.Contains("beta", PanelSnapshot.RegisteredPanels);
        Assert.Equal(string.Empty, PanelSnapshot.DumpAll()["beta"]!["title"]!.GetValue<string>());

        Reset();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The case that decides where <c>DeclareInstrumented</c> is called.</b> 📐 A panel whose window
    /// is never opened NEVER DRAWS ⇒ never registers. ⛔ If instrumentation were declared by drawing, it
    /// would be indistinguishable from a panel nobody converted. ⇒ ⭐ declared at CONSTRUCTION, always.
    /// </summary>
    [Fact]
    public void APanelThatNeverDrew_IsStillReportedAsInstrumented()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.DeclareInstrumented("gamma");        // constructed…
                                                           // …and never drawn.

        Assert.Contains("gamma", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("gamma", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("gamma"));

        Reset();
    }

    // ── The flag gates the DUMP, not the BUILD ─────────────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_NothingIsCaptured_ButInstrumentationIsStillKnown()
    {
        Reset();                                            // CaptureEnabled == false

        PanelSnapshot.DeclareInstrumented("delta");
        PanelSnapshot.Register(new FakePanelVm("delta", "drawn anyway"));

        Assert.Contains("delta", PanelSnapshot.RegisteredPanels);
        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("delta"));
        Assert.Empty(PanelSnapshot.DumpAll());

        Reset();
    }

    // ── Contract guards ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AModelWithoutAPanelId_IsRefusedRatherThanStoredUnderAnEmptyKey()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        Assert.Throws<System.ArgumentException>(() => PanelSnapshot.Register(new FakePanelVm("")));
        Assert.Throws<System.ArgumentNullException>(() => PanelSnapshot.Register(null!));
        Assert.Throws<System.ArgumentException>(() => PanelSnapshot.DeclareInstrumented("  "));

        Reset();
    }

    /// <summary>
    /// ⚠⚠ <b>A TRIPWIRE ON A KNOWN LIMIT, not an endorsement of it.</b> 📐 There is no frame boundary in
    /// <c>PanelSnapshot</c> — clearing per frame needs a call site in the frame loop *(<c>EditorSubsystem</c>)*,
    /// which this lane must not touch. ⇒ ⛔ a panel that stops drawing leaves its LAST model visible.
    /// ⭐ <b>This rail pins that as the current behaviour</b>: when someone adds a frame boundary it goes RED,
    /// and the limit gets removed from the design at the same moment the code changes.
    /// </summary>
    [Fact]
    public void WithNoFrameBoundary_AModelSurvivesUntilOverwritten()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.Register(new FakePanelVm("epsilon", "frame one"));
        // …the panel's window closes; no further Register calls arrive.

        var stale = Assert.IsType<FakePanelVm>(PanelSnapshot.TryGet("epsilon"));
        Assert.Equal("frame one", stale.Title);
        Assert.Single(PanelSnapshot.CapturedPanels.Where(id => id == "epsilon"));

        Reset();
    }
}
