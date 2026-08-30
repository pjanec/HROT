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
    /// <para>⚠ <c>RequiredSystems</c> and <c>Unserviceable(hostRunSet)</c> appear on this box in §3.2b's
    /// diagram but belong to <b><c>S3</c></b> (declare + report). They are deliberately NOT built here —
    /// filed rather than half-built, so nobody mistakes an absent method for an absent intent.</para>
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
            GizmoExecutionController gate)
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
    }
}
