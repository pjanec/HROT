using System;
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S2b</c> — the ONE place the map's machinery is constructed.</b>
    /// 📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.2 · §3.2a · §3.2b (UML) · ⭐ **§3.2d (the
    /// three amendments measured before building)**.
    ///
    /// <para>📐 <b>What it replaces.</b> Five hosts built the same buffer, the same two registries, the
    /// same reflection call, the same three systems, the same togglable group and the same gate — by hand,
    /// in five composition roots: <c>SimHostApp</c> · <c>CgfSubsystem</c> · <c>IgApplication</c> ·
    /// <c>EditorSubsystem</c> · <c>ReplayBrowserSubsystem</c>.</para>
    ///
    /// <para>🔴🔴 <b>Why that mattered, concretely.</b> <c>S2a</c> measured that the difference between a
    /// working map and a dark one was <b>a single constructor argument</b> that one host passed and four
    /// did not (<c>CE-123</c>: SimHost handed <c>StatelessGizmoSystem</c> the selection predicate meant for
    /// the drag handles). Five hand-written constructions are five chances to get an argument wrong, and
    /// nothing reports it. This pack is what removes those chances.</para>
    ///
    /// <para>🔒 <b>The pack CONSTRUCTS; the HOST SCHEDULES.</b> User ruling, <c>2026-08-28</c>. Enforced
    /// structurally: <see cref="MapInteractionContext"/> carries no <c>ModuleHostKernel</c>, so scheduling
    /// is unreachable from here rather than merely forbidden — the run-set follows the host's ROLE
    /// (<c>DESIGN_Subsystem_Composition_Unification.md</c> §3.1/§3.2).</para>
    ///
    /// <para>⛔ <b>Equally deliberately NOT here:</b> DDS publishers, ingress/egress translators, action
    /// registries, selection systems, canvas menus and layer-control gizmos. Those are the host's role or
    /// its own affordances; a host adds its gizmos through
    /// <see cref="MapInteractionContext.ContributeExtras"/>.</para>
    /// </summary>
    public static class MapInteractionPack
    {
        /// <summary>
        /// Constructs the map's gizmo machinery and hands it back for the host to schedule.
        /// </summary>
        public static MapInteraction Build(MapInteractionContext ctx)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.World is null) throw new ArgumentException("MapInteractionContext.World is required.", nameof(ctx));

            var buffer = ctx.BufferCapacity is int capacity
                ? new DebugPrimitiveBuffer(capacity)
                : new DebugPrimitiveBuffer();

            // Every host either creates a bus and registers the interaction events over it, or already has
            // one. Doing it here means a host cannot forget the RegisterAll half.
            var bus = ctx.InteractionBus;
            if (bus is null)
            {
                bus = new FdpEventBus();
                Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(bus);
            }

            var settings          = ctx.Settings ?? new GizmoSettingsRegistry();
            var gizmoRegistry     = new GizmoRegistry();
            var statelessRegistry = new StatelessGizmoRegistry();

            // ST-031: one reflection pass replaces the five hand-rolled per-host family lists. Uniform
            // membership; component presence decides what actually draws.
            GizmoReflectionRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settings);

            // ⚠⚠ ORDERING IS LOAD-BEARING (§3.2d ③). The host's own gizmos go in AFTER reflection and
            // BEFORE the systems are constructed, because StatelessGizmoSystem sizes its visibility cache
            // from registry.Rules.Count — a rule added later lands beyond the cache and silently ignores
            // its visibility policy. Giving hosts this one window closes the hazard by construction.
            ctx.ContributeExtras?.Invoke(
                new MapInteractionRegistries(gizmoRegistry, statelessRegistry, settings, buffer, bus));

            var globalManager = new GlobalGizmoManager(
                buffer, bus, breakpointManager: ctx.BreakpointManager);

            var dataDriven = new DataDrivenGizmoSystem(
                gizmoRegistry,
                buffer,
                isSelectedPredicate: ctx.IsSelectedPredicate,
                interactionBus: bus,
                breakpointManager: ctx.BreakpointManager);

            // 🔒 NO isSelectedPredicate here, ever. On StatelessGizmoSystem the predicate is ONE BLANKET
            // GATE over every projector the host owns — the entity avatars, the routes, the tactical areas,
            // the map overlay. That is CE-123, and the rail TheMapIsNotSelectionGatedRails pins it.
            var stateless = new StatelessGizmoSystem(statelessRegistry, buffer);

            // Group member order follows the four hosts that agree (global manager, drag handles, map);
            // the editor listed the first two the other way round, which nothing depended on.
            //
            // ⭐⭐ S3: MapSelfCheckSystem goes LAST, so it observes the frame the other three just wrote.
            // Putting it in the group rather than asking hosts to schedule it separately is deliberate —
            // a diagnostic a host can forget to wire is a diagnostic that is absent exactly where it is
            // needed. §3.2e.
            // ⚠ The group's members are constructor-only, and the self-check needs to know whether the
            // group is enabled — hence the delegate and the deferred assignment rather than a circular
            // constructor pair.
            TogglablePostSimulationGroup? groupRef = null;
            var selfCheck = new MapSelfCheckSystem(
                buffer, () => groupRef?.Enabled ?? false, ctx.ReportMapDiagnostic);

            var group = new TogglablePostSimulationGroup(
                "GizmoExecution", globalManager, dataDriven, stateless, selfCheck);
            groupRef = group;

            // 🔴 GZH-003 headless-first, but NOT "disabled for everyone" (§3.2d ①): the only production
            // driver of AddListener() is PerspectiveCoordinatorSystem, so a standalone IG or editor has no
            // viewer-attach path and would sit behind a permanently shut gate. The per-host truth survives
            // as this one named input; what dies is the four scattered literals.
            group.Enabled = ctx.StartEnabled;

            var gate = new GizmoExecutionController(group, globalManager, dataDriven);

            return new MapInteraction(
                buffer, bus, gizmoRegistry, statelessRegistry, settings,
                globalManager, dataDriven, stateless, group, gate, selfCheck);
        }
    }
}
