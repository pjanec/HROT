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
[Collection(PanelSnapshotTestCollection.Name)]
public class PanelSnapshotTests
{
    // ── A minimal view-model, standing in for a converted panel ────────────────────────────────

    private sealed class FakePanelVm : IPanelViewModel
    {
        public FakePanelVm(string panelId, string title = "", int count = 0, string? kind = null)
        { PanelId = panelId; PanelKind = kind ?? panelId; Title = title; Count = count; }

        public string PanelId   { get; }
        public string PanelKind { get; }
        public string Title     { get; }
        public int    Count     { get; }

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

    // ── THE ADDRESS / KIND SPLIT ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>THE CASE THAT FORCED TWO FIELDS.</b> 🔒 <b>User, <c>2026-08-22</c>:</b> <i>"how will the MCP
    /// server know what the panel id to ask for if the panel does not have unique id no matter what model
    /// it is showing?"</i>
    ///
    /// <para>📐 Three watches are live at once — one per perspective. ⛔ With a single shared id they would
    /// <b>overwrite one another</b> in the snapshot and <c>GET /panels/watch</c> would be ambiguous.
    /// ⭐ With distinct ADDRESSES they stay individually reachable, ⭐⭐ and the shared KIND still groups
    /// them for a conformance diff — <b>which is the whole reason the two cannot be one field.</b></para>
    /// </summary>
    [Fact]
    public void ThreeLivePanelsOfOneKind_StayIndividuallyAddressable()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        PanelSnapshot.Register(new FakePanelVm("ai_watch_btree",     "BTree watch",     kind: "watch"));
        PanelSnapshot.Register(new FakePanelVm("ai_watch_hsm",       "HSM watch",       kind: "watch"));
        PanelSnapshot.Register(new FakePanelVm("ai_watch_blueprint", "Blueprint watch", kind: "watch"));

        // ⛔ NOT clobbered — the defect a shared id would have caused.
        Assert.Equal(3, PanelSnapshot.CapturedPanels.Count);
        Assert.Equal("HSM watch", ((FakePanelVm)PanelSnapshot.TryGet("ai_watch_hsm")!).Title);

        // ⭐⭐ …and they are still comparable AS a group, deterministically ordered.
        Assert.Equal(
            new[] { "ai_watch_blueprint", "ai_watch_btree", "ai_watch_hsm" },
            PanelSnapshot.PanelsOfKind("watch"));

        Assert.Empty(PanelSnapshot.PanelsOfKind("variables"));   // ⛔ anti-vacuity
        Reset();
    }

    /// <summary>⭐ A singleton carries the same string in both roles — ⚠ which is exactly why the missing
    /// split was invisible on the pilot, and worth pinning so nobody "simplifies" it back.</summary>
    [Fact]
    public void ASingletonPanel_CarriesTheSameStringInBothRoles()
    {
        var vm = new FakePanelVm("entity-blueprints");
        Assert.Equal(vm.PanelId, vm.PanelKind);

        var json = vm.Dump();
        Assert.Equal("entity-blueprints", json["panelId"]!.GetValue<string>());
        Assert.Equal("entity-blueprints", json["panelKind"]!.GetValue<string>());
    }

    [Fact]
    public void AModelWithoutAPanelId_IsRefusedRatherThanStoredUnderAnEmptyKey()
    {
        Reset();
        PanelSnapshot.CaptureEnabled = true;

        Assert.Throws<System.ArgumentException>(() => PanelSnapshot.Register(new FakePanelVm("")));
        Assert.Throws<System.ArgumentNullException>(() => PanelSnapshot.Register(null!));
        Assert.Throws<System.ArgumentException>(() => PanelSnapshot.DeclareInstrumented("  "));

        // ⭐ A model with an address but NO KIND is refused too — ⛔ it would be unreachable to every
        //   conformance query while looking perfectly healthy in the dump.
        Assert.Throws<System.ArgumentException>(() => PanelSnapshot.Register(new FakePanelVm("addr", kind: " ")));

        Reset();
    }

