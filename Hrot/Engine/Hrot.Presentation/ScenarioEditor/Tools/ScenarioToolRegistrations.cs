using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> step 3b — WHAT the six map tools DO, registered onto a host's arbiter.</b>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4 · §4.7c.
    ///
    /// <para>🔒 <b>This split IS <c>Q26</c> constraint 3</b>, quoted in
    /// <c>Architect_Question_27_Tool_Model.md</c>: <i>"a tool descriptor is shared; its activation is
    /// host-bound."</i> ⇒ <b>the DESCRIPTORS and the bodies live here, once</b>; the RESOLVERS that bind
    /// them to a host's selection, gizmo systems and spawn adapter are arguments.</para>
    ///
    /// <para>⛔⛔ <b>Why this is not a method on <c>ToolActivationDrainSystem</c> any more.</b> 📐 Measured
    /// <c>2026-09-09</c>: <c>MapInteractionPack.Build</c> is called by <b>FIVE</b> hosts — IG, CGF,
    /// ReplayBrowser, SimHost and the Editor — but only CGF and the Editor compose
    /// <c>ScenarioEditorModule.InteractionDeps</c> and therefore the drain. ⇒ putting the registrations
    /// inside the drain made the tool set reachable on two hosts out of five, and the other three
    /// hand-rolled the same gizmos inline (the <c>D′</c> idiom: <c>IgApplication.cs</c> ×3,
    /// <c>SimHostApp.cs</c>, <c>SimHostVisualization.cs</c>).</para>
    ///
    /// <para>🔒 <b>And that is a user ruling, not a preference</b> (<c>2026-08-10</c>, in <c>Q27</c>'s
    /// standing rulings): <i>"all map subsystems share the FULL tool set; differences are data
    /// availability or host rules, never set membership"</i> ⇒ <b>do not design a per-subsystem
    /// whitelist.</b> ⭐ A host that cannot service a tool still REGISTERS it and reports why when it is
    /// activated — which is what the <c>Unserviceable</c> outcome is for (ruling 49).</para>
    /// </summary>
    public static class ScenarioToolRegistrations
    {
        /// <summary>
        /// Register the six map tools on <paramref name="controller"/>.
        /// </summary>
        /// <param name="controller">The host's arbiter — <c>MapInteraction.Tools</c>.</param>
        /// <param name="world">
        /// ⚠⚠ A RESOLVER, not the world. The arms run at activation time, which is later than
        /// registration; on a host that rebuilds or tears down its world a captured instance would go
        /// stale silently. ⛔ Returning <see langword="null"/> makes every entity tool report rather than
        /// throw.
        /// </param>
        /// <param name="gizmos">The entity-scoped arbiter. <see langword="null"/> ⇒ those tools report.</param>
        /// <param name="globalGizmos">The non-entity arbiter. <see langword="null"/> ⇒ Measure reports.</param>
        /// <param name="startPlacementMode">
        /// ⭐ The Spawn tool's whole behaviour. <see langword="null"/> on a host that composes no spawn
        /// adapter — ⛔ Spawn is still REGISTERED (the no-whitelist ruling) and reports why.
        /// </param>
        /// <param name="reportUnserviceable">Where the reasons go. Defaults to the FDP log.</param>
        public static void RegisterAll(
            ToolController               controller,
            Func<EntityRepository?>      world,
            Func<DataDrivenGizmoSystem?> gizmos,
            Func<GlobalGizmoManager?>?   globalGizmos        = null,
            Action?                      startPlacementMode  = null,
            Action<string>?              reportUnserviceable = null)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (world      == null) throw new ArgumentNullException(nameof(world));
            if (gizmos     == null) throw new ArgumentNullException(nameof(gizmos));

            ToolActivationOutcome Unserviceable(EditorTool tool, string reason)
            {
                var message = $"tool '{tool}' did nothing — {reason}.";
                if (reportUnserviceable != null) reportUnserviceable(message);
                else FdpLog<ToolController>.Info("[Tools] {0}", message);
                return ToolActivationOutcome.Unserviceable;
            }

            controller.Register(
                new ToolDescriptor(ScenarioToolIds.Select, "Select", ToolModality.Modal, ToolArbiter.None,
                                   ShowOnToolbar: true),
                // 🔒 The NULL modal tool. It arms nothing of its own — CancelOtherArbiter(None) has already
                //    cleared both sides by the time this runs, and that IS the whole behaviour (Q27).
                _ => ToolActivationOutcome.Armed);

            controller.Register(
                new ToolDescriptor(ScenarioToolIds.Spawn, "Place Entity", ToolModality.Modal, ToolArbiter.Global,
                                   ShowOnToolbar: true),
                _ =>
                {
                    if (startPlacementMode == null)
                        return Unserviceable(EditorTool.Spawn, "this host composes no spawn adapter");
                    startPlacementMode();
                    return ToolActivationOutcome.Armed;
                });

            controller.Register(
                new ToolDescriptor(ScenarioToolIds.Edit, "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped,
                                   ShowOnToolbar: true, ToggleOnReactivate: true),
                target => ToggleEntityGizmo<EditablePolyline>(target, EditorTool.Edit,
                    (w, e, netId, onRemove) => new VertexEditGizmo(w, e, netId, onRemove)));

            controller.Register(
                new ToolDescriptor(ScenarioToolIds.Route, "Edit Route", ToolModality.Modal, ToolArbiter.EntityScoped,
                                   ShowOnToolbar: true, ToggleOnReactivate: true),
                target => ToggleEntityGizmo<RoutePlan>(target, EditorTool.Route,
                    (w, e, netId, onRemove) => new RouteWaypointGizmo(w, e, netId, onRemove)));

            controller.Register(
                new ToolDescriptor(ScenarioToolIds.Measure, "Measure", ToolModality.Modal, ToolArbiter.Global,
                                   ShowOnToolbar: true),
                _ =>
                {
                    var global = globalGizmos?.Invoke();
                    if (global == null)
                        return Unserviceable(EditorTool.Measure, "this host composes no global gizmo manager");

                    var id = GlobalGizmoManager.NewId();
                    global.Register(id, new MeasureGizmo(onRemove: () => global.Unregister(id)));
                    return ToolActivationOutcome.Armed;
                });

            controller.Register(
                new ToolDescriptor(ScenarioToolIds.Rotate, "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped,
                                   ShowOnToolbar: true),
                ActivateRotate);

            // ── the arm bodies ───────────────────────────────────────────────────

            // ⭐⭐ The Edit/Route shape: TOGGLE an entity-scoped gizmo, gated on the component that makes
            //    the tool meaningful. ⚠ The toggle is the part worth sharing rather than re-deriving:
            //    pressing the tool twice must deactivate, ⛔ not stack a second gizmo on the same entity.
            ToolActivationOutcome ToggleEntityGizmo<TComponent>(
                Entity e,
                EditorTool tool,
                Func<EntityRepository, Entity, long, Action, IEntityStatefulGizmo> factory)
                where TComponent : class
            {
                if (world() is not { } w)  return Unserviceable(tool, "this host has no world");
                if (gizmos() is not { } g) return Unserviceable(tool, "this host composes no entity gizmo system");

                if (e == Entity.Null)                      return Unserviceable(tool, "nothing is selected");
                if (!w.HasManagedComponent<TComponent>(e))
                    return Unserviceable(tool, $"the selected entity has no {typeof(TComponent).Name}");

                // ⚠ Still reachable even though the controller cancels our arbiter first: a gizmo injected
                //   by something the controller did not arm (an unconverted adapter) is a real toggle-off.
                if (g.HasInjectedGizmo(e)) { g.DeactivateGizmo(e); return ToolActivationOutcome.Dismissed; }

                g.ActivateGizmo(e, factory(w, e, NetworkIdOf(w, e), () => g.DeactivateGizmo(e)));
                return ToolActivationOutcome.Armed;
            }

            // ⛔ Unlike Edit/Route this does NOT toggle: it deactivates unconditionally and re-activates.
            //   ⚠ Preserved deliberately — every host did it that way, and a rotate gizmo re-armed on the
            //   same entity is the documented interaction.
            // 🔒 EntityWriteRouter: a host that does not OWN SimTransform cannot poke it directly, so the
            //   router asks the owner (`AX-005b`). That is why this is shared rather than per-host.
            ToolActivationOutcome ActivateRotate(Entity e)
            {
                if (world() is not { } w)  return Unserviceable(EditorTool.Rotate, "this host has no world");
                if (gizmos() is not { } g) return Unserviceable(EditorTool.Rotate, "this host composes no entity gizmo system");

                if (e == Entity.Null)                return Unserviceable(EditorTool.Rotate, "nothing is selected");
                if (!w.HasComponent<SimTransform>(e))
                    return Unserviceable(EditorTool.Rotate, "the selected entity has no SimTransform");

                g.DeactivateGizmo(e);
                g.ActivateGizmo(e, new EntityRotatorGizmo(
                    w, e,
                    onRemove: () => g.DeactivateGizmo(e),
                    writer:   EntityWriteRouter.For(w)));
                return ToolActivationOutcome.Armed;
            }
        }

        private static long NetworkIdOf(EntityRepository world, Entity e)
            => world.HasComponent<NetworkIdentity>(e)
                ? world.GetComponentRO<NetworkIdentity>(e).Value
                : 0L;
    }
}
