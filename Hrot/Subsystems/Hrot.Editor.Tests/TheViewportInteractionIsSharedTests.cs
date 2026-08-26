using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.Common.Events;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Systems;
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
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void NoCompositionRootConstructsAToolGizmoItself(string project, string file)
    {
        var text = ReadHostSource(project, file);

        foreach (var gizmo in new[] { "new EntityRotatorGizmo", "new VertexEditGizmo", "new RouteWaypointGizmo",
                                      "new MeasureGizmo" })
            Assert.False(text.Contains(gizmo, StringComparison.Ordinal),
                $"{file} constructs {gizmo} itself — CE-051 moved every tool gizmo into the shared "
              + "ToolActivationDrainSystem (ruling 9). Publish ActivateEditorToolEvent instead.");
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
    /// ⚠ CGF is exactly this case for <c>Spawn</c> — it composes no <c>EditorSpawnAdapter</c>.
    /// </summary>
    [Fact]
    public void AnUnserviceableToolIsReportedWithItsReason()
    {
        var (world, _, _) = WorldWithEntity();
        var reports = new List<string>();
        var system = new ToolActivationDrainSystem(
            selection:           () => new DefaultSelectionState(),
            gizmos:              () => NewGizmoSystem(),
            globalGizmos:        null,
            startPlacementMode:  null,
            reportUnserviceable: reports.Add);

        world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Spawn));
        world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Measure));
        world.Bus.SwapBuffers();
        system.Execute(world, 0f);

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
        var system = new ToolActivationDrainSystem(
            selection:           () => new DefaultSelectionState(),
            gizmos:              () => NewGizmoSystem(),
            globalGizmos:        null,
            startPlacementMode:  () => placed = true,
            reportUnserviceable: reports.Add);

        world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Spawn));
        world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Select));
        world.Bus.SwapBuffers();
        system.Execute(world, 0f);

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
        var system = new ToolActivationDrainSystem(selection: () => null, gizmos: () => null);

        world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Rotate));
        world.Bus.SwapBuffers();

        Assert.Null(Record.Exception(() => system.Execute(world, 0f)));
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
