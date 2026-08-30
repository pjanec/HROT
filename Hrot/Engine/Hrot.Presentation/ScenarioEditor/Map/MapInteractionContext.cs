using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ <b>The registries a host may contribute to, handed out by <see cref="MapInteractionPack"/> at
    /// the one moment when contributing is safe.</b>
    ///
    /// <para>⚠⚠ <b>The moment matters, and getting it wrong fails silently.</b>
    /// <c>StatelessGizmoSystem</c>'s constructor sizes its visibility cache from
    /// <c>registry.Rules.Count</c> (<c>:49-50</c>), and <c>Execute</c>'s guard is
    /// <c>if (r &lt; cache.Length &amp;&amp; !cache[r]) continue;</c> — so a rule registered AFTER the
    /// system exists lands beyond the cache and <b>ignores its visibility policy entirely</b>. Not a
    /// crash; a silent semantic difference. The pack invokes
    /// <see cref="MapInteractionContext.ContributeExtras"/> before constructing the systems, which closes
    /// that hazard by construction instead of by each host remembering it.</para>
    /// </summary>
    public sealed class MapInteractionRegistries
    {
        internal MapInteractionRegistries(
            GizmoRegistry gizmos,
            StatelessGizmoRegistry stateless,
            GizmoSettingsRegistry settings,
            DebugPrimitiveBuffer buffer,
            FdpEventBus interactionBus)
        {
            Gizmos         = gizmos;
            Stateless      = stateless;
            Settings       = settings;
            Buffer         = buffer;
            InteractionBus = interactionBus;
        }

        /// <summary>Stateful gizmo definitions — the tool half (<c>UXI-07</c>'s territory).</summary>
        public GizmoRegistry Gizmos { get; }

        /// <summary>Stateless projectors — the map half.</summary>
        public StatelessGizmoRegistry Stateless { get; }

        /// <summary>The shared settings store (§3.2c: standalone and injectable, never per-host state).</summary>
        public GizmoSettingsRegistry Settings { get; }

        /// <summary>The one buffer every gizmo system writes into.</summary>
        public DebugPrimitiveBuffer Buffer { get; }

        /// <summary>The interaction bus the constructed systems will use.</summary>
        public FdpEventBus InteractionBus { get; }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The input to <see cref="MapInteractionPack.Build"/> — <c>UXI-23</c> <c>S2b</c>.</b>
    /// 📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.2, §3.2a, §3.2b, ⭐ **§3.2d (the amendments)**.
    ///
    /// <para>🔒 <b>It deliberately carries NO <c>ModuleHostKernel</c>, and that is the whole point.</b>
    /// The user's ruling is <i>"pack owns construction, host decides scheduling"</i>, and
    /// <c>DESIGN_Subsystem_Composition_Unification.md</c> §3.2 forbids any shared bundle from registering
    /// a module, a global system, a translator or a participant — because the run-set follows the host's
    /// ROLE. Withholding the kernel makes that violation <b>unreachable rather than merely forbidden</b>,
    /// which is the same technique <c>UiBundleContext</c> uses.</para>
    /// </summary>
    public sealed class MapInteractionContext
    {
        /// <summary>The world the gizmo systems will run over. Required.</summary>
        public required EntityRepository World { get; init; }

        /// <summary>
        /// The interaction bus. Optional: when null the pack creates one and runs
        /// <c>InteractionEventRegistry.RegisterAll</c> over it, which is what every host does by hand today.
        /// </summary>
        public FdpEventBus? InteractionBus { get; init; }

        /// <summary>
        /// The settings store. Optional: when null the pack creates a private one.
        /// ⭐ §3.2c — a host may pass a SHARED instance (a "2D map station" persisting its own file), or its
        /// own, or an empty one. That choice belongs to whoever owns the instance, not to the pack.
        /// </summary>
        public GizmoSettingsRegistry? Settings { get; init; }

        /// <summary>
        /// Which entities carry drag handles. ⭐ Genuinely per-host: a dumb terminal draws handles for
        /// everything (<c>null</c>), an editor draws them only on the selection.
        ///
        /// <para>⛔ This gates <c>DataDrivenGizmoSystem</c> ONLY. There is deliberately no stateless
        /// equivalent here: <c>S2a</c> measured that SimHost passed this same predicate to
        /// <c>StatelessGizmoSystem</c> as well, where it acts as one blanket gate over every projector and
        /// left the map dark (<c>CE-123</c>). The map is not gated by selection.</para>
        /// </summary>
        public Func<ISimulationView, Entity, bool>? IsSelectedPredicate { get; init; }

        /// <summary>
        /// 🔴 <b>Whether the gizmo group starts enabled.</b> Default <c>false</c> — <c>GZH-003</c>
        /// headless-first.
        ///
        /// <para>⚠⚠ <b>This exists because "start disabled everywhere" was measured unsafe.</b> The only
        /// production driver of <c>GizmoExecutionController.AddListener()</c> is
        /// <c>PerspectiveCoordinatorSystem</c>; <c>LocalTerminalModule</c> and
        /// <c>GizmoCapabilitiesTracker</c> are registered by no host at all. So a standalone IG or editor
        /// has no viewer-attach path, and starting disabled would shut their gate permanently — which is
        /// exactly why both hard-set <c>Enabled = true</c> today.</para>
        ///
        /// <para>⭐ The per-host TRUTH survives as one named input; only the four scattered literals die.
        /// 🔒 <c>R-137</c>: unification removes the duplication, not the capability. §3.2d ①.</para>
        /// </summary>
        public bool StartEnabled { get; init; }

        /// <summary>
        /// ⭐ Primitive-buffer capacity. Default matches <c>DebugPrimitiveBuffer</c>'s own default; IG asks
        /// for <c>4096</c> because it draws the richest frame. 🔒 <c>R-137</c>: a per-host number that was
        /// a constructor argument stays a per-host number, named once.
        /// </summary>
        public int? BufferCapacity { get; init; }

        /// <summary>
        /// Optional breakpoint manager, forwarded to the two interactive systems. Host-supplied because
        /// only the editor and the replay browser have one.
        /// </summary>
        public Fdp.Toolkit.Diagnostics.Gizmos.IActiveViewProvider? BreakpointManager { get; init; }

        /// <summary>
        /// ⭐⭐ <b>The host's own gizmos.</b> Invoked AFTER the reflection pass and BEFORE the systems are
        /// constructed — see <see cref="MapInteractionRegistries"/> for why that ordering is load-bearing.
        ///
        /// <para>📐 Four hosts need this, for projectors reflection cannot find: <c>EntityEditorLabelGizmo</c>
        /// and <c>EntityEditorPolylineGizmo</c> (deliberately attribute-less — their constructors need a
        /// <c>BehaviorRegistry</c>), <c>RubberBandGizmo</c>, <c>ReplaySpatialBoundsGizmo</c>,
        /// <c>LayerControlGizmo</c>, <c>EntityDragGizmoDefinition</c>.</para>
        /// </summary>
        public Action<MapInteractionRegistries>? ContributeExtras { get; init; }
    }
}
