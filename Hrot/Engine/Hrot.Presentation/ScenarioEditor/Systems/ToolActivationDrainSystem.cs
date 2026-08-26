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

        foreach (ref readonly var evt in world.Bus.Read<ActivateEditorToolEvent>())
        {
            switch (evt.Tool)
            {
                case EditorTool.Select:
                    // (Phase 5: _interactionTool removed; selection via ECS gizmos)
                    break;

                case EditorTool.Spawn:
                    // Start placement with the last selected type (tracked by the adapter).
                    if (_startPlacementMode != null) _startPlacementMode();
                    else Unserviceable(EditorTool.Spawn, "this host composes no spawn adapter");
                    break;

                case EditorTool.Edit:
                    ToggleEntityGizmo<EditablePolyline>(world, selection, gizmos, EditorTool.Edit,
                        (w, e, netId, onRemove) => new VertexEditGizmo(w, e, netId, onRemove));
                    break;

                case EditorTool.Route:
                    ToggleEntityGizmo<RoutePlan>(world, selection, gizmos, EditorTool.Route,
                        (w, e, netId, onRemove) => new RouteWaypointGizmo(w, e, netId, onRemove));
                    break;

                case EditorTool.Measure:
                {
                    var global = _globalGizmos?.Invoke();
                    if (global != null)
                    {
                        var id = GlobalGizmoManager.NewId();
                        global.Register(id, new MeasureGizmo(onRemove: () => global.Unregister(id)));
                    }
                    else Unserviceable(EditorTool.Measure, "this host composes no global gizmo manager");
                    break;
                }

                case EditorTool.Rotate:
                    ActivateRotate(world, selection, gizmos);
                    break;
            }
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
    private void ToggleEntityGizmo<TComponent>(
        EntityRepository world,
        ISelectionState selection,
        DataDrivenGizmoSystem gizmos,
        EditorTool tool,
        Func<EntityRepository, Entity, long, Action, IEntityStatefulGizmo> factory)
        where TComponent : class
    {
        var entity = selection.PrimarySelected;
        if (entity is not { } e || e == Entity.Null) { Unserviceable(tool, "nothing is selected"); return; }
        if (!world.HasManagedComponent<TComponent>(e))
        {
            Unserviceable(tool, $"the selected entity has no {typeof(TComponent).Name}");
            return;
        }

        if (gizmos.HasInjectedGizmo(e)) { gizmos.DeactivateGizmo(e); return; }

        gizmos.ActivateGizmo(e, factory(world, e, NetworkIdOf(world, e), () => gizmos.DeactivateGizmo(e)));
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
    private void ActivateRotate(EntityRepository world, ISelectionState selection, DataDrivenGizmoSystem gizmos)
    {
        var entity = selection.PrimarySelected;
        if (entity is not { } e || e == Entity.Null) { Unserviceable(EditorTool.Rotate, "nothing is selected"); return; }
        if (!world.HasComponent<SimTransform>(e))
        {
            Unserviceable(EditorTool.Rotate, "the selected entity has no SimTransform");
            return;
        }

        gizmos.DeactivateGizmo(e);
        gizmos.ActivateGizmo(e, new EntityRotatorGizmo(
            world, e,
            onRemove: () => gizmos.DeactivateGizmo(e),
            writer:   EntityWriteRouter.For(world)));
    }

    private static long NetworkIdOf(EntityRepository world, Entity e)
        => world.HasComponent<NetworkIdentity>(e)
            ? world.GetComponentRO<NetworkIdentity>(e).Value
            : 0L;

    private void Unserviceable(EditorTool tool, string reason)
    {
        var message = $"tool '{tool}' did nothing — {reason}.";
        if (_reportUnserviceable != null) _reportUnserviceable(message);
        else FdpLog<ToolActivationDrainSystem>.Info("[Tools] {0}", message);
    }
}
