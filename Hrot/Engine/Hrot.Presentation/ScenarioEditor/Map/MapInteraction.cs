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
            MapSelfCheckSystem selfCheck)
        {
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
        };

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
             : "part of the map will be silently absent";
    }
}
