using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ THE EQUIVALENCE RAIL for phase 2 slice ② — <see cref="DiagnosticsWindowsBundle"/>.
///
/// <para>🔒 <b>Why it exists the day the bundle does.</b> <c>CE-072</c>'s lesson: <i>when a wrapper
/// becomes the only production path to tested code, the existing tests stop covering production</i>.
/// ⭐ 20 hand-written call sites across four hosts became one bundle; ⛔ if it emits one id, title,
/// perspective or colour differently, a user's saved layout resets and nothing else would say so.</para>
///
/// <para>⭐⭐ <b>Every expectation below is a LITERAL copied out of the pre-change host</b> — the exact
/// strings <c>IgSubsystem</c>, <c>SimHostSubsystem</c>, <c>CgfSubsystem</c> and <c>EditorSubsystem</c>
/// passed by hand before this slice. ⛔ NOT <c>DiagnosticsWindowsBundle.InspectorId(prefix)</c> compared
/// against itself, which would pass no matter what the scheme became.</para>
///
/// <para>⚠ The measured DRIFTS are asserted — <c>G1</c> (the editor's kernel guard) as a drift that is
/// PRESERVED, <c>G2</c> as one that was RESOLVED by the user (<c>CE-083</c>: one colour per subsystem),
/// and <c>G3</c> by omission. 📄 Design
/// <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.7.2 / §5c.7.5 item ④.</para>
/// </summary>
/// <remarks>
/// ⛔⛔ <b>The <c>[Collection]</c> attribute is REQUIRED, and leaving it off crashed the test host.</b>
/// 📐 Measured: this class alone passed 9/9 and the rest of the assembly passed 140/140, but TOGETHER the
/// host died — because registering real windows touches the <b>process-global <c>PanelSnapshot</c>
/// singleton</b>, and this class was running in parallel with the four classes that serialise on it.
/// ⇒ ⭐ the convention already existed *(<see cref="PanelSnapshotTestCollection"/>, mirrored in two other
/// assemblies)* and this rail was written without it. ⚠ Worth recording: a green filtered run is NOT
/// evidence a new test class is safe in its assembly.
/// </remarks>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheDiagnosticsBundleEmitsWhatEachHostEmittedTests
{
    private static WindowManager NewWm()
        => new WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 512, 512));

    /// <summary>
    /// Composes the bundle over a real <see cref="WindowManager"/>, exactly as a host does.
    /// ⚠ Through <see cref="UiBundleHost.Compose"/>, not by calling <c>RegisterInto</c> directly — the
    /// composition path is part of what this asserts.
    /// </summary>
    private static WindowManager Compose(DiagnosticsHostServices services)
    {
        var wm = NewWm();
        UiBundleHost.Compose(
            new IUiBundle[] { new DiagnosticsWindowsBundle(services) },
            new UiBundleContext(wm));
        return wm;
    }

    /// <summary>
    /// ⚠ The panel REJECTS a null service, so the stub is real rather than <c>null!</c>. ⭐ Worth saying:
    /// the first draft of this rail passed <c>null!</c> and every case threw in the fixture — which is a
    /// fixture bug that looks exactly like a product failure.
    /// </summary>
    private sealed class StubArchitectureService : Fdp.ModuleHost.Diagnostics.IArchitectureDiagnosticsService
    {
        public Fdp.ModuleHost.Diagnostics.ArchitectureSnapshotDto GetSnapshot() => new();
    }

    private static ArchitectureDiagnosticsPanel Panel()
        => new ArchitectureDiagnosticsPanel(new StubArchitectureService());

    private static DiagnosticsHostServices Services(
        string idPrefix, string titlePrefix, string perspective, Vector4 color,
        bool withKernel = true)
        => new DiagnosticsHostServices(
            IdPrefix:       idPrefix,
            TitlePrefix:    titlePrefix,
            Perspective:    perspective,
            Inspector:      new EntityInspectorPanel(),
            RepoAdapter:    () => null,
            InspectorState: () => new Fdp.Presentation.Abstractions.InspectorState(),
            EventBrowser:   new EventBrowserPanel(),
            TitleBarColor:  color,
            ArchitecturePanel: withKernel ? Panel() : null,
            ExecutionStats:    withKernel ? () => null : null,
            PickBridge:        null);

    private static (string Title, string Perspective, Vector4? Color) Get(WindowManager wm, string id)
    {
        Assert.True(wm.TryGetWindow(id, out var w),
            $"no window registered with id '{id}' — the bundle changed an id a host had already shipped, "
          + "which resets every user's saved layout for that window.");
        return (w!.Title, w.OwningPerspective, w.TitleBarColor);
    }

    // ── the four hosts, against the literals they used to pass by hand ────────────

    /// <summary>
    /// ⭐⭐⭐ IG — the four ids/titles verbatim from <c>IgSubsystem</c> before this slice.
    /// </summary>
    [Fact]
    public void IG_gets_exactly_the_ids_titles_and_perspective_it_had_before()
    {
        var igWindows = new Vector4(0.07f, 0.30f, 0.07f, 1f);   // IgWindowColor.TitleBar
        var wm = Compose(Services("ig_", "IG", "IG", igWindows));

        Assert.Equal(("IG Entity Inspector",          "IG", igWindows), Get(wm, "ig_fdp_inspector"));
        Assert.Equal(("IG Event Browser",             "IG", igWindows), Get(wm, "ig_fdp_events"));
        Assert.Equal(("IG Architecture Diagnostics",  "IG", igWindows), Get(wm, "ig_architecture_diagnostics"));
        Assert.Equal(("IG System Profiler",           "IG", igWindows), Get(wm, "ig_system_profiler"));
    }

    /// <summary>⭐⭐ SimHost — same, from <c>SimHostSubsystem</c>.</summary>
    [Fact]
    public void SimHost_gets_exactly_the_ids_titles_and_perspective_it_had_before()
    {
        var shWindows = new Vector4(0.50f, 0.10f, 0.10f, 1f);   // SimHostWindowColor.TitleBar
        var wm = Compose(Services("simhost_", "SimHost", "SimHost", shWindows));

        Assert.Equal(("SimHost Entity Inspector",         "SimHost", shWindows), Get(wm, "simhost_fdp_inspector"));
        Assert.Equal(("SimHost Event Browser",            "SimHost", shWindows), Get(wm, "simhost_fdp_events"));
        Assert.Equal(("SimHost Architecture Diagnostics", "SimHost", shWindows), Get(wm, "simhost_architecture_diagnostics"));
        Assert.Equal(("SimHost System Profiler",          "SimHost", shWindows), Get(wm, "simhost_system_profiler"));
    }

    /// <summary>
    /// ⭐⭐ CGF — ⚠ note the PERSPECTIVE is <c>"Scenario"</c>, not <c>"CGF"</c>, while the id and title
    /// prefixes are <c>cgf_</c>/<c>CGF</c>. 📌 That asymmetry is real and shipped; a bundle that derived
    /// the perspective from the title prefix would break it.
    /// </summary>
    [Fact]
    public void CGF_keeps_the_cgf_ids_but_the_Scenario_perspective()
    {
        var c = new Vector4(0.20f, 0.20f, 0.45f, 1f);
        var wm = Compose(Services("cgf_", "CGF", "Scenario", c));

        Assert.Equal(("CGF Entity Inspector",         "Scenario", c), Get(wm, "cgf_fdp_inspector"));
        Assert.Equal(("CGF Event Browser",            "Scenario", c), Get(wm, "cgf_fdp_events"));
        Assert.Equal(("CGF Architecture Diagnostics", "Scenario", c), Get(wm, "cgf_architecture_diagnostics"));
        Assert.Equal(("CGF System Profiler",          "Scenario", c), Get(wm, "cgf_system_profiler"));
    }

    /// <summary>⭐⭐ Editor — the same asymmetry, and the ids the golden pins.</summary>
    [Fact]
    public void Editor_keeps_the_editor_ids_and_the_Scenario_perspective()
    {
        var c = new Vector4(0.15f, 0.15f, 0.35f, 1f);
        var wm = Compose(Services("editor_", "Editor", "Scenario", c));

        Assert.Equal(("Editor Entity Inspector",         "Scenario", c), Get(wm, "editor_fdp_inspector"));
        Assert.Equal(("Editor Event Browser",            "Scenario", c), Get(wm, "editor_fdp_events"));
        Assert.Equal(("Editor Architecture Diagnostics", "Scenario", c), Get(wm, "editor_architecture_diagnostics"));
        Assert.Equal(("Editor System Profiler",          "Scenario", c), Get(wm, "editor_system_profiler"));
    }

    // ── G1: the editor's kernel guard ─────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>G1</c> — <b>a host with no kernel registers only TWO windows.</b>
    ///
    /// <para>📐 Before this slice the editor guarded its architecture + profiler windows on
    /// <c>if (_kernel != null)</c> while the other three hosts registered them unconditionally.
    /// ⛔ Unifying to the unconditional form would have added two windows to the editor's registered set
    /// and MOVED the <c>ui-baseline</c> golden. ⇒ ⭐ this rail is the guard, expressed as a test.</para>
    /// </summary>
    [Fact]
    public void A_host_with_no_kernel_registers_neither_architecture_nor_profiler()
    {
        var c  = new Vector4(0.1f, 0.1f, 0.1f, 1f);
        var wm = Compose(Services("editor_", "Editor", "Scenario", c, withKernel: false));

        // the two that never depended on a kernel are still there ...
        Assert.True(wm.TryGetWindow("editor_fdp_inspector", out _));
        Assert.True(wm.TryGetWindow("editor_fdp_events",    out _));

        // ... and the two that did are ABSENT, not present-and-empty (ruling 49).
        Assert.False(wm.TryGetWindow("editor_architecture_diagnostics", out _),
            "the architecture window was registered without a kernel — the editor's `if (_kernel != null)` "
          + "guard was lost, which grows its window set and moves the ui-baseline golden.");
        Assert.False(wm.TryGetWindow("editor_system_profiler", out _),
            "the system profiler was registered without execution stats — same lost guard.");
    }

    // ── G2, RESOLVED: one colour per subsystem ────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>G2</c>/<c>CE-083</c> — <b>every window this bundle registers carries the subsystem's ONE
    /// colour.</b>
    ///
    /// <para>🔒 User ruling `2026-08-27`: <i>"each subsystem still needs its own different titlebar color,
    /// for each its window"</i>. 📐 Before it, IG passed <c>(0.08,0.40,0.08)</c> to the "Inspect…" helper
    /// but <c>(0.07,0.30,0.07)</c> to its windows, so a spawned watch window did not match the window it
    /// came from. ⇒ ⭐ the record now carries ONE colour field and the helper gets the same value.</para>
    ///
    /// <para>⚠ The helper's colour is only observable on a window spawned by an "Inspect…" CLICK, which
    /// this rail does not simulate — ⛔ so this asserts the half that IS observable *(all four windows
    /// share one colour)*, and the other half is true **by construction**: the record has no second
    /// colour left to pass. 📌 Stated rather than over-claimed.</para>
    /// </summary>
    [Fact]
    public void Every_window_carries_the_one_subsystem_colour()
    {
        var subsystemColour = new Vector4(0.07f, 0.30f, 0.07f, 1f);   // IgWindowColor.TitleBar
        var wm = Compose(Services("ig_", "IG", "IG", subsystemColour));

        foreach (var id in new[] { "ig_fdp_inspector", "ig_fdp_events",
                                   "ig_architecture_diagnostics", "ig_system_profiler" })
            Assert.Equal(subsystemColour, Get(wm, id).Color);
    }

    // ── the scheme itself, and the seam ───────────────────────────────────────────

    /// <summary>
    /// ⭐ The four id helpers are the ONLY spelling of the scheme. ⚠ Asserted against literals, so a
    /// change to the scheme reddens here rather than silently in three hosts.
    /// </summary>
    [Fact]
    public void The_id_scheme_is_the_one_every_host_already_shipped()
    {
        Assert.Equal("ig_fdp_inspector",                  DiagnosticsWindowsBundle.InspectorId("ig_"));
        Assert.Equal("simhost_fdp_events",                DiagnosticsWindowsBundle.EventsId("simhost_"));
        Assert.Equal("cgf_architecture_diagnostics",      DiagnosticsWindowsBundle.ArchitectureId("cgf_"));
        Assert.Equal("editor_system_profiler",            DiagnosticsWindowsBundle.ProfilerId("editor_"));
    }

    /// <summary>
    /// ⭐ All four windows are <see cref="WindowScope.PerspectiveBound"/> — ⛔ not host-level/flat.
    /// 📌 <c>CE-070</c> deleted a registrar precisely because it registered perspective-bound windows
    /// flatly; this asserts the bundle did not reintroduce that shape.
    /// </summary>
    [Fact]
    public void Every_window_the_bundle_registers_is_perspective_bound()
    {
        var wm = Compose(Services("ig_", "IG", "IG", new Vector4(1, 1, 1, 1)));

        foreach (var id in new[] { "ig_fdp_inspector", "ig_fdp_events",
                                   "ig_architecture_diagnostics", "ig_system_profiler" })
        {
            Assert.True(wm.TryGetWindow(id, out var w));
            Assert.Equal(WindowScope.PerspectiveBound, w!.Scope);
        }
    }

    /// <summary>⭐ The bundle names itself, so a throwing composition names the FEATURE (phase 1's point).</summary>
    [Fact]
    public void The_bundle_is_named_so_a_failure_names_the_feature()
        => Assert.Equal("diagnostics-windows",
            new DiagnosticsWindowsBundle(Services("ig_", "IG", "IG", new Vector4(1, 1, 1, 1))).Name);
}
