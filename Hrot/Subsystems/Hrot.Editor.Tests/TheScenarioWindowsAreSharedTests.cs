using Fdp.Core;
using Hrot.Common;
using Hrot.Common.Events;
using Hrot.Presentation.Windows;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-060</c>/<c>CE-061</c> — Axis-C <b>E5</b>: the Scenario-perspective windows are ONE
/// implementation, and CGF composes them.</b> 📄 <c>docs/DESIGN_Cgf_Scenario_Windows_Slice.md</c> §7 ⑤.
///
/// <para>⚠⚠ <b>What these rails can and cannot reach — stated up front, because the gap this slice closed
/// hid behind exactly this.</b> CGF builds its panels and adapters inside <c>Initialize</c>'s
/// <c>if (!_headless)</c> block, from <c>_context</c> (world, bus, TKB db, geo transform). ⇒ ⛔ a
/// bare-ctor <c>RegisterWindows</c> rail — the kind every window unit test uses — reaches <c>null</c>
/// panels and registers nothing, so it <b>cannot</b> assert that CGF shows these windows.</para>
///
/// <para>⭐⭐ So the coverage is split honestly: <b>source-scan composition guards</b> for *"the host wires
/// it"*, <b>behavioural rails</b> for the parts that are pure logic, and the <b>T3 conformance rails</b>
/// (which read <c>PanelSnapshot</c> from a real cluster) for *"the window actually appears"*. ⛔ Claiming
/// a unit rail proves the last one is the mistake that kept <c>CE-049</c> and <c>CE-053</c> green.</para>
/// </summary>
public sealed class TheScenarioWindowsAreSharedTests
{
    // ══ ① ONE IMPLEMENTATION — the wrappers are gone from the hosts ══════════

    /// <summary>
    /// ⭐⭐⭐ <b>Neither host declares its own wrapper for a shared panel any more.</b> 📐 Before E5,
    /// <c>EditorWindows.cs</c> and <c>ExConWindows.cs</c> each declared four, with the same bodies —
    /// which is why CGF could not have them: <c>Hrot.Editor → Hrot.CGF</c> makes that file unreachable.
    /// ⚠ Scoped to the EDITOR's file: <c>Hrot.ExCon</c> is the backend lane's and adopting the shared set
    /// there is a separate item (design §4).
    /// </summary>
    [Theory]
    [InlineData("EditorSpawnerWindow")]
    [InlineData("EditorMissionWindow")]
    [InlineData("EditorConfigWindow")]
    [InlineData("EditorSharedOrbatWindow")]
    public void TheEditorDeclaresNoPrivateWrapperForASharedPanel(string typeName)
    {
        var text = HostSource.ReadRelative("Hrot", "Subsystems", "Hrot.Editor", "Windows", "EditorWindows.cs");
        Assert.DoesNotContain($"class {typeName} ", text);
    }

    /// <summary>
    /// ⭐⭐ <b>Both hosts register through the shared types.</b> ⚠ A source scan is necessary — window
    /// registration is composition — and it is the substitute for the bare-ctor rail this file's remarks
    /// explain cannot reach CGF's panels.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostRegistersTheSharedScenarioWindows(string project, string file)
    {
        var text = HostSource.Read(project, file);

        Assert.Contains("SpawnerPanelWindow(",     text);
        Assert.Contains("MissionPanelWindow(",     text);
        Assert.Contains("ConfigPanelWindow(",      text);
        Assert.Contains("SharedOrbatPanelWindow(", text);
    }

    /// <summary>
    /// ⭐⭐ <b>A host that registers a window also CONSTRUCTS its adapter.</b> 📌 This is the silent-default
    /// guard in rail form: registering the window with a null adapter would give a window that cannot be
    /// serviced, and the shared wrappers take the adapter by value so it cannot be skipped.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostThatRegistersTheWindowsConstructsTheAdapters(string project, string file)
    {
        var text = HostSource.Read(project, file);
        if (!text.Contains("SpawnerPanelWindow(", StringComparison.Ordinal)) return;

        // ⭐ CE-1017 — the spawn adapter comes from the map pack's EntityAuthoring (one construction site).
        Assert.Contains("EntityAuthoring?.Spawn",    text);
        Assert.Contains("ScenarioMissionService(",   text);
        Assert.Contains("ScenarioMapConfigAdapter(", text);
        Assert.Contains("ScenarioOrbatAdapter(",     text);
    }