    /// <summary>
    /// ⚠⚠ <b>THE DEFAULT IS STILL LATEST-WINS — and after <c>MX-006</c> that is a CHOICE, not a limit.</b>
    ///
    /// <para>⭐ <b>Superseded premise, kept visible:</b> this rail was written saying <i>"clearing per frame
    /// needs a call site in the frame loop (EditorSubsystem), which this lane must not touch"</i>. ⛔ That is
    /// no longer true — the frame boundary shipped as <see cref="PanelSnapshot.ClearCaptured"/> and the
    /// editor calls it. ⇒ ⚠ the rail did NOT go red, because nothing calls it IMPLICITLY.</para>
    ///
    /// <para>⭐⭐ <b>And that is deliberate.</b> A host that never calls <c>ClearCaptured</c> keeps the old
    /// behaviour rather than silently losing models — ⛔ so this case pins the DEFAULT, and
    /// <c>ClearCaptured_DropsLastFramesModels_ButKeepsTheInstrumentedSet</c> pins the boundary.</para>
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

    // ── MX-006 — the frame boundary ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>MX-006</c> — a panel that did not draw THIS frame is absent from
    /// <see cref="PanelSnapshot.CapturedPanels"/>.</b> 📄 The time lane measured the ghost over
    /// <c>GET /panels</c> (<c>HN-122</c>): a closed window kept reporting its last model forever.
    ///
    /// <para>⛔⛔ <b>The second assertion is the whole reason this is not just <c>Clear()</c>.</b>
    /// <c>RegisteredPanels</c> must SURVIVE — it is declared once at construction, so a per-frame
    /// <c>Clear()</c> would empty it permanently after frame one and collapse the two sets the opt-in
    /// registry exists to keep apart.</para>
    /// </summary>
    [Fact]
    public void ClearCaptured_DropsLastFramesModels_ButKeepsTheInstrumentedSet()
    {
        Reset();
        PanelSnapshot.DeclareInstrumented("zeta");
        PanelSnapshot.DeclareInstrumented("eta");
        PanelSnapshot.CaptureEnabled = true;

        // Frame one: both draw.
        PanelSnapshot.Register(new FakePanelVm("zeta", "frame one"));
        PanelSnapshot.Register(new FakePanelVm("eta",  "frame one"));
        Assert.Equal(2, PanelSnapshot.CapturedPanels.Count);

        // Frame two: eta's window is closed, so only zeta draws.
        PanelSnapshot.ClearCaptured();
        PanelSnapshot.Register(new FakePanelVm("zeta", "frame two"));

        Assert.Contains("zeta", PanelSnapshot.CapturedPanels);
        Assert.DoesNotContain("eta", PanelSnapshot.CapturedPanels);   // ⭐ the ghost is gone
        Assert.Null(PanelSnapshot.TryGet("eta"));

        // ⛔ …and BOTH are still instrumented. This is what Clear() would have destroyed.
        Assert.Contains("zeta", PanelSnapshot.RegisteredPanels);
        Assert.Contains("eta",  PanelSnapshot.RegisteredPanels);

        Reset();
    }

    /// <summary>⚠ Anti-vacuity for the rail above: <c>Clear()</c> DOES drop the instrumented set, so the
    /// two methods are genuinely different and the distinction is not decorative.</summary>
    [Fact]
    public void Clear_UnlikeClearCaptured_AlsoDropsTheInstrumentedSet()
    {
        Reset();
        PanelSnapshot.DeclareInstrumented("theta");
        Assert.Contains("theta", PanelSnapshot.RegisteredPanels);

        PanelSnapshot.ClearCaptured();
        Assert.Contains("theta", PanelSnapshot.RegisteredPanels);

        PanelSnapshot.Clear();
        Assert.DoesNotContain("theta", PanelSnapshot.RegisteredPanels);

        Reset();
    }
}
