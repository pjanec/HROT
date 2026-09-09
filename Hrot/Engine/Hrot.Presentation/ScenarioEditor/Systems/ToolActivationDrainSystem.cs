using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common;
using Hrot.Common.Events;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-051</c> (Axis-C <b>E3</b>) — the tool-activation drain, SHARED. Finishes
/// <c>PACK2-E002</c>.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c></b> §1, §3 ②, §4, §5.
///
/// <para>🔴 <b>What it replaces: a switch welded into a 5 000-line host.</b> 📐 Measured
/// <c>2026-08-26</c>: the whole "tool system" was `EditorSubsystem.DrainToolActivationEvents` — the
/// `EditorTool` enum, a `switch`, and the (already shared) gizmos. ⭐ CGF had the same primitives and
/// reached them through **hand-rolled context-menu callbacks instead**, which is how the two drifted.</para>
///
/// <para>⭐⭐ <b>Deliberately NOT an <c>ITool</c>/<c>ToolManager</c> registry.</b> Design §1 measured that
/// no such thing exists and §8 forbids inventing tool vocabulary — E3 shares the ORCHESTRATION, it does
/// not design a tool framework.</para>
///
/// <para>⚠⚠ <b>Every dependency is a DELEGATE or an already-shared type, and that is what let this move
/// at all.</b> The editor's drain read four host-local fields; three of them
/// *(<c>DataDrivenGizmoSystem</c>, <c>GlobalGizmoManager</c>, <c>ISelectionState</c>)* turned out to be
/// shared already, and only the spawn adapter is host-supplied — as a bare
/// <see cref="Action"/>, for the reason its parameter documents.</para>
/// </summary>
/// <remarks>
/// ⚠⚠ <b><c>[UpdateInPhase(PostSimulation)]</c> — and its ABSENCE was a BOOT CRASH, caught by T3.</b>
/// 📐 <c>SystemScheduler.RegisterSystem</c> throws <c>"System X must have [UpdateInPhase] attribute"</c>, so
/// <c>kernel.Initialize()</c> — and therefore the whole editor — failed to start. ⛔ Every unit rail passed:
/// the test's recording registry accepted any system, so it never asked the question the real scheduler asks.
/// ⇒ ⭐ the rail now asserts the attribute is present *(see <c>TheViewportInteractionIsSharedTests</c>)*.
///
/// <para>⭐ <b>Why <c>PostSimulation</c>:</b> 📐 the editor's old drain ran from <c>EditorSubsystem.Update()</c>
/// at <c>:2239</c>, AFTER <c>_kernel.Update()</c> at <c>:2232</c> — so a gizmo it activated first executed on
/// the following frame either way. ⭐ <c>PostSimulation</c> is the main-thread phase the gizmo group and the
/// sibling <c>CanvasMenuUpdateSystem</c> already use, which keeps this within one frame of the old ordering.
/// ⛔ Not <c>Simulation</c>: that runs on background threads and this touches ImGui-adjacent host state.</para>
/// </remarks>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class ToolActivationDrainSystem : IEcsModuleSystem
{
    private readonly Func<ISelectionState?>       _selection;
    private readonly Func<DataDrivenGizmoSystem?>  _gizmos;
    private readonly Func<GlobalGizmoManager?>?    _globalGizmos;
    private readonly Action?                _startPlacementMode;
    private readonly Action<string>?        _reportUnserviceable;
    private readonly ToolController         _tools;

    /// <summary>
    /// ⚠⚠ <b>The ONE piece of per-frame state, and it is here because <c>Execute</c> is the only place a
    /// world exists.</b> Set at the top of <see cref="Execute"/> and cleared in a <c>finally</c>, so an
    /// activation that somehow runs outside a frame reports unserviceable instead of dereferencing null.
    /// ⛔ Everything else (<c>selection</c>, both arbiters) is a RESOLVER and is re-resolved per activation.
    /// </summary>
    private EntityRepository? _frameWorld;

    /// <param name="startPlacementMode">
    /// ⭐⭐ The Spawn tool's whole behaviour: *"start placement with the last selected type"*.
    ///
    /// <para>⚠ <b>DEVIATION from design §3 ①, argued.</b> The design says lift <c>EditorSpawnAdapter</c>
    /// to shared. 📐 Measured: the drain's entire dependency on it is **one parameterless call**, while the
    /// adapter itself pulls in <c>Hrot.Map.Common</c>, <c>Hrot.UI.Common.Facades</c>,
    /// <c>Hrot.Core.Network</c> and a creation-request source. ⇒ ⭐ a delegate collapses the drain's
    /// duplication *(there is still exactly ONE adapter and ONE drain — ruling 9)* without dragging four
    /// namespaces across an assembly boundary for zero behavioural gain. Folded into the design's §9.</para>
    ///
    /// <para>⛔ <see langword="null"/> on a host that composes no spawn adapter *(CGF today)*. ⚠ The
    /// activation is then REPORTED through <paramref name="reportUnserviceable"/>, ⛔ never silently
    /// dropped — ruling 49's *"absent, and it says so"* applied to a tool rather than a menu item.</para>
    /// </param>
    /// <param name="globalGizmos">
    /// Needed by the Measure tool only — it registers a screen-space gizmo rather than an entity-scoped
    /// one. ⛔ <see langword="null"/> ⇒ Measure is reported unserviceable, same rule as Spawn.
    /// </param>
    /// <param name="reportUnserviceable">
    /// Where *"this host cannot service tool X"* goes. ⭐ Defaults to the FDP log; a rail injects a
    /// recorder. ⚠ It carries the TOOL NAME and the REASON, because *"nothing happened"* is
    /// indistinguishable from *"not implemented"* to the operator holding the mouse.
    /// </param>
    public ToolActivationDrainSystem(
        Func<ISelectionState?>       selection,
        Func<DataDrivenGizmoSystem?> gizmos,
        Func<GlobalGizmoManager?>?   globalGizmos        = null,
        Action?                      startPlacementMode  = null,
        Action<string>?              reportUnserviceable = null)
    {
        _selection           = selection ?? throw new ArgumentNullException(nameof(selection));
        _gizmos              = gizmos    ?? throw new ArgumentNullException(nameof(gizmos));
        _globalGizmos        = globalGizmos;
        _startPlacementMode  = startPlacementMode;
        _reportUnserviceable = reportUnserviceable;

        _tools = new ToolController(
            () => _globalGizmos?.Invoke(),
            _gizmos,
            reportUnserviceable);
        RegisterScenarioTools();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> step 2 — the single arbiter this drain activates through.</b> Exposed so the
    /// host can bind a toolbar / <c>Escape</c> to <see cref="IToolController.ActiveModalChanged"/> and
    /// <see cref="IToolController.Cancel"/> (step 5) without a second registry.
    /// </summary>
    public IToolController Tools => _tools;

    /// <summary>
    /// ⭐⭐ <b>The six arms, unchanged in body, moved behind descriptors.</b> 📄
    /// <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.
    ///
    /// <para>⭐ <b>What each descriptor encodes, and why it is what it is:</b>
    /// <list type="bullet">
    /// <item><b>Arbiter</b> — measured, not guessed: <c>Edit</c>/<c>Route</c>/<c>Rotate</c> inject
    /// per-entity gizmos through <c>DataDrivenGizmoSystem</c>; <c>Measure</c> and the spawn adapter register
    /// on <c>GlobalGizmoManager</c>. <c>Select</c> owns no gizmo at all.</item>
    /// <item><b><c>ToggleOnReactivate</c></b> — only <c>Edit</c>/<c>Route</c>, which toggled before this
    /// slice. ⛔ <c>Rotate</c> deliberately re-arms (both hosts did it that way) and <c>Measure</c> is
    /// re-registered rather than toggled.</item>
    /// </list></para>
    ///
    /// <para>⛔⛔ <b><c>Select</c> is now LIVE, and that is a deliberate, user-visible change.</b> It was an
    /// empty <c>break</c> — the toolbar button did nothing. 🔒 <c>Q27</c> makes it the NULL MODAL TOOL, so
    /// arming it clears BOTH arbiters (<c>ToolArbiter.None</c>) and is how an operator leaves a tool.
    /// ⚠ This is the one place where routing through the controller changes behaviour that previously
    /// "worked"; folded into the design at §4.7.</para>
    /// </summary>
    private void RegisterScenarioTools()
    {
        _tools.Register(
            new ToolDescriptor(ScenarioToolIds.Select, "Select", ToolModality.Modal, ToolArbiter.None,
                               ShowOnToolbar: true),
            // The null modal tool: it arms nothing of its own — CancelOtherArbiter(None) has already
            // cleared both sides by the time this runs, and that IS the whole behaviour.
            _ => ToolActivationOutcome.Armed);

        _tools.Register(
            new ToolDescriptor(ScenarioToolIds.Spawn, "Place Entity", ToolModality.Modal, ToolArbiter.Global,
                               ShowOnToolbar: true),
            _ =>
            {
                // Start placement with the last selected type (tracked by the adapter).
                if (_startPlacementMode == null)
                    return Unserviceable(EditorTool.Spawn, "this host composes no spawn adapter");
                _startPlacementMode();
                return ToolActivationOutcome.Armed;
            });

        _tools.Register(
            new ToolDescriptor(ScenarioToolIds.Edit, "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped,
                               ShowOnToolbar: true, ToggleOnReactivate: true),
            target => ToggleEntityGizmo<EditablePolyline>(target, EditorTool.Edit,
                (w, e, netId, onRemove) => new VertexEditGizmo(w, e, netId, onRemove)));

        _tools.Register(
            new ToolDescriptor(ScenarioToolIds.Route, "Edit Route", ToolModality.Modal, ToolArbiter.EntityScoped,
                               ShowOnToolbar: true, ToggleOnReactivate: true),
            target => ToggleEntityGizmo<RoutePlan>(target, EditorTool.Route,
                (w, e, netId, onRemove) => new RouteWaypointGizmo(w, e, netId, onRemove)));

        _tools.Register(
            new ToolDescriptor(ScenarioToolIds.Measure, "Measure", ToolModality.Modal, ToolArbiter.Global,
                               ShowOnToolbar: true),
            _ =>
            {
                var global = _globalGizmos?.Invoke();
                if (global == null)
                    return Unserviceable(EditorTool.Measure, "this host composes no global gizmo manager");

                var id = GlobalGizmoManager.NewId();
                global.Register(id, new MeasureGizmo(onRemove: () => global.Unregister(id)));
                return ToolActivationOutcome.Armed;
            });

        _tools.Register(
            new ToolDescriptor(ScenarioToolIds.Rotate, "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped,
                               ShowOnToolbar: true),
            ActivateRotate);
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        // ⚠⚠ Resolved PER EXECUTE, not captured — see InteractionDeps' remarks: the editor creates its
        //    selection state and camera AFTER kernel.Initialize() and nulls them on teardown, so a
        //    captured instance would be permanently null.
        var selection = _selection();
        var gizmos    = _gizmos();
        if (selection == null || gizmos == null) return;

        _frameWorld = world;
        try
        {
            foreach (ref readonly var evt in world.Bus.Read<ActivateEditorToolEvent>())
            {
                // ⭐⭐⭐ UXI-07 step 2 — the WHOLE drain is now one line, because "which tool is active" is
                //    no longer this system's business. The controller cancels the OTHER arbiter's modal
                //    before arming, which is the correctness change (ToolController.CancelOtherArbiter).
                // ⚠ The TARGET comes from the selection, exactly as the switch did. 📐 A context-menu
                //   caller SELECTS first and then activates — see ActivateRotate's remarks; that is why the
                //   editor's action path can publish ActivateEditorToolEvent instead of duplicating a body.
                var target = selection.PrimarySelected is { } p ? p : Entity.Null;
                _tools.Activate(ScenarioToolIds.ForEditorTool(evt.Tool), target);
            }
        }
        finally
        {
            _frameWorld = null;
        }
    }

    /// <summary>
    /// ⭐⭐ The <c>Edit</c>/<c>Route</c> shape: **toggle** an entity-scoped gizmo on the primary selection,
    /// gated on the component that makes the tool meaningful.
    ///
    /// <para>⚠ The TOGGLE is the part worth sharing rather than re-deriving: pressing the tool twice must
    /// deactivate, ⛔ not stack a second gizmo on the same entity. 📐 The editor had this; CGF's
    /// context-menu parallels did not have the concept at all.</para>
    /// </summary>
    private ToolActivationOutcome ToggleEntityGizmo<TComponent>(
        Entity e,
        EditorTool tool,
        Func<EntityRepository, Entity, long, Action, IEntityStatefulGizmo> factory)
        where TComponent : class
    {
        if (_frameWorld is not { } world) return Unserviceable(tool, "no simulation frame is in progress");
        var gizmos = _gizmos();
        if (gizmos == null) return Unserviceable(tool, "this host composes no entity gizmo system");

        if (e == Entity.Null)                          return Unserviceable(tool, "nothing is selected");
        if (!world.HasManagedComponent<TComponent>(e))
            return Unserviceable(tool, $"the selected entity has no {typeof(TComponent).Name}");

        // ⚠ Still reachable even though the controller cancels our arbiter first: a gizmo injected by
        //   something the controller did not arm (an unconverted adapter) is still a real toggle-off.
        if (gizmos.HasInjectedGizmo(e)) { gizmos.DeactivateGizmo(e); return ToolActivationOutcome.Dismissed; }

        gizmos.ActivateGizmo(e, factory(world, e, NetworkIdOf(world, e), () => gizmos.DeactivateGizmo(e)));
        return ToolActivationOutcome.Armed;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The Rotate arm — and the reconciliation that made it shareable.</b>
    ///
    /// <para>📐 <b>Measured: the editor and CGF had NEARLY the same body</b> — same gizmo, same
    /// <c>DeactivateGizmo</c>-then-activate, same <c>EntityWriteRouter</c> *(`AX-005b`: CGF does not own
    /// <c>SimTransform</c>, so a direct poke would do nothing and the router asks the owner)*. ⭐ CGF's
    /// copy did ONE thing extra: it set <c>PrimarySelected</c> first, because it was invoked from a
    /// context menu on an entity rather than from a toolbar acting on the selection. ⇒ ⭐⭐ that stays a
    /// CALLER concern — the caller selects, then activates — so the shared body needs no host branch.</para>
    ///
    /// <para>⛔ Unlike Edit/Route this does NOT toggle: it deactivates unconditionally and re-activates.
    /// ⚠ Preserved deliberately — both hosts did it that way, and a rotate gizmo re-armed on the same
    /// entity is the documented interaction.</para>
    /// </summary>
    private ToolActivationOutcome ActivateRotate(Entity e)
    {
        if (_frameWorld is not { } world)
            return Unserviceable(EditorTool.Rotate, "no simulation frame is in progress");
        var gizmos = _gizmos();
        if (gizmos == null)
            return Unserviceable(EditorTool.Rotate, "this host composes no entity gizmo system");

        if (e == Entity.Null)                   return Unserviceable(EditorTool.Rotate, "nothing is selected");
        if (!world.HasComponent<SimTransform>(e))
            return Unserviceable(EditorTool.Rotate, "the selected entity has no SimTransform");

        gizmos.DeactivateGizmo(e);
        gizmos.ActivateGizmo(e, new EntityRotatorGizmo(
            world, e,
            onRemove: () => gizmos.DeactivateGizmo(e),
            writer:   EntityWriteRouter.For(world)));
        return ToolActivationOutcome.Armed;
    }

    private static long NetworkIdOf(EntityRepository world, Entity e)
        => world.HasComponent<NetworkIdentity>(e)
            ? world.GetComponentRO<NetworkIdentity>(e).Value
            : 0L;

    /// <summary>
    /// Say what happened, never fail silently (ruling 49) — and RETURN the outcome, so an arm's last line
    /// is <c>return Unserviceable(...)</c> and no path can report-then-fall-through to "armed".
    /// </summary>
    private ToolActivationOutcome Unserviceable(EditorTool tool, string reason)
    {
        var message = $"tool '{tool}' did nothing — {reason}.";
        if (_reportUnserviceable != null) _reportUnserviceable(message);
        else FdpLog<ToolActivationDrainSystem>.Info("[Tools] {0}", message);
        return ToolActivationOutcome.Unserviceable;
    }
}
