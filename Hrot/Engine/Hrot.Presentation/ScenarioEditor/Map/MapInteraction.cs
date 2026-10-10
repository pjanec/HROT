using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐⭐ <b>What <see cref="MapInteractionPack.Build"/> CONSTRUCTS — <c>UXI-23</c> <c>S2b</c>.</b>
    /// 📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.2b (the class diagram) and §3.2d.
    ///
    /// <para>🔒 <b>The host SCHEDULES this; the pack never does.</b> Everything here is constructed and
    /// handed back — nothing is registered with a kernel, because the pack cannot reach one
    /// (<see cref="MapInteractionContext"/> withholds it). The user's ruling: <i>"pack owns construction,
    /// host decides scheduling"</i>.</para>
    ///
    /// <para>⭐ <c>S3</c> added <see cref="RequiredSystems"/> and <see cref="Unserviceable"/> — the declare
    /// half. ⚠⚠ <b>Read §3.2e before trusting them:</b> they catch a host that never SCHEDULES the map, and
    /// they would <b>not</b> have caught <c>CE-123</c>, where every system was present, scheduled and
    /// enabled and the map still drew nothing. That case is <c>MapSelfCheckSystem</c>'s.</para>
    /// </summary>
    public sealed class MapInteraction
    {
        internal MapInteraction(
            DebugPrimitiveBuffer buffer,
            FdpEventBus interactionBus,
            GizmoRegistry gizmoRegistry,
            StatelessGizmoRegistry statelessRegistry,
            GizmoSettingsRegistry settings,
            GlobalGizmoManager globalManager,
            DataDrivenGizmoSystem dataDrivenSystem,
            StatelessGizmoSystem statelessSystem,
            TogglablePostSimulationGroup gizmoGroup,
            GizmoExecutionController gate,
            MapSelfCheckSystem selfCheck,
            Hrot.ScenarioEditor.Tools.ToolController tools,
            Hrot.ScenarioEditor.Selection.EcsSelectionState selection,
            Hrot.ScenarioEditor.Systems.SelectionInteractionSystem selectionInteraction,
            Hrot.ScenarioEditor.Systems.SelectionRequestSystem selectionRequests,
            Hrot.ScenarioEditor.Systems.SelectionNotificationSystem selectionNotifications,
            Hrot.ScenarioEditor.Gizmos.RubberBandState rubberBand,
            Hrot.Common.Interactions.GlobalActionRegistry actions,
            Hrot.Common.Systems.GlobalActionDispatchSystem actionDispatch,
            Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo layerControl,
            Hrot.UI.Common.AddEntity.EntityAuthoring? entityAuthoring,
            Hrot.Presentation.Systems.CanvasMenuUpdateSystem canvasMenu)
        {
            EntityAuthoring        = entityAuthoring;
            CanvasMenu             = canvasMenu;
            Actions                = actions;
            ActionDispatch         = actionDispatch;
            LayerControl           = layerControl;
            RubberBand             = rubberBand;
            Selection              = selection;
            SelectionInteraction   = selectionInteraction;
            SelectionRequests      = selectionRequests;
            SelectionNotifications = selectionNotifications;
            Tools             = tools;
            Buffer            = buffer;
            InteractionBus    = interactionBus;
            GizmoRegistry     = gizmoRegistry;
            StatelessRegistry = statelessRegistry;
            Settings          = settings;
            GlobalManager     = globalManager;
            DataDrivenSystem  = dataDrivenSystem;
            StatelessSystem   = statelessSystem;
            GizmoGroup        = gizmoGroup;
            Gate              = gate;
            SelfCheck         = selfCheck;
        }

        // ══ Map actions and the layer control — built here so ALL map hosts have them ══════════

        /// <summary>
        /// ⭐⭐ <b>The map's action registry</b> — what a menu item's action id runs. The pack registers
        /// the shared actions (<c>OpenLayerControl</c>); the host adds its own. 📐 Before this, Editor,
        /// SimHost and ReplayBrowser each built one by hand and CGF and IG had none, so a map-menu action
        /// did nothing there.
        /// </summary>
        public Hrot.Common.Interactions.GlobalActionRegistry Actions { get; }

        /// <summary>The system that runs <see cref="Actions"/>; ⚠ the HOST schedules it (the pack only constructs).</summary>
        public Hrot.Common.Systems.GlobalActionDispatchSystem ActionDispatch { get; }

        /// <summary>
        /// ⭐ <c>CE-3123</c> (R-228) — applies <see cref="Fdp.Toolkit.Behavior.Diagnostics.PatchDebugStateCommand"/>s, the path the
        /// map's pin and AI-trace actions publish on. Part of <see cref="InteractionSystems"/>, so every host that runs the map runs
        /// it. ⚠ CGF and the Editor ALSO run one from <c>BehaviorDiagnosticsModule</c> (a headless Editor schedules no interaction
        /// systems, and its HTTP tracer patches too); a patch sets absolute values, so applying it twice yields the same state.
        /// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
        /// </summary>
        public Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem DebugStatePatch { get; } = new();

        /// <summary>
        /// The "View ▸ Tactical Map Layers…" control, registered with <see cref="GlobalManager"/>. Its panel
        /// needs the schema — a host's renderer registers it via <see cref="MapInteractionPack.RegisterGizmoSchemas"/>.
        /// </summary>
        public Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo LayerControl { get; }

        /// <summary>⭐ <c>CE-1017</c> — the map's entity-authoring surface (spawn adapter, picker, Add Entity action);
        /// null when the host passed no <c>MapInteractionContext.EntityAuthoring</c>. Draw its
        /// <see cref="Hrot.UI.Common.AddEntity.EntityAuthoring.DrawFrame"/> once per ImGui frame.</summary>
        public Hrot.UI.Common.AddEntity.EntityAuthoring? EntityAuthoring { get; }

        /// <summary>⭐ <c>CE-1017</c> — the canvas (empty-map) menu system, built here for every host (with the Add Entity
        /// submenu when <see cref="EntityAuthoring"/> exists). The host SCHEDULES it.</summary>
        public Hrot.Presentation.Systems.CanvasMenuUpdateSystem CanvasMenu { get; }

        // ══ UXI-11 — the SELECTION. 📄 UX_Feature_Selection.md §2.7 / §2.7.10 ═══════════════

        /// <summary>
        /// ⭐⭐⭐ <b>The one selection this map subsystem has</b> — a read-through VIEW over the
        /// <c>SelectionState</c> component, never a store. Panels read it; ⛔ nothing writes it except
        /// <see cref="SelectionRequests"/>.
        /// </summary>
        public Hrot.ScenarioEditor.Selection.EcsSelectionState Selection { get; }

        /// <summary>Turns map gestures — click, rubber-band, Delete — into selection changes.</summary>
        public Hrot.ScenarioEditor.Systems.SelectionInteractionSystem SelectionInteraction { get; }

        /// <summary>
        /// ⭐⭐⭐ <b>The marquee's state — built and DRAWN by the pack, so every host with a 2-D map has
        /// one.</b> 🔒 User ruling, <c>2026-09-20</c>: <i>"any perspective showing 2d map should support
        /// marquee and rubberband, not just editor and cgf."</i>
        /// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.16.
        ///
        /// <para>🔴 <b>What this closes, measured:</b> box-select LOGIC ran on all five hosts —
        /// <c>SelectionInteractionSystem</c> tracks the box and commits it — but only the editor and
        /// ReplayBrowser ever constructed a <c>RubberBandState</c> or registered a
        /// <c>RubberBandGizmo</c>. ⇒ on IG, SimHost and CGF the operator dragged a box that WORKED and
        /// was INVISIBLE, which reads as "nothing happened". ⛔ Not a design decision — no design record
        /// says those hosts should lack it; it is three composition roots that were never given it.</para>
        ///
        /// <para>⚠ A host may still pass its own through <c>MapInteractionContext.RubberBand</c> when it
        /// needs the handle early; the pack uses that instance rather than making a second one.</para>
        /// </summary>
        public Hrot.ScenarioEditor.Gizmos.RubberBandState RubberBand { get; }

        /// <summary>🔒 <b>The ONE writer</b> (§2.7.3 rule 1). Consumes every surface's request.</summary>
        public Hrot.ScenarioEditor.Systems.SelectionRequestSystem SelectionRequests { get; }

        /// <summary>Points the host's <c>IInspectorContext</c> at whatever the selection became.</summary>
        public Hrot.ScenarioEditor.Systems.SelectionNotificationSystem SelectionNotifications { get; }

        /// <summary>
        /// ⭐⭐⭐ <b>Schedule these two, IN THIS ORDER.</b> Requests must apply before the announcement is
        /// consumed, or a cause and its consequence land a frame apart.
        ///
        /// <para>⚠ The pack cannot enforce it — 🔒 <i>"pack constructs, host schedules"</i> — so it hands
        /// back an ordered pair instead of two properties a host could register the wrong way round.
        /// ⛔ <see cref="SelectionInteraction"/> is deliberately NOT in here: hosts tick it in different
        /// places (some inside their map update, some on the kernel), and that is pre-existing.</para>
        /// </summary>
        public IReadOnlyList<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> SelectionSystemsInOrder
            => new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[] { SelectionRequests, SelectionNotifications };

        /// <summary>The one buffer all three systems write into, and the terminal reads.</summary>
        public DebugPrimitiveBuffer Buffer { get; }

        /// <summary>The bus the interactive systems publish and subscribe on.</summary>
        public FdpEventBus InteractionBus { get; }

        /// <summary>Stateful gizmo definitions. Exposed so a host can register more after the fact.</summary>
        public GizmoRegistry GizmoRegistry { get; }

        /// <summary>
        /// Stateless projectors. ⚠ Exposed for inspection; registering here AFTER Build leaves the rule
        /// beyond <c>StatelessGizmoSystem</c>'s visibility cache — use
        /// <see cref="MapInteractionContext.ContributeExtras"/> instead.
        /// </summary>
        public StatelessGizmoRegistry StatelessRegistry { get; }

        /// <summary>The settings store backing every configurable gizmo (§3.2c).</summary>
        public GizmoSettingsRegistry Settings { get; }

        /// <summary>Non-entity-bound gizmos: placement, picker, layer control.</summary>
        public GlobalGizmoManager GlobalManager { get; }

        /// <summary>The drag handles — the tool half.</summary>
        public DataDrivenGizmoSystem DataDrivenSystem { get; }

        /// <summary>
        /// ⭐⭐⭐ <b><c>UXI-07</c> — THE ONE ARBITER of "which modal tool holds the interaction", for THIS
        /// map subsystem.</b> 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4 · §4.7c.
        ///
        /// <para>⛔⛔ <b>It lives HERE, and not behind <c>ToolActivationDrainSystem</c>, because the defect
        /// it closes is structural on every host that builds this pack.</b> 📐 Measured <c>2026-09-09</c>:
        /// <see cref="GlobalManager"/> and <see cref="DataDrivenSystem"/> each guard exclusivity only
        /// within themselves while sharing <see cref="InteractionBus"/>, so two "exclusive" tools can hold
        /// focus at once — and <c>Build</c> is called by <b>FIVE</b> hosts (IG, CGF, ReplayBrowser, SimHost,
        /// Editor). ⚠ Only two of them compose the drain, so a controller owned by the drain left the other
        /// three unarbitrated and hand-rolling the same gizmos inline.</para>
        ///
        /// <para>🔒 <b>ONE PER MAP SUBSYSTEM is the ruling, not a convenience:</b> <c>Q27-B</c> was answered
        /// <b>B1 — per subsystem</b> (<i>"perspective switch often means focus switch to another
        /// subsystem"</i>), and the question's own worked example is SimHost holding <c>Measure</c> while
        /// the user switches to CGF and back. ⇒ the arbiter's lifetime is the map's, which is exactly this
        /// object's lifetime.</para>
        ///
        /// <para>⭐ The pack also REGISTERS the six map tools on it — <see cref="Tools.ScenarioToolRegistrations"/>
        /// — so no host writes tool wiring of its own. A tool this host cannot service is still registered
        /// and reports why (🔒 no per-subsystem whitelist, user <c>2026-08-10</c>).</para>
        /// </summary>
        public Hrot.ScenarioEditor.Tools.ToolController Tools { get; }

        /// <summary>The map — every <c>[GizmoProjector]</c>.</summary>
        public StatelessGizmoSystem StatelessSystem { get; }

        /// <summary>🔒 <b>The host schedules THIS.</b> Holds the three systems in one togglable group.</summary>
        public TogglablePostSimulationGroup GizmoGroup { get; }

        /// <summary>The ref-counted gate. A viewer attaching calls <c>AddListener()</c>.</summary>
        public GizmoExecutionController Gate { get; }

        /// <summary>
        /// ⭐⭐ <b><c>S3</c>'s working half</b> — reports when the map is running and drawing nothing.
        /// Already the LAST member of <see cref="GizmoGroup"/>, so a host that schedules the group gets it
        /// automatically; nothing extra to remember, which is the point.
        /// </summary>
        public MapSelfCheckSystem SelfCheck { get; }

        /// <summary>
        /// ⭐⭐ <b><c>S3</c> — the systems a host must SCHEDULE for the map to produce anything.</b>
        ///
        /// <para>🔒 The pack constructs these; it cannot schedule them (it has no kernel), so declaring
        /// them is the only way the map can say what it needs. <c>DESIGN_Subsystem_Composition_Unification</c>
        /// §3.2's table: a bundle may <i>"DECLARE the systems its affordances require"</i> and must
        /// <i>"report unserviceable when the host does not run them"</i> — never silently no-op.</para>
        /// </summary>
        public IReadOnlyList<Type> RequiredSystems { get; } = new[]
        {
            typeof(GlobalGizmoManager),
            typeof(DataDrivenGizmoSystem),
            typeof(StatelessGizmoSystem),
            typeof(Hrot.Common.Systems.GlobalActionDispatchSystem),
            typeof(Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem),   // ⭐ CE-3123 — pins and AI-trace toggles
        };

        /// <summary>
        /// ⭐ What a host schedules for map INTERACTION, in order: the action dispatcher, the debug-state patch (⭐ CE-3123 — so a
        /// pin set by a menu action draws in the group that follows), then the gizmo group (the order every host used). Pass it to the interaction module AND to
        /// <see cref="Unserviceable"/>, so the two cannot disagree.
        /// </summary>
        public Fdp.ModuleHost.Abstractions.IEcsModuleSystem[] InteractionSystems
            => new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[] { ActionDispatch, DebugStatePatch, GizmoGroup };

        /// <summary>
        /// ⭐⭐ Returns one message per required system the host did not schedule — empty when the host
        /// runs them all.
        ///
        /// <para>⚠⚠ <b>Scope, stated honestly (§3.2e).</b> This catches a host that never schedules the
        /// map. It would <b>NOT</b> have caught <c>CE-123</c>: SimHost scheduled the group
        /// (<c>SimHostApp.cs:442</c>), all three systems were present and the gate was open — and the map
        /// still drew 3 non-<c>Line</c> primitives for 8 entities, because one of them had been handed a
        /// predicate that suppressed everything. A run-set check cannot see a system that is present and
        /// told to do nothing; <c>MapSelfCheckSystem</c> is what sees that.</para>
        /// </summary>
        /// <param name="hostRunSet">
        /// What the host actually scheduled. ⭐ Pass <see cref="TogglablePostSimulationGroup.GetSystems"/>
        /// of the group you registered, so the answer comes from the object the kernel got — not from a
        /// second list that can drift away from it.
        /// </param>
        public IReadOnlyList<string> Unserviceable(IEnumerable<object>? hostRunSet)
        {
            var scheduled = new HashSet<Type>();
            Collect(hostRunSet, scheduled, depth: 0);

            var missing = new List<string>();
            foreach (Type required in RequiredSystems)
            {
                if (scheduled.Contains(required)) continue;
                missing.Add(
                    $"map system '{required.Name}' is constructed but NOT scheduled — "
                  + Reason(required));
            }
            return missing;
        }

        /// <summary>
        /// ⭐⭐ Flattens togglable groups, so a host may pass exactly what it handed the kernel.
        ///
        /// <para>⚠ Without this the answer would be a FALSE ALARM: hosts register the GROUP, not its three
        /// members, so a type-level comparison against the registration list would report all three
        /// missing. Flattening is what makes <i>"pass what you scheduled"</i> a truthful instruction —
        /// and it keeps the genuinely useful answer available: a host that never put the group in its
        /// registration list gets all three reported.</para>
        /// </summary>
        private static void Collect(IEnumerable<object>? systems, HashSet<Type> into, int depth)
        {
            if (systems is null || depth > 4) return;   // depth guard: groups do not nest deeply

            foreach (object system in systems)
            {
                if (system is null) continue;
                into.Add(system.GetType());

                if (system is TogglablePostSimulationGroup group)
                    Collect(group.GetSystems(), into, depth + 1);
            }
        }

        private static string Reason(Type system)
            => system == typeof(StatelessGizmoSystem)
                ? "the map draws no entity shapes, routes, tactical areas or overlays at all"
             : system == typeof(DataDrivenGizmoSystem)
                ? "no drag handles or vertex editing appear on any entity"
             : system == typeof(GlobalGizmoManager)
                ? "screen-space gizmos (placement, picker, layer control) never draw"
             : system == typeof(Hrot.Common.Systems.GlobalActionDispatchSystem)
                ? "map menu actions (layer control, centre on entity, rotate…) do nothing"
             : system == typeof(Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem)
                ? "gizmo pins and AI-trace toggles from the map menu are published and never applied"
             : "part of the map will be silently absent";
    }
}