    // ══ ② THE IDS — the byte-identical gate, and no cross-host collision ═════

    /// <summary>
    /// ⭐⭐⭐ <b>The editor's four window ids are UNCHANGED.</b> ⚠ This is the gate that makes E5 safe: the
    /// ids key layout files, <c>PanelSnapshot</c> instrumentation and every id-keyed rail, so a "tidier"
    /// rename would silently reset users' window layouts.
    /// </summary>
    [Fact]
    public void TheEditorIdsAreTheHistoricalOnes()
    {
        Assert.Equal("editor_spawner",      ScenarioPanelWindowIds.EditorSpawner);
        Assert.Equal("editor_mission",      ScenarioPanelWindowIds.EditorMission);
        Assert.Equal("editor_config",       ScenarioPanelWindowIds.EditorConfig);
        Assert.Equal("editor_shared_orbat", ScenarioPanelWindowIds.EditorOrbat);
    }

    /// <summary>
    /// ⭐⭐ <b>CGF's ids are DISTINCT from the editor's.</b> ⚠ The two hosts can never run in one process
    /// (<c>HrotRunnerConfiguration.Validate</c> rejects <c>editor</c> with <c>cgf</c>), so a collision
    /// would not crash — ⛔ it would make a layout file written by one host reposition the other's
    /// windows, which is worse because it is silent.
    /// </summary>
    [Fact]
    public void TheCgfIdsDoNotCollideWithTheEditorIds()
    {
        var editor = new[] { ScenarioPanelWindowIds.EditorSpawner, ScenarioPanelWindowIds.EditorMission,
                             ScenarioPanelWindowIds.EditorConfig,  ScenarioPanelWindowIds.EditorOrbat };
        var cgf    = new[] { ScenarioPanelWindowIds.CgfSpawner,    ScenarioPanelWindowIds.CgfMission,
                             ScenarioPanelWindowIds.CgfConfig,     ScenarioPanelWindowIds.CgfOrbat };

        Assert.Empty(editor.Intersect(cgf));
        Assert.Equal(4, editor.Distinct().Count());
        Assert.Equal(4, cgf.Distinct().Count());
    }

    /// <summary>
    /// ⭐⭐ <b>The window is genuinely parameterised</b> — the same type yields a different id/perspective
    /// per host. ⛔ Without this, "one implementation" could still be one implementation with a hard-coded
    /// id, which is what the two duplicated wrappers were.
    /// </summary>
    [Fact]
    public void OneWindowTypeServesBothHostsIdentities()
    {
        var editorWin = new SpawnerPanelWindow(
            new SpawnerPanel(), Mock.Of<ISpawnController>(),
            ScenarioPanelWindowIds.EditorSpawner, "Scenario", default);
        var cgfWin = new SpawnerPanelWindow(
            new SpawnerPanel(), Mock.Of<ISpawnController>(),
            ScenarioPanelWindowIds.CgfSpawner, "Scenario", default);

        Assert.Equal("editor_spawner", editorWin.Id);
        Assert.Equal("cgf_spawner",    cgfWin.Id);
        Assert.Equal(editorWin.SimulateDrawClientArea().PanelKind,
                     cgfWin.SimulateDrawClientArea().PanelKind);   // ⭐ one KIND, two instances
    }

