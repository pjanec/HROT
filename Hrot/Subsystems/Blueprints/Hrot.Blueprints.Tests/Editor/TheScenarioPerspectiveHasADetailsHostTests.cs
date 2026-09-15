using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Fdp.Toolkit.Runner;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>L6.1c</c> — THE SCENARIO PERSPECTIVE HAS A DETAILS HOST.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 2 · §5 · the <c>L6</c> sequence.
///
/// <para>🔴 <b>What was measured before this</b> *(§6 <c>L6</c> as-built (b), <c>2026-08-22</c>)*:
/// <i>"the Scenario perspective has NO <c>PerspectiveWorkspaceRegistrar</c>, no <c>DetailsWindow</c>,
/// no registry — it uses a bespoke <c>RegisterPane</c>."</i> ⇒ ⭐ standing one up IS <c>L6</c>'s real
/// work, and it is only cheap because <c>L6.1a</c> split the generic half out of the registrar's
/// 21-parameter AI service bag.</para>
///
/// <para>⭐⭐ <b>The REAL <see cref="EditorSubsystem"/>, through <c>RegisterWindows</c></b> — 📌
/// <c>R-67</c>: <i>"a rail that builds its own composition root cannot see a composition-root
/// defect."</i> ⚠⚠ <b>And <c>RegisterWindows</c> specifically, measured:</b> <c>Initialize(headless)</c>
/// alone leaves the workspace <c>null</c>, because the perspective wiring lives in
/// <c>RegisterWindows(WindowManager)</c> — which the shell calls, not <c>Initialize</c>. ⛔ A rail
/// calling only <c>Initialize</c> would have asserted a null and looked like a defect in the wiring
/// rather than in itself.</para>
///
/// <para>⚠ <b>This suite is the home because it is GATED and already drives the production editor</b>
/// *(<c>TheSelectedEntityReachesEveryPerspectiveTests</c> does the same two lines)*. ⛔ The natural home
/// — <c>Hrot.ClusterRunner.Integration.Tests</c> — cannot be gated *(<c>BP-378</c>)*.</para>
/// </summary>
public sealed class TheScenarioPerspectiveHasADetailsHostTests
{
    /// <summary>⭐ <c>RegisterWindows</c> is what builds the perspective wiring — see the class
    /// remarks. ⚠ No <c>Initialize</c>: these rails need only the wiring, not a World.</summary>
    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    /// <summary>
    /// ⭐⭐ <b>The FULL production order: <c>Initialize</c> then <c>RegisterWindows</c>.</b>
    /// 📐 Measured, and it is why there are two helpers: <c>Initialize</c> builds the <b>World</b> and
    /// <c>RegisterWindows</c> builds the <b>perspective wiring</b> — ⛔ neither does the other's job, so
    /// a rail about a context flowing needs both.
    /// </summary>
    private static EditorSubsystem RealEditorWithWorld()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Scenario gets a workspace and a Details panel.</b>
    /// ⛔ Both halves: a workspace with no window is a catalogue nobody draws, and a window with no
    /// workspace has no context to draw FROM.
    /// </summary>
    [Fact]
    public void TheScenarioPerspective_HasAWorkspaceAndADetailsPanel()
    {
        var editor = RealEditor();

        Assert.NotNull(editor.ScenarioWorkspace);
        Assert.NotNull(editor.ScenarioDetails);
    }

    /// <summary>
    /// ⭐⭐ <b>It is bound to the SCENARIO perspective, under its PERSISTED key — which is now
    /// <c>"Scenario"</c>.</b> 📄 <c>DESIGN_Perspective_Unification.md</c> §3 <c>A1</c> · charter
    /// <c>D2</c>.
    ///
    /// <para>⭐⭐⭐ <b>INVERTED by <c>A1</c>, <c>2026-08-23</c>: this rail used to pin the DEFERRAL</b>
    /// *(<c>L6.1b</c>)* and named its reason — <i>"<c>CurrentPerspective</c> and every
    /// <c>OwningPerspective</c> are persisted and a bare rename silently resets saved layouts"</i>.
    /// ⛔⛔ <b>That reason was measurably FALSE.</b> 📐 <c>WindowManagerSettings</c> persists window
    /// <b>ids</b> with <c>IsOpen</c>/<c>IsPinned</c>, and <b>exactly one</b> perspective name —
    /// <c>ActivePerspective</c>; <c>WindowInternalName</c> is <c>$"{Title}###{Id}"</c>, so the ImGui ini
    /// holds none. ⇒ ⭐ one orphaned string, handled by <c>A0</c>'s validated restore.</para>
    ///
    /// <para>⚠ It still pins the same thing in the same way — ⭐ only the expected value moved, so a
    /// half-done rename reddens HERE rather than in a designer's blank window.</para>
    /// </summary>
    [Fact]
    public void TheScenarioHost_UsesTheScenarioKey()
    {
        var editor = RealEditor();

        Assert.Equal("Scenario", editor.ScenarioWorkspace!.PerspectiveName);
        Assert.Equal("Scenario", editor.ScenarioDetails!.OwningPerspective);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Its catalogue is REAL — the window contributed its own variables view through the claim
    /// chain.</b> 📄 §6 <c>L1.2</c> *(<c>R-67</c>)*: windows self-wire, so the composition root passes
    /// nothing extra. ⛔ An empty catalogue would mean the panel draws <c>R-117</c>'s grey line for
    /// ever, which looks like "nothing selected" rather than "nothing wired".
    /// </summary>
    [Fact]
    public void TheScenarioCatalogue_ReceivedTheWindowsOwnView()
        => Assert.Contains("details.variables",
                           RealEditor().ScenarioWorkspace!.DetailsViews.All.Select(d => d.Id));

    /// <summary>
    /// ⭐⭐⭐ <b>THE ITEM'S OWN GATE: a selected entity yields a NON-EMPTY <c>ctx.Entities</c> on
    /// Scenario.</b> 📄 The handoff's row: <i>"Scenario shows a details panel; a selected entity yields
    /// a non-empty <c>ctx.Entities</c>."</i>
    ///
    /// <para>⭐⭐ <b>Selected the way production selects</b> — <c>SelectionState</c> on the entity, in the
    /// World *(<c>R-122</c>)*, ⛔ not by writing a store the panel happens to read. ⚠ That is what makes
    /// the assertion about the WIRE rather than about the test.</para>
    ///
    /// <para>⛔ <b>The negative half is not decoration:</b> with nothing selected the context must be
    /// EMPTY. Without it, a builder that returned every entity in the world would pass the positive
    /// half — and the entity views' predicates *(<c>L6.5</c>)* key on <c>[exactly one]</c>.</para>
    /// </summary>
    [Fact]
    public void ASelectedEntity_ReachesTheScenarioContext()
    {
        var editor = RealEditorWithWorld();
        try
        {
            // ⚠ `World` throws when uninitialised, so reaching it at all is the guard: an editor with
            //   no world would make both halves of this rail vacuous.
            var world = editor.World;
            world.RegisterComponent<SelectionState>();

            // ── nothing selected ⇒ empty, and that must be true first ────────
            Assert.Empty(editor.ScenarioWorkspace!.BuildContext().Entities);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new SelectionState
            {
                IsSelected = true, IsPrimarySelection = true,
            });

            var ctx = editor.ScenarioWorkspace!.BuildContext();

            Assert.Equal(new[] { entity }, ctx.Entities.ToArray());
            Assert.Equal("Scenario", ctx.Perspective);
        }
        finally
        {
            editor.Shutdown();
        }
    }
}
