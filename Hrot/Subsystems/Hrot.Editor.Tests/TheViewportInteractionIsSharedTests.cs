using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.IG.Components;
using Hrot.Common.Events;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Systems;
using Hrot.ScenarioEditor.Tools;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-051</c> (Axis-C E3) — rails for the shared viewport interaction.</b>
/// 📄 <c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c> §3, §6 *(the two-way reconciliation)*, §7.
///
/// <para>⭐⭐ <b>The two rails that carry the batch</b> are the SOURCE SCANS: they assert that neither host
/// still owns a hand-rolled parallel. ⛔ A reference count cannot see this — the parallels called the same
/// shared primitives and referenced nothing new, which is exactly how the editor's drain and CGF's context
/// menu drifted apart for months *(the same reason E2's create-core rail is a source scan)*.</para>
/// </summary>
// 🔒 SERIALIZED: this class flips the process-global FdpConfig.EnforceExplicitEventRegistration (§⑤).
//    ⛔ Without this, that flip leaks into any class publishing a managed event in parallel — measured
//    2026-09-09 against JsonEntityContextMenuHandlerTests. See the collection's own remarks.
[Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheViewportInteractionIsSharedTests
{
    // ══ ① THE DE-DUP GUARDS — §6's requirement, made checkable ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>CGF must not hand-roll the camera centre again.</b>
    /// 🔴 The deleted `CenterCameraOnEntity` was MEASURED BROKEN: it assigned <c>Camera.Target</c>, which
    /// <c>MapCamera.Update</c> overwrites from <c>_targetTarget</c> every frame — so centring moved the
    /// view to the origin. ⇒ this rail fails any host that assigns <c>Camera.Target</c> instead of
    /// publishing <see cref="CenterOnEntityCommand"/> / calling <c>FocusOn</c>.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void NoCompositionRootAssignsTheCameraTargetDirectly(string project, string file)
    {
        var text = ReadHostSource(project, file);

        Assert.DoesNotContain("Camera.Target =", text);
        Assert.DoesNotContain("Camera.Target=", text);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Neither host still drives a tool by constructing its gizmo inline.</b>
    /// 📐 Before E3 both did: the editor in <c>DrainToolActivationEvents</c>, CGF in its context menu — and
    /// only CGF's set the selection first, which is how *"the same tool"* meant two things.
    /// ⇒ the gizmo constructors now appear only in the shared <see cref="ToolActivationDrainSystem"/>.
    ///
    /// <para>🔴🔴 <b>THE RAIL WAS BLIND, measured <c>2026-09-09</c> during <c>UXI-07</c> step 3.</b> It
    /// matched the literal <c>"new EntityRotatorGizmo"</c>, while <c>EditorSubsystem.cs:1862</c> wrote
    /// <c>new Hrot.ScenarioEditor.Gizmos.EntityRotatorGizmo(</c> — <b>fully qualified</b>. ⇒ the editor's
    /// <c>GlobalActionIds.Rotate</c>/<c>EditOverlay</c>/<c>EditRoute</c> handlers carried a verbatim copy of
    /// the drain's three arms and this rail stayed GREEN over all of it.
    /// ⚠⚠ <b>The lesson is the substring, not the copy:</b> a source scan that pins the SPELLING of a
    /// reference tests the spelling. ⇒ it is a REGEX now, and it tolerates any qualification.
    /// 🔒 <c>R-142</c> ③ — a rail that stays green while the feature is broken is itself the finding, and
    /// it is fixed in place rather than routed around.</para>
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void NoCompositionRootConstructsAToolGizmoItself(string project, string file)
    {
        var text = ReadHostSource(project, file);

        foreach (var gizmo in new[] { "EntityRotatorGizmo", "VertexEditGizmo", "RouteWaypointGizmo",
                                      "MeasureGizmo" })
        {
            // `new` · optional namespace qualification · the type · `(`  — the whole point is that
            // "new Hrot.ScenarioEditor.Gizmos.EntityRotatorGizmo(" must match as surely as "new EntityRotatorGizmo(".
            var construction = new System.Text.RegularExpressions.Regex(
                @"new\s+(?:[A-Za-z_][\w]*\s*\.\s*)*" + gizmo + @"\s*\(");

            Assert.False(construction.IsMatch(text),
                $"{file} constructs {gizmo} itself — CE-051 moved every tool gizmo into the shared "
              + "ToolActivationDrainSystem (ruling 9). Publish ActivateEditorToolEvent instead.");
        }
    }

    /// <summary>
    /// ⭐⭐ <b>And the editor's drain method itself is gone</b> — the positive half of the same claim, so
    /// the rail cannot pass merely because a name was renamed.
    /// </summary>
    [Fact]
    public void TheEditorsDrainMethodIsGone()
    {
        var text = ReadHostSource("Hrot.Editor", "EditorSubsystem.cs");

        Assert.DoesNotContain("private void DrainToolActivationEvents()", text);
        // ⭐ …and the module that replaced it IS registered.
        Assert.Contains("new ScenarioEditorModule(", text);
        Assert.Contains("InteractionDeps(", text);
    }

    /// <summary>⭐ CGF registers the same module — the other side of §6's reconciliation.</summary>
    [Fact]
    public void CgfRegistersTheSharedModule()
    {
        var text = ReadHostSource("Hrot.CGF", "CgfSubsystem.cs");

        Assert.Contains("ScenarioEditorModule(", text);
        Assert.Contains("InteractionDeps(", text);
        Assert.DoesNotContain("private void CenterCameraOnEntity(", text);
    }

    // ══ ② THE SELECTION SYSTEM — new capability, so rail it as such ═════════

    /// <summary>
    /// ⭐⭐⭐ <b><see cref="SelectEntityCommand"/> finally DOES something.</b>
    /// 🔴 MEASURED <c>2026-08-26</c>: before E3 the command was published by
    /// <c>EditorApplication.SelectEntity</c> and <b>read by nothing in the repo</b> ⇒
    /// <c>IEditorLogic.SelectEntity</c> was a silent no-op on every host. ⚠ Its reference count was
    /// non-zero, which is precisely why *"never read a reference count as adoption"* exists.
    /// </summary>
    [Fact]
    public void SelectEntityCommandWritesThePrimarySelection()
    {
        var (world, entity, netId) = WorldWithEntity();
        var selection = new DefaultSelectionState();
        Entity? alsoSelected = null;

        var system = new SelectEntitySystem(() => selection, e => alsoSelected = e);

        world.Bus.Publish(new SelectEntityCommand { NetworkId = netId });
        world.Bus.SwapBuffers();
        system.Execute(world, 0f);

        Assert.Equal(entity, selection.PrimarySelected);
        Assert.Equal(entity, alsoSelected);
    }

    /// <summary>
    /// ⭐⭐ <b>An unknown network id changes nothing</b> — ⛔ not even to <c>Entity.Null</c>.
    /// ⚠ Clearing the selection because a stale id arrived would be a worse bug than the no-op it replaces.
    /// </summary>
    [Fact]
    public void AnUnknownNetworkIdLeavesTheSelectionAlone()
    {
        var (world, entity, _) = WorldWithEntity();
        var selection = new DefaultSelectionState { PrimarySelected = entity };
        var system = new SelectEntitySystem(() => selection);

        world.Bus.Publish(new SelectEntityCommand { NetworkId = 999_999 });
        world.Bus.SwapBuffers();
        system.Execute(world, 0f);

        Assert.Equal(entity, selection.PrimarySelected);
    }

    // ══ ③ THE CAMERA — the bug the reconciliation found ═════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL FOR THE MEASURED DEFECT: centring must SURVIVE the next camera update.</b>
    ///
    /// <para>🔴 CGF's deleted arm set <c>Camera.Target</c>, leaving <c>_targetTarget</c> untouched;
    /// <c>MapCamera.Update</c> then assigned <c>InnerCamera.Target = _targetTarget</c> and the centre was
    /// undone. ⇒ ⭐⭐ this rail centres, then calls <c>Update</c>, then asserts the position **still**
    /// holds. ⛔ A rail that only checked <c>Target</c> immediately after would have passed on the broken
    /// code — which is exactly why the bug survived.</para>
    /// </summary>
    [Fact]
    public void CentringSurvivesTheNextCameraUpdate()
    {
        var (world, _, netId) = WorldWithEntity(x: 123f, y: 456f);
        var camera = new MapCamera();
        var system = new CenterOnEntitySystem(() => camera);

        world.Bus.Publish(new CenterOnEntityCommand { NetworkId = netId });
        world.Bus.SwapBuffers();
        system.Execute(world, 0f);

        // ⚠ THE point of the rail — one frame of camera update, which is what broke the old path.
        camera.Update(1f / 60f);

        Assert.Equal(123f, camera.Target.X, 3);
        Assert.Equal(456f, camera.Target.Y, 3);
    }

    /// <summary>⭐ No camera composed ⇒ no throw. A headless host publishes the command harmlessly.</summary>
    [Fact]
    public void CentringWithNoCameraIsHarmless()
    {
        var (world, _, netId) = WorldWithEntity();
        var system = new CenterOnEntitySystem(() => null);

        world.Bus.Publish(new CenterOnEntityCommand { NetworkId = netId });
        world.Bus.SwapBuffers();

        Assert.Null(Record.Exception(() => system.Execute(world, 0f)));
    }

    // ══ ④ THE TOOL DRAIN — unserviceable tools SAY SO ══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A tool this host cannot service is REPORTED, not silently dropped.</b>
    /// 🔒 Ruling 49 / <c>VC-3</c>, applied to a tool rather than a menu item: *"nothing happened"* is
    /// indistinguishable from *"not implemented"* to the operator holding the mouse.
    /// ⚠⚠ <b>CORRECTED <c>2026-08-27</c> (<c>CE-061</c>):</b> this used to read *"CGF is exactly this
    /// case for `Spawn` — it composes no `EditorSpawnAdapter`"*. 📐 No longer true: CGF now composes
    /// <c>ScenarioSpawnAdapter</c> and PASSES <c>StartPlacementMode</c>, so Spawn is serviceable there.
    /// ⭐ The rail is unaffected — it asserts the MECHANISM (a null dependency is reported), not that any
    /// particular host lacks one. ⛔ A headless node still has no adapter, and then the report is right.
    /// </summary>
    [Fact]
    public void AnUnserviceableToolIsReportedWithItsReason()
    {
        var (world, _, _) = WorldWithEntity();
        var reports = new List<string>();
        // ⭐ UXI-07 step 3b — the reporting lives in the SHARED registration now, so the rail drives it
        //   there rather than through the drain. ⛔ A host with neither a spawn adapter nor a global gizmo
        //   manager still REGISTERS both tools (no per-subsystem whitelist) and says why they do nothing.
        var tools = new ToolController(() => null, () => NewGizmoSystem(), reports.Add);
        ScenarioToolRegistrations.RegisterAll(
            tools, world: () => world, gizmos: () => NewGizmoSystem(),
            globalGizmos: null, startPlacementMode: null, reportUnserviceable: reports.Add);

        tools.Activate(ScenarioToolIds.Spawn);
        tools.Activate(ScenarioToolIds.Measure);

        Assert.Equal(2, reports.Count);
        Assert.Contains(reports, r => r.Contains("Spawn") && r.Contains("spawn adapter"));
        Assert.Contains(reports, r => r.Contains("Measure") && r.Contains("global gizmo manager"));
    }

    /// <summary>
    /// ⭐⭐ <b>A SERVICED tool reports nothing</b> — the negative half. ⚠ A drain that logged on every
    /// activation would bury the real signal, which is the same reasoning behind the E1 warn-once dedup.
    /// </summary>
    [Fact]
    public void AServicedToolReportsNothing()
    {
        var (world, _, _) = WorldWithEntity();
        var reports = new List<string>();
        bool placed = false;
        var tools = new ToolController(() => null, () => NewGizmoSystem(), reports.Add);
        ScenarioToolRegistrations.RegisterAll(
            tools, world: () => world, gizmos: () => NewGizmoSystem(),
            globalGizmos: null, startPlacementMode: () => placed = true, reportUnserviceable: reports.Add);

        tools.Activate(ScenarioToolIds.Spawn);
        tools.Activate(ScenarioToolIds.Select);

        Assert.True(placed);
        Assert.Empty(reports);
    }

    /// <summary>
    /// ⭐⭐ <b>The systems tolerate a host whose viewport is not built yet.</b>
    /// 🔴 THE reason every dep is a resolver: in <c>EditorSubsystem</c> the module is constructed at
    /// <c>:1273</c> and <c>RegisterSystems</c> runs at <c>:1733</c>, but the selection state and camera are
    /// created at <c>:1801</c>–<c>:1945</c> and nulled again on teardown. ⛔ Capturing instances would have
    /// wired the systems to permanent nulls with no error at all.
    /// </summary>
    [Fact]
    public void ANotYetBuiltViewportIsToleratedRatherThanThrowing()
    {
        var (world, _, _) = WorldWithEntity();
        var system = new ToolActivationDrainSystem(
            selection: () => null, gizmos: () => null, tools: () => null);

        world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Rotate));
        world.Bus.SwapBuffers();

        Assert.Null(Record.Exception(() => system.Execute(world, 0f)));
    }

    // ══ ④b UXI-07 — THE DRAIN ARMS THROUGH THE ONE CONTROLLER ═══════════════
    //
    // 📄 docs/UX/UX_Feature_Tool_Model.md §4. These two rails are added to the DRAIN's own suite rather
    //    than to a new class (R-142 ④): the drain is the feature, and steps 2–3 changed HOW it arms.
    // ⛔ The CONTROLLER's own rules (retarget, Dismissed, PushModal) are railed next to the controller in
    //    Hrot.Presentation.Tests/Tools/ToolControllerTests.cs — these two assert the DRAIN's end of it.

    /// <summary>
    /// ⭐⭐⭐ <b>The <c>Edit</c> toggle SURVIVED being routed through the controller — and it very nearly
    /// did not.</b>
    ///
    /// <para>🔴🔴 <b>The hazard, measured <c>2026-09-09</c>:</b> the controller cancels the armed modal
    /// before invoking the next activation, and <c>DataDrivenGizmoSystem.CancelInteractiveTools()</c>
    /// (<c>:124-137</c>) clears <b>every</b> injected gizmo. ⇒ by the time the <c>Edit</c> arm runs, its
    /// <c>HasInjectedGizmo(e)</c> toggle test is already false and a naive routing would RE-ARM on the
    /// second press instead of turning off. 🔒 <c>R-137</c>: unification may not cost a capability.
    /// ⭐ The fix is that the controller remembers the (tool, TARGET) pair — <c>ArmedTool</c>.</para>
    /// </summary>
    [Fact]
    public void TheEditToolStillTogglesOffOnASecondPress()
    {
        var (world, entity, _) = WorldWithEntity();
        var ecb = (EntityCommandBuffer)((ISimulationView)world).GetCommandBuffer();
        ecb.AddManagedComponent(entity, new EditablePolyline
        {
            Points  = new List<System.Numerics.Vector2> { new(0f, 0f), new(1f, 1f) },
            Version = 1,
        });
        ecb.Playback(world);

        var selection = new DefaultSelectionState { PrimarySelected = entity };
        var gizmos    = NewGizmoSystem();                       // ⚠ ONE instance — a per-call factory
        var tools     = new ToolController(() => null, () => gizmos);
        ScenarioToolRegistrations.RegisterAll(                   //   would hide the whole effect.
            tools, world: () => world, gizmos: () => gizmos);
        var system    = new ToolActivationDrainSystem(
            selection: () => selection,
            gizmos:    () => gizmos,
            tools:     () => tools);

        Publish(world, EditorTool.Edit);
        system.Execute(world, 0f);
        Assert.True(gizmos.HasInjectedGizmo(entity));           // armed

        Publish(world, EditorTool.Edit);
        system.Execute(world, 0f);
        Assert.False(gizmos.HasInjectedGizmo(entity));          // 🔴 and OFF again, not re-armed
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> END TO END — arming a tool clears a modal holding focus in the OTHER
    /// arbiter.</b> This is the defect the whole issue exists to close, asserted where a user actually
    /// triggers it: by publishing <see cref="ActivateEditorToolEvent"/>.
    ///
    /// <para>⛔⛔ <b>The bypassing gizmo is not a straw man</b> — <c>EditorMapPickAdapter</c>,
    /// <c>EditorZoneAdapter</c> and <c>ScenarioSpawnAdapter</c> all call <c>GlobalGizmoManager.Register</c>
    /// directly and are NOT converted in this slice. 🔒 And it is not academic: the Windows lane measured
    /// that wiring <c>CanvasMapPickAdapter</c> to a live manager on SimHost breaks
    /// <c>hill-attack-close</c> (<c>CE-257</c>).</para>
    ///
    /// <para>⚠ Inverse-edit red-proof: removing <c>ToolController.CancelOtherArbiter</c>'s global branch
    /// leaves <c>picker.IsFocused</c> true and this rail fails.</para>
    /// </summary>
    [Fact]
    public void ArmingAToolClearsAModalHoldingFocusInTheOtherArbiter()
    {
        var (world, entity, _) = WorldWithEntity();

        // The production wiring: ONE buffer, ONE bus, BOTH arbiters — MapInteractionPack.cs:92-100.
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var bus    = new FdpEventBus();
        var global = new GlobalGizmoManager(buffer, bus);
        var gizmos = new DataDrivenGizmoSystem(new GizmoRegistry(), buffer, interactionBus: bus);

        // ⭐ A REAL exclusive-focus gizmo, registered straight on the arbiter — the adapters' exact shape.
        //   ⛔ Deliberately not a hand-rolled probe: a fake could differ from production in the one
        //   property that decides this (RequiresExclusiveFocus), which is what CancelInteractiveTools
        //   filters on (GlobalGizmoManager.cs:112-119).
        long pickerId = GlobalGizmoManager.NewId();
        global.Register(pickerId, new Hrot.ScenarioEditor.Gizmos.MeasureGizmo(
            onRemove: () => global.Unregister(pickerId)));
        Assert.Equal(1, global.ActiveCount);                     // the adapter shape holds the arbiter …

        var selection = new DefaultSelectionState { PrimarySelected = entity };
        var tools     = new ToolController(() => global, () => gizmos);
        ScenarioToolRegistrations.RegisterAll(
            tools, world: () => world, gizmos: () => gizmos, globalGizmos: () => global);
        var system    = new ToolActivationDrainSystem(
            selection: () => selection,
            gizmos:    () => gizmos,
            tools:     () => tools);

        Publish(world, EditorTool.Rotate);
        system.Execute(world, 0f);

        Assert.True(gizmos.HasInjectedGizmo(entity));             // the tool armed …
        Assert.Equal(0, global.ActiveCount);                      // 🔴 … and the other arbiter LET GO
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> step 3b — EVERY host that builds the pack gets an arbiter WITH THE FULL TOOL
    /// SET.</b> This is the rail for the placement fix, and it is a <c>Build</c>-level claim on purpose.
    ///
    /// <para>📐 <b>What it prevents, measured <c>2026-09-09</c>:</b> the controller was first built inside
    /// <c>ToolActivationDrainSystem</c>. <c>MapInteractionPack.Build</c> is called by <b>FIVE</b> hosts
    /// (IG, CGF, ReplayBrowser, SimHost, Editor) and only <b>TWO</b> compose that drain ⇒ three hosts had
    /// no arbiter at all and hand-rolled the same gizmos inline.</para>
    ///
    /// <para>🔒 <b>And the FULL set, not a subset</b> — user, <c>2026-08-10</c>: <i>"all map subsystems
    /// share the full tool set; differences are data availability or host rules, never set
    /// membership."</i> ⇒ this host passes no spawn adapter, and <c>Spawn</c> is registered anyway.</para>
    /// </summary>
    [Fact]
    public void ThePackGivesEveryHostAnArbiterCarryingTheWholeToolSet()
    {
        var (world, _, _) = WorldWithEntity();

        var map = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext { World = world });

        Assert.NotNull(map.Tools);

        // ⛔ Named as LITERALS, not reflected off ScenarioToolIds: a rail that derived its expectations
        //    from the code under test would follow that code wherever it went.
        foreach (var id in new[] { "scenario.select", "scenario.spawn", "scenario.edit",
                                   "scenario.route", "scenario.measure", "scenario.rotate" })
            Assert.True(map.Tools.IsRegistered(id),
                $"tool '{id}' is not registered on a freshly-built pack — every map subsystem gets the "
              + "FULL set (user ruling, 2026-08-10: never set membership).");
    }

    /// <summary>
    /// ⭐⭐ <b>The arbiter is PER MAP SUBSYSTEM</b> — 🔒 <c>Q27-B</c> answered <b>B1</b>, whose worked
    /// example is SimHost holding <c>Measure</c> while the user switches to CGF and back. ⇒ two packs must
    /// not share one controller, or a perspective switch would cancel the other subsystem's tool.
    /// </summary>
    [Fact]
    public void TwoMapSubsystemsGetTwoIndependentArbiters()
    {
        var (worldA, _, _) = WorldWithEntity();
        var (worldB, _, _) = WorldWithEntity();

        var a = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext { World = worldA });
        var b = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext { World = worldB });

        Assert.NotSame(a.Tools, b.Tools);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE FORWARDING RAIL — every production root that builds a pack must PASS its arbiter on.</b>
    ///
    /// <para>🔒 <b>The control for the silent-default pattern</b>, which this programme has now found ten
    /// times: <i>"a production caller that HAS the dependency must PASS it."</i> ⛔ <c>InteractionDeps.Tools</c>
    /// is optional so a headless or partial host still constructs, which is exactly the shape that lets a
    /// root forget it — and a forgotten arbiter means every tool press on that host is dropped.</para>
    ///
    /// <para>⚠ A SOURCE SCAN, because the failure is an OMISSION in one composition root: no behavioural
    /// test can see a host that simply never passed the argument, and a reference count cannot either.</para>
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void EveryRootThatBuildsAPackForwardsItsToolArbiter(string project, string file)
    {
        var src = ReadHostSource(project, file);

        Assert.Contains("InteractionDeps(", src);
        Assert.True(
            new System.Text.RegularExpressions.Regex(@"Tools:\s*\(\)\s*=>").IsMatch(src),
            $"{file} builds a MapInteraction and registers ScenarioEditorModule but never passes "
          + "InteractionDeps.Tools. The drain would then drop every tool activation on this host. "
          + "Pass the pack's MapInteraction.Tools (UXI-07 step 3b).");

        // ⭐⭐⭐ ONE RAIL PER FORWARDED DEPENDENCY — and this second assertion exists because the first
        //   one alone was NOT enough. 🔴 Measured 2026-09-09: step 3b moved the tool registrations into
        //   MapInteractionPack, which takes the spawn delegate through MapInteractionContext. Both hosts
        //   kept handing it to InteractionDeps instead — a record that no longer read it — so Spawn
        //   reported "this host composes no spawn adapter" on hosts that compose one.
        // ⚠ A BEHAVIOURAL rail could not see this: it builds its own pack and would pass regardless.
        //   The failure is an OMISSION at a composition root, which only a source scan reaches.
        Assert.True(
            new System.Text.RegularExpressions.Regex(@"StartPlacementMode\s*=\s*\(\)\s*=>").IsMatch(src),
            $"{file} builds a MapInteractionContext but never sets StartPlacementMode on it, so the "
          + "Spawn tool this host registers will report itself unserviceable even though the host "
          + "composes a spawn adapter (UXI-07 section 4.10). Note the '=' — it belongs on the CONTEXT, "
          + "not as an ':' argument to InteractionDeps, which no longer carries it.");
    }

    /// <summary>
    /// ⭐⭐ <b>ONE RULE for activating a tool, and no host may invent a third idiom.</b>
    /// 📄 <c>UX_Feature_Tool_Model.md</c> §4.7d — TARGETED activation calls <c>Tools.Activate(id, target)</c>;
    /// TARGET-LESS activation publishes <see cref="ActivateEditorToolEvent"/> and the drain supplies the
    /// selection.
    ///
    /// <para>🔴 <b>The mistake this pins, made and corrected on <c>2026-09-09</c>:</b> step 3 routed the
    /// editor's three entity context-menu actions through the EVENT, which meant writing
    /// <c>PrimarySelected</c> first purely to smuggle the target to the drain — ⛔ a side effect the
    /// original handlers never had, and a third idiom next to SimHost's and IG's direct calls.</para>
    /// </summary>
    [Fact]
    public void TheEditorsEntityActionsActivateDirectlyRatherThanReSelecting()
    {
        var src = ReadHostSource("Hrot.Editor", "EditorSubsystem.cs");

        // The three entity-targeted actions go through one direct-activation helper …
        Assert.Contains("void ActivateToolOnEntity(string toolId, Entity target)", src);
        Assert.Contains("_editorToolController?.Activate(toolId, target)", src);

        // … and that helper does NOT write the selection to carry the target.
        var helper = src.Substring(src.IndexOf("void ActivateToolOnEntity(string toolId, Entity target)",
                                               StringComparison.Ordinal));
        helper = helper.Substring(0, helper.IndexOf("}", StringComparison.Ordinal));
        Assert.DoesNotContain("PrimarySelected", helper);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A HOST THAT COMPOSES A SPAWN ADAPTER MUST GET A SERVICEABLE <c>Spawn</c> TOOL.</b>
    ///
    /// <para>🔴🔴 <b>THE REGRESSION THIS EXISTS FOR, shipped and then found <c>2026-09-09</c>:</b> step 3b
    /// moved the tool registrations out of <c>ScenarioEditorModule</c> and into <c>MapInteractionPack</c>,
    /// which takes the spawn delegate through <c>MapInteractionContext</c>. ⛔ Both hosts kept passing it
    /// to <c>InteractionDeps</c> — which nothing read any more — so <c>Spawn</c> reported <i>"this host
    /// composes no spawn adapter"</i> on the Editor and CGF, <b>both of which compose one</b>.</para>
    ///
    /// <para>⚠⚠ <b>Why the existing forwarding rail did not catch it:</b> it asserts <c>Tools</c> is passed.
    /// ⛔ One rail per dependency is the stated control, and this dependency had none. ⇒ this is that rail.
    /// ⭐ It is BEHAVIOURAL, not a source scan: it builds a real pack and asks the real tool whether it
    /// works, which is the only form that could have failed.</para>
    /// </summary>
    [Fact]
    public void APackGivenASpawnDelegateHasAServiceableSpawnTool()
    {
        var (world, _, _) = WorldWithEntity();
        var reports = new List<string>();
        bool placed = false;

        var map = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext
            {
                World                   = world,
                StartPlacementMode      = () => placed = true,
                ReportUnserviceableTool = reports.Add,
            });

        Assert.True(map.Tools.Activate(ScenarioToolIds.Spawn));
        Assert.True(placed);                       // 🔴 the delegate actually ran …
        Assert.Empty(reports);                     // 🔴 … and nothing cried "no spawn adapter"
    }

    /// <summary>
    /// ⭐ The complement, so the rail above cannot pass by accident: a pack given NO spawn delegate still
    /// REGISTERS <c>Spawn</c> (🔒 no per-subsystem whitelist) and reports why it does nothing (ruling 49).
    /// </summary>
    [Fact]
    public void APackWithNoSpawnDelegateStillRegistersSpawnAndSaysWhyItCannot()
    {
        var (world, _, _) = WorldWithEntity();
        var reports = new List<string>();

        var map = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext
            {
                World                   = world,
                ReportUnserviceableTool = reports.Add,
            });

        Assert.True(map.Tools.IsRegistered(ScenarioToolIds.Spawn));   // registered …
        Assert.False(map.Tools.Activate(ScenarioToolIds.Spawn));      // … unserviceable …
        Assert.Contains(reports, r => r.Contains("spawn adapter"));   // … and it SAID so
    }

    private static void Publish(EntityRepository world, EditorTool tool)
    {
        world.Bus.Publish(new ActivateEditorToolEvent(tool));
        world.Bus.SwapBuffers();
    }

    // ══ ⑤ the module wires them, and only when it has a viewport ════════════

    /// <summary>
    /// ⭐⭐ <b><c>PACK2-E002</c> is finished: <c>RegisterSystems</c> registers the three systems</b> — and
    /// registers NOTHING when the host supplied no viewport, so a headless node or a file-service-only
    /// construction behaves exactly as it did before E3.
    /// </summary>
    [Fact]
    public void TheModuleRegistersTheThreeSystemsOnlyWhenItHasAViewport()
    {
        var withoutViewport = new ScenarioEditorModule();
        Assert.False(withoutViewport.HasInteractionSystems);

        var registry = new RecordingRegistry();
        withoutViewport.RegisterSystems(registry);
        Assert.Empty(registry.Registered);

        var withViewport = new ScenarioEditorModule(
            fileService: null,
            interaction: new ScenarioEditorModule.InteractionDeps(
                Selection: () => new DefaultSelectionState(),
                Gizmos:    () => NewGizmoSystem(),
                Camera:    () => null));

        Assert.True(withViewport.HasInteractionSystems);
        registry = new RecordingRegistry();
        withViewport.RegisterSystems(registry);

        Assert.Equal(
            new[] { nameof(ToolActivationDrainSystem), nameof(SelectEntitySystem), nameof(CenterOnEntitySystem) },
            registry.Registered.ToArray());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>A recording registry that asks WHAT THE REAL SCHEDULER ASKS.</b>
    ///
    /// <para>🔴🔴 <b>Its first cut did not, and T3 paid for it.</b> 📐 The three new systems shipped without
    /// <c>[UpdateInPhase]</c>; <c>SystemScheduler.RegisterSystem</c> throws
    /// <c>"System X must have [UpdateInPhase] attribute"</c>, so <c>kernel.Initialize()</c> — and the whole
    /// editor boot — failed. ⛔ <b>Every unit rail was green</b>, because this fake accepted anything.
    /// ⇒ ⭐⭐ the check moved HERE, where it costs nothing and runs on every future system the module
    /// registers. ⚠ The lesson generalises: <b>a fake that is more permissive than production turns a rail
    /// into a rubber stamp</b>, and the gap only shows in the slowest gate you have.</para>
    /// </summary>
    private sealed class RecordingRegistry : Fdp.ModuleHost.Abstractions.ISystemRegistry
    {
        public readonly List<string> Registered = new();

        public void RegisterSystem<T>(T system) where T : Fdp.ModuleHost.Abstractions.IEcsModuleSystem
            => Registered.Add(RequirePhase(system));

        public Fdp.ModuleHost.Abstractions.IEcsModuleSystem RegisterManualSystem<T>(T system)
            where T : Fdp.ModuleHost.Abstractions.IEcsModuleSystem
        { Registered.Add(RequirePhase(system)); return system; }

        /// <summary>⭐ The scheduler's own precondition, asserted at unit speed.</summary>
        private static string RequirePhase<T>(T system) where T : Fdp.ModuleHost.Abstractions.IEcsModuleSystem
        {
            var type = system!.GetType();
            var phase = type.GetCustomAttributes(
                typeof(Fdp.ModuleHost.Abstractions.UpdateInPhaseAttribute), inherit: true);

            Assert.True(phase.Length > 0,
                $"{type.Name} has no [UpdateInPhase] attribute. SystemScheduler.RegisterSystem THROWS on "
              + "that, so kernel.Initialize() — and the whole host boot — would fail. This rail exists "
              + "because that shipped once and only the T3 system suite caught it.");

            return type.Name;
        }
    }

    // ══ ⑤ CE-065 — THE EVENTS ARE REGISTERED ON THE ONE LIST ═════════════════════════════════
    //
    // 🔴🔴 THE GAP THESE RAILS CLOSE, and every rail above it missed it. The source scans proved CGF
    //    publishes the SHARED command instead of hand-rolling; the behavioural rails proved the shared
    //    SYSTEM reacts correctly. ⛔ NEITHER asked whether the event was REGISTERED on the publishing
    //    host's bus — and under the runner's process-wide strict mode an unregistered publish THROWS.
    // 📐 Measured `2026-08-27` over MCP on --mode all:
    //    POST /entities/1000/focus -> 500 "Strict Mode Violation: Unmanaged event type
    //    'CenterOnEntityCommand' (ID: 8104) was published without being explicitly registered."
    //    ⇒ the user's reported crash: "Center on entity" out of CGF's entity-inspector context menu.
    // ⚠⚠ Note WHY the unit rails could not see it: they run with strict mode OFF (the default), where
    //    Publish lazily creates the stream. ⇒ these rails turn it ON, which is the production condition.

    /// <summary>
    /// ⭐⭐⭐ <b>Every event the SHARED viewport systems read can be PUBLISHED by a host that only adopted
    /// <c>PresentationComponentRegistry</c>.</b>
    ///
    /// <para>⛔⛔ <b>Strict mode ON is the whole point</b> — with it off *(the unit default)*
    /// <c>Publish&lt;T&gt;</c> creates the stream lazily and this rail would pass for an EMPTY registry.
    /// 📌 That is exactly how the gap survived: the behavioural rails above publish these very events and
    /// were green throughout. ⭐ Save/set/restore mirrors <c>HrotNodeBuilderTests</c>, which rails the
    /// identical claim for the ORCHESTRATION registry one bus over.</para>
    ///
    /// <para>⚠ The three types are named as LITERALS rather than reflected out of
    /// <c>ScenarioEditorModule</c>: a rail that derived its expectations from the code under test would
    /// follow that code wherever it went, which is the opposite of a rail.</para>
    /// </summary>
    [Fact]
    public void TheSharedViewportEventsArePublishableAfterOnlyTheSharedRegistry()
    {
        bool previous = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        try
        {
            var world = new EntityRepository();

            // ⭐ ONLY the shared registry — no host-specific registrations. That is the claim: a host
            //   which adopts the shared systems needs nothing else to publish their events.
            Hrot.Map.Common.PresentationComponentRegistry.RegisterAll(world);

            // ⛔ Each publish is separate so the failure message names WHICH event is missing.
            world.Bus.Publish(new CenterOnEntityCommand { NetworkId = 1L });
            world.Bus.Publish(new SelectEntityCommand { NetworkId = 1L });
            world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Rotate));
        }
        finally
        {
            FdpConfig.EnforceExplicitEventRegistration = previous;
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>And the registration is CENTRAL, not re-added inline.</b>
    /// ⛔ Being registered in <c>EditorSubsystem</c> and nowhere else is precisely what broke CGF, so a
    /// green above is not enough — someone "fixing" a future host by adding a line to its own composition
    /// root would restore the two-list state that caused this. ⇒ ⭐ ruling 9, made checkable.
    /// ⚠ A SOURCE SCAN for the same reason the rails at the top of this file are: the defect is an
    /// omission in one host, which no reference count and no behavioural test can see.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void NoHostRegistersTheSharedViewportEventsItself(string project, string file)
    {
        var src = ReadHostSource(project, file);

        foreach (var evt in new[] { "CenterOnEntityCommand", "ActivateEditorToolEvent", "SelectEntityCommand" })
            Assert.DoesNotContain($"RegisterEvent<{evt}>()", src, StringComparison.Ordinal);
    }

    /// <summary>⭐ The smallest real gizmo system — a registry + a draw buffer, nothing else needed here.</summary>
    private static DataDrivenGizmoSystem NewGizmoSystem()
        => new(new GizmoRegistry(), new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer());

    private static (EntityRepository World, Entity Entity, long NetworkId) WorldWithEntity(
        float x = 10f, float y = 20f)
    {
        var world = new EntityRepository();
        Hrot.Map.Common.PresentationComponentRegistry.RegisterAll(world);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();

        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new System.Numerics.Vector3(x, y, 0f) });
        world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 4242L });
        return (world, e, 4242L);
    }

    /// <summary>Reads a composition root's source; the source scan is the only way to see a local function.</summary>
    private static string ReadHostSource(string project, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "Hrot", "Subsystems", project, file);
        Assert.True(File.Exists(path), $"expected {path} to exist — the rail's target moved.");
        return File.ReadAllText(path);
    }
}