    // ══ ③ CE-060 — SelectEntity finally selects the entity ═══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-060</c>: <c>ScenarioOrbatAdapter.SelectEntity</c> publishes BOTH shared events.</b>
    /// 📐 Measured before the fix: the body was <c>_logic.ActivateTool(EditorTool.Select)</c> — it
    /// activated the tool and ⛔ <b>dropped the <c>entityId</c> on the floor</b>, so clicking an ORBAT row
    /// selected nothing on either host. ⚠ That editor-facade call was also the LAST thing keeping this
    /// adapter inside <c>Hrot.Editor</c>.
    /// </summary>
    [Fact]
    public void SelectingAnOrbatRowActivatesTheToolAndSelectsTheEntity()
    {
        using var world = new EntityRepository();
        var bus = world.Bus;

        new ScenarioOrbatAdapter(world, bus, Mock.Of<ISpawnController>()).SelectEntity(4242);
        // ⭐ The bus is double-buffered — a publish is visible to Read<> only after the swap, the same
        //   way AdapterTests' embark/disembark rails read it.
        bus.SwapBuffers();

        var tools = bus.Read<ActivateEditorToolEvent>().ToArray();
        var picks = bus.Read<SelectEntityCommand>().ToArray();

        Assert.Single(tools);
        Assert.Equal(EditorTool.Select, tools[0].Tool);
        Assert.Single(picks);
        Assert.Equal(4242L, picks[0].NetworkId);   // ⛔ the half that was missing entirely
    }

    // ══ ④ ONE SPAWNER CATALOG ════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The spawner catalog is built from the TKB, not a literal.</b> 📐 It was declared twice — 15 entries in
    /// <c>EditorSubsystem</c>, a near-duplicate 9 in <c>ExConSubsystem</c> — and CE-061 made it one shared list.
    /// ⭐ <c>CE-1017</c> S4 retires that list too: both hosts take <c>EntityTypeCatalog.SpawnerEntries(tkb)</c> and open
    /// the grouped picker (docs/DESIGN_Add_Entity_Picker.md D7, S4).
    /// </summary>
    [Fact]
    public void TheEditorAndCgfTakeTheirSpawnerCatalogFromTheTkb()
    {
        foreach (var (project, file) in new[] { ("Hrot.Editor", "EditorSubsystem.cs"), ("Hrot.CGF", "CgfSubsystem.cs") })
        {
            var text = HostSource.Read(project, file);
            Assert.Contains("EntityTypeCatalog.SpawnerEntries(", text);
            Assert.Contains("OpenPicker = () => _shellPickers", text);
            Assert.DoesNotContain("new TkbCatalogEntry[]", text);
            Assert.DoesNotContain("ScenarioSpawnerCatalog.Default", text);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1017</c> — ONE entity-authoring wiring on every windowed map host (🔒 user: "Do we share unified code
    /// across simhost and cgf and ig and editor? We should."): each passes <c>MapInteractionContext.EntityAuthoring</c>,
    /// schedules the PACK's canvas menu and draws a picker — and none hand-builds a spawn adapter, a canvas menu system
    /// or an entity-icon picker of its own. ⛔ A host that forgets a half gets no submenu and no error — this rail is
    /// the error.
    /// </summary>
    [Theory]
    [InlineData("Hrot.Editor",  "EditorSubsystem.cs")]
    [InlineData("Hrot.CGF",     "CgfSubsystem.cs")]
    [InlineData("Hrot.SimHost", "SimHostApp.cs")]
    [InlineData("Hrot.IG",      "IgApplication.cs")]
    public void EveryWindowedMapHostUsesTheOneEntityAuthoringWiring(string project, string file)
    {
        var text = HostSource.Read(project, file);

        Assert.Contains("EntityAuthoring = _headless ? null : new Hrot.UI.Common.AddEntity.EntityAuthoringInputs(", text);
        Assert.Matches(@"RegisterGlobalSystem\(\w+\.CanvasMenu\)", text);
        Assert.Contains(".DrawFrame()", text);   // the picker is drawn, or OpenPicker only queues
        Assert.DoesNotContain("new Hrot.Presentation.Systems.CanvasMenuUpdateSystem(", text);
        Assert.DoesNotContain("ScenarioSpawnAdapter(", text);
        Assert.DoesNotContain("new Hrot.UI.Common.AddEntity.EntityIconLibrary(", text);
    }
}
