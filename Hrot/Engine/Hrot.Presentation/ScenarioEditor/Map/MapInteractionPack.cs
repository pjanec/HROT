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
    /// registries, canvas menus and layer-control gizmos. Those are the host's role or
    /// its own affordances; a host adds its gizmos through
    /// <see cref="MapInteractionContext.ContributeExtras"/>.</para>
    ///
    /// <para>⚠⚠ <b>AMENDED <c>2026-09-20</c>: <i>"selection systems"</i> WAS on that exclusion list and is
    /// now BUILT HERE.</b> 🔒 User: <i>"we want to unify across host also the bootstrap code as far as
    /// possible, including this entity selection stuff."</i> 📐 The exclusion was written when selection
    /// genuinely was host-shaped — five hosts, three different parallel stores, five hand-rolled writers.
    /// <c>UXI-11</c> <c>S-1</c>…<c>S-3b</c> made the wiring <b>identical on every host</b>, at which point
    /// the exclusion preserved exactly the five-way duplication this pack exists to remove. ⭐ The ruling
    /// it was protecting is untouched: <b>the pack constructs, the host schedules.</b>
    /// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.10.</para>
    /// </summary>
    /// <summary>⭐ CE-1033 — what <see cref="MapInteractionPack.AttachMapLayers"/> attached to a host's canvas.</summary>
    public sealed record MapLayers(
        Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer GizmoLayer,
        Fdp.Toolkit.Vis3D.MapViewSwitch ViewSwitch,
        Fdp.Toolkit.Vis3D.TerrainLayer3D Terrain,
        Hrot.UI.Common.Map3D.EntityBodyLayer3D Bodies,
        Hrot.UI.Common.Effects.EffectLayer Effects,
        Hrot.UI.Common.Effects.ProjectileLayer? Projectiles = null,
        Hrot.UI.Common.Map3D.PossessionStation? Possession = null,
        Hrot.UI.Common.Audio.AudioLayer? Audio = null,
        Hrot.UI.Common.Map2D.TerrainLayer2D? Terrain2D = null,
        Fdp.Toolkit.Vis2D.Layers.GridMapLayer? Grid = null,
        MapCursorReadout? Cursor = null);

    public static class MapInteractionPack
    {
        /// <summary>
        /// The policy every projector gets unless the host says otherwise: culling for the entity
        /// projector (itself gated by a setting), the framework default for everything else.
        /// </summary>
        private static Func<Type, IGizmoVisibilityPolicy?> DefaultVisibilityPolicy(GizmoSettingsRegistry settings)
        {
            var culling = new CullingStateVisibilityPolicy(settings);
            // ⭐ CE-3120 (R-227) — one GizmoFamilyVisibilityPolicy per family, attached to every projector that names one, so the
            //   family's scope (all / selected or pinned) and the per-unit pins decide. 📄 DESIGN_Terrain_Combat_Tuning.md §5b.
            var families = new System.Collections.Generic.Dictionary<Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags, GizmoFamilyVisibilityPolicy>();
            return type =>
            {
                if (type == typeof(Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo)) return culling;
                var family = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<GizmoProjectorAttribute>(type)?.Family
                             ?? Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.None;
                if (family == Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.None) return null;
                if (!families.TryGetValue(family, out var policy))
                    families[family] = policy = new GizmoFamilyVisibilityPolicy(settings, family);
                return policy;
            };
        }

        /// <summary>
        /// Constructs the map's gizmo machinery and hands it back for the host to schedule.
        /// </summary>
        /// <summary>⭐ CE-1203 FL-8 — the layer control <see cref="Build"/> made for each map buffer. Every host hands
        /// <see cref="AttachMapLayers"/> the buffer Build made (<c>MapInteraction.Buffer</c>), so the view switch reaches the layer control
        /// with no host wiring to forget (rail <c>CE1203_TheLayerControl_FollowsTheView_OnEveryMapHost</c>).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DebugPrimitiveBuffer, Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo>
            LayerControlOf = new();

        /// <summary>⭐ CE-4038 B10 — the layer control of a map buffer (the film director's override and <c>/map/layers</c> reach it here).</summary>
        public static Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo? LayerControlFor(DebugPrimitiveBuffer buffer)
            => LayerControlOf.TryGetValue(buffer, out var g) ? g : null;

        /// <summary>⭐ CE-1234 — the local UI-state transport <see cref="Build"/> made for each map buffer; <see cref="AttachMapLayers"/> hands it
        /// to the gizmo render layer, whose panel drains it.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DebugPrimitiveBuffer, Fdp.Toolkit.Diagnostics.Gizmos.Hub.LocalGizmoUiStateTransport>
            UiStatesOf = new();

        public static MapInteraction Build(MapInteractionContext ctx)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.World is null) throw new ArgumentException("MapInteractionContext.World is required.", nameof(ctx));

            var buffer = new DebugPrimitiveBuffer(ctx.BufferCapacity ?? MapInteractionContext.DefaultBufferCapacity);

            // ⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-1) — the HOST's actions: its registry, its interaction bus (the events
            //   registered over it) and its dispatch. The host made them at its start and passes them; a test gets fresh ones.
            var host = ctx.Host ?? SharedHostActions.Create();
            var bus = host.InteractionBus;

            // ⭐ CE-3123 (R-228) — the debug view state the pin and AI-trace actions patch, on every map host.
            Hrot.Map.Common.PresentationComponentRegistry.RegisterDebugViewState(ctx.World);
            // ⭐ CE-1042 E2 — the effect entities' components, so the effect systems below can run on every map host.
            Hrot.Map.Common.PresentationComponentRegistry.RegisterEffects(ctx.World);

            var settings          = ctx.Settings ?? new GizmoSettingsRegistry();
            var gizmoRegistry     = new GizmoRegistry();
            var statelessRegistry = new StatelessGizmoRegistry();

            // ST-031: one reflection pass replaces the five hand-rolled per-host family lists. Uniform
            // membership; component presence decides what actually draws.
            // ⭐⭐ S4: the resolver is how a reflection-discovered projector gets a visibility policy.
            // The DEFAULT attaches CullingStateVisibilityPolicy to the entity projector — the policy itself
            // is off unless the host sets map.entity.cullOffscreen, so this changes no behaviour until
            // asked. A host resolver attaches its own policy to any projector it names (layered below).
            // 📄 UX_Feature_Map_Parity.md §3.2f · UX_Feature_Entity_Symbology.md §3.4.
            // ⭐ CE-3120 — the host's resolver is LAYERED over the default (it wins for the types it answers), so a host override
            //   does not silently drop the family policies.
            var defaults = DefaultVisibilityPolicy(settings);
            var hostResolve = ctx.VisibilityPolicyResolver;
            Func<Type, IGizmoVisibilityPolicy?> resolve = hostResolve == null ? defaults : type => hostResolve(type) ?? defaults(type);
            // ⭐ CE-3123 (R-228) — a projector's constructor services come from the host's Services; one the host lacks is reported.
            GizmoReflectionRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settings, resolve,
                services: ctx.Services,
                // ⚠ The "this host cannot serve it" channel, as for tools — NOT ReportMapDiagnostic, the self-check's "the map
                //   draws nothing" channel (MapInteractionContext.ReportUnserviceableTool says why the two stay apart).
                reportUnserviceable: ctx.ReportUnserviceableTool
                    ?? (m => Fdp.Core.Logging.FdpLog<MapInteraction>.Info("[Map] " + m)));

            // ⚠⚠ ORDERING IS LOAD-BEARING (§3.2d ③). The host's own gizmos go in AFTER reflection and
            // BEFORE the systems are constructed, because StatelessGizmoSystem sizes its visibility cache
            // from registry.Rules.Count — a rule added later lands beyond the cache and silently ignores
            // its visibility policy. Giving hosts this one window closes the hazard by construction.
            ctx.ContributeExtras?.Invoke(
                new MapInteractionRegistries(gizmoRegistry, statelessRegistry, settings, buffer, bus));

            // ⭐⭐⭐ R-144 / §6.2b — THE ONE FOCUS SLOT, created HERE because this is the only production
            //    site that builds both arbiters. 📐 The defect it closes is named two comments below and
            //    was written in this file long before the ruling: the two systems "each guard exclusivity
            //    only within themselves while sharing `bus`, so two 'exclusive' tools can hold focus at
            //    once."  ⛔ One registry EACH would satisfy every signature and fix nothing — the whole of
            //    68-A is that this single instance reaches both constructors.
            // ⚠ Q27-B, "per subsystem": the slot's lifetime is the map's, which is this object's.
            var focus = new GizmoFocusRegistry();

            var globalManager = new GlobalGizmoManager(
                buffer, bus, breakpointManager: ctx.BreakpointManager, focus: focus);

            var dataDriven = new DataDrivenGizmoSystem(
                gizmoRegistry,
                buffer,
                isSelectedPredicate: ctx.IsSelectedPredicate,
                interactionBus: bus,
                breakpointManager: ctx.BreakpointManager,
                focus: focus);

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

            // ⭐⭐⭐ CE-259r — THE ORDER IS LOAD-BEARING, AND `stateless` MUST PRECEDE `dataDriven`.
            //   📐 Measured 2026-09-10: the host clears the primitive buffer (EndFrame) immediately before
            //     ticking this group, so a hit-test dispatched INSIDE the group sees only what earlier
            //     members have emitted THIS frame. With dataDriven first, EntityPickerGizmo's hover
            //     hit-tested an EMPTY buffer (frame=0, anchored=0) => Entity.Null every frame =>
            //     _hoveredValid never true => amber crosshair, and its left-release pick arm unreachable.
            //     ⛔ NOT a "partly filled frame" problem — the buffer is EMPTY; it is pure ordering.
            //   ⭐ The entity pick boxes come from the STATELESS projector (EntityPresentationGizmo), so
            //     running it first is what makes the picker's hover resolve.
            //   🔴🔴🔴 CORRECTED 2026-09-10, SAME DAY, BY AN OPERATOR RUN. The first fix put `stateless`
            //     second (after globalManager) and DID NOT WORK — the crosshair stayed amber.
            //     📐 CAUSE: the production picker is registered on ToolArbiter.Global
            //     (PickerToolHost.cs:116, g.Register at :173/:272), so its hover hit-test runs inside
            //     EntityPickerGizmo.OnDragUpdate (:141-146) dispatched by GLOBALMANAGER — the FIRST group
            //     member — not by dataDriven. Swapping stateless past dataDriven alone left globalManager
            //     ahead of it, so the buffer was still empty.
            //     ⛔ MY HEADLESS PROBE PUT THE PICKER IN dataDriven and therefore measured a scenario that
            //     does not exist in production. That is the same mistake the CE-259q rail made and wrote
            //     down as a lesson — "mirror the ARBITERS, not just the gesture" — repeated.
            //     ⇒ `stateless` must precede EVERY arbiter that dispatches interactions, so it goes FIRST.
            //   ⚠⚠ This ALSO flips the z-order tiebreak: emission order breaks DebugLayer ties
            //     (DebugGizmoLayer.cs:510) and pick boxes + tool handles are both layer 0 => a HANDLE now
            //     beats an entity box. 🔒 Ruled CORRECT for an ACTIVE tool (user, 2026-09-10) — and safe
            //     only because §4.7i makes a SUSPENDED tool draw no handles at all. ⛔ Do not reorder this
            //     back, and do not ship it without §4.7i.
            //   📄 docs/UX/UX_Feature_Tool_Model.md §4.7g.1 (the frame as a sequence) and §4.7i.
            var group = new TogglablePostSimulationGroup(
                "GizmoExecution", stateless, globalManager, dataDriven, selfCheck);
            groupRef = group;

            // ⭐⭐⭐ UXI-07 step 3b — THE ONE ARBITER, built here so all FIVE hosts get it.
            // 📐 The defect is structural in the two lines above: globalManager and dataDriven each guard
            //    exclusivity only within themselves while sharing `bus`, so two "exclusive" tools can hold
            //    focus at once. ⇒ the arbiter belongs where the pair is CONSTRUCTED, not behind a system
            //    only two of the five hosts compose.
            // 🔒 Q27-B: "per subsystem" — the arbiter's lifetime is the map's, which is this object's.
            // ⭐ Registering the tool set here too is what makes the user's 2026-08-10 ruling true by
            //    construction: "all map subsystems share the FULL tool set … never set membership."
            //    A host that cannot service one still has it, and it REPORTS why (ruling 49).
            Hrot.UI.Common.AddEntity.EntityAuthoring? authoringRef = null;
            var tools = new Hrot.ScenarioEditor.Tools.ToolController(
                () => globalManager, () => dataDriven, ctx.ReportUnserviceableTool);

            Hrot.ScenarioEditor.Tools.ScenarioToolRegistrations.RegisterAll(
                tools,
                world:               () => ctx.World,
                gizmos:              () => dataDriven,
                globalGizmos:        () => globalManager,
                // ⭐ CE-1017 — a host that passes EntityAuthoring gets the Spawn tool from the SAME shared adapter;
                //   resolved at call time (the authoring surface is built below, its adapter on first use).
                startPlacementMode:  ctx.StartPlacementMode
                                     ?? (ctx.EntityAuthoring is null ? null : () => authoringRef?.Spawn?.ArmPlacement()),
                reportUnserviceable: ctx.ReportUnserviceableTool,
                measureUnits:        ctx.MeasureUnits);

            // 🔴 GZH-003 headless-first, but NOT "disabled for everyone" (§3.2d ①): the only production
            // driver of AddListener() is PerspectiveCoordinatorSystem, so a standalone IG or editor has no
            // viewer-attach path and would sit behind a permanently shut gate. The per-host truth survives
            // as this one named input; what dies is the four scattered literals.
            group.Enabled = ctx.StartEnabled;

            var gate = new GizmoExecutionController(group, globalManager, dataDriven);

            // ⭐⭐⭐ UXI-11 — THE SELECTION, BUILT HERE SO ALL FIVE HOSTS GET THE SAME ONE.
            // 🔒 User, 2026-09-20: "unify across host also the bootstrap code as far as possible,
            //    including this entity selection stuff."
            // 📐 What this replaces: five composition roots each constructing EcsSelectionState, the
            //    interaction system, the request system and the notification system in their own order,
            //    plus — before S-1..S-3b — three different parallel stores. Same disease as the buffer
            //    and the three gizmo systems this pack was written for, and the same cure.
            // ⛔ The pack still CONSTRUCTS ONLY. Scheduling stays the host's, per the 2026-08-28 ruling
            //   and enforced structurally: MapInteractionContext carries no kernel.
            var selection = new Hrot.ScenarioEditor.Selection.EcsSelectionState(ctx.World);

            // ⭐⭐⭐ THE MARQUEE, BUILT AND DRAWN HERE SO ALL FIVE HOSTS HAVE ONE.
            // 🔒 User ruling, 2026-09-20: "any perspective showing 2d map should support marquee and
            //    rubberband, not just editor and cgf."
            // 🔴 MEASURED, and the shape is sharper than "it is missing": the box-select LOGIC already
            //    ran everywhere — SelectionInteractionSystem tracks _isBoxSelecting and commits the box
            //    on all five hosts. What only the editor and ReplayBrowser had was the STATE OBJECT and
            //    the GIZMO THAT DRAWS IT. ⇒ on IG, SimHost and CGF the drag worked and was invisible.
            // ⭐ Same cure as S-3c: the thing every host needs is constructed in the one place that
            //    builds the map, not in five composition roots three of which forgot.
            // ⚠ ctx.RubberBand is still honoured — a host that needs the handle before Build() returns
            //   passes its own and the pack adopts it rather than making a second one.
            var rubberBand = ctx.RubberBand ?? new Hrot.ScenarioEditor.Gizmos.RubberBandState();
            MapUiStates.Set(ctx.World, rubberBand);   // ⭐ CE-1136 — the 3-D view lends it its projection (RubberBandOverlay3D)
            statelessRegistry.RegisterGlobal(new Hrot.ScenarioEditor.Gizmos.RubberBandGizmo(rubberBand));

            var selectionInteraction = new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(
                ctx.World, bus, rubberBand, selection);
            if (ctx.OnMapSelectionChanged != null)
                selectionInteraction.OnSelectionChanged += ctx.OnMapSelectionChanged;

            // ⚠ ORDER IS LOAD-BEARING and the pack cannot enforce it — the host schedules. Requests must
            //   apply BEFORE the announcement is consumed, or a cause and its consequence land a frame
            //   apart. MapInteraction.SelectionSystemsInOrder exists so a host cannot get it wrong.
            var selectionRequests = new Hrot.ScenarioEditor.Systems.SelectionRequestSystem(() => selection);

            // ⭐⭐⭐ UXI-11 S-5 — ruling ②: "if entity becomes unselected, it should cancel any editing on
            //   the entity losing the selection." The arbiter and the store are BOTH built right here, so
            //   the hook is a constructor argument rather than a per-host wiring step — all five hosts get
            //   it, in one place, exactly as S-3c did for the selection itself.
            // 🔒 THE SILENT-DEFAULT RULE, applied: both parameters are optional so a lightweight host or a
            //   test need not supply them, but this caller HOLDS them and therefore PASSES them.
            // ⭐⭐⭐ CE-300 — the AI editors' entity cell joins the SAME announcement, as a third
            //   consequence. 📄 DESIGN_Editor_Entity_Selection_Source.md §3.1 / §4.
            // ⛔ NOT a fourth system: this class already IS "what a selection change causes", and a
            //   parallel consumer of one edge would be two implementations of one concept (ruling 9) —
            //   the same argument S-5 made for putting CancelArmedOn here.
            var selectionNotifications = new Hrot.ScenarioEditor.Systems.SelectionNotificationSystem(
                ctx.Inspector ?? (static () => null), tools, selection, ctx.AiEntitySelection);

            // ⭐⭐ MAP ACTIONS + THE LAYER CONTROL, on every map host (user, 2026-10-07: "pls unify and share, as usual"). ⭐ CE-1150 (§12
            //    HA-2) — registered on the HOST's registry (ctx.Host), which the map no longer owns; the actions that need no map (Select,
            //    Center View, pins, AI trace) are SharedHostActions'. ⛔ Construct only — the host schedules ActionDispatch.
            var actions        = host.Actions;
            var actionDispatch = host.Dispatch;
            long layerControlId = GlobalGizmoManager.NewId();
            // ⭐ CE-1203 FL-9 — the operator's remembered layers into the shared settings BEFORE the gizmo reads them; saved on every change
            var layerSettingsPath = ctx.LayerSettingsPath ?? MapLayerPreferences.DefaultPath();
            MapLayerPreferences.Load(settings, layerSettingsPath);
            // ⭐ CE-1234 — the layer panel learns what the gizmo holds: its state goes to the host's publisher (a remote terminal) AND to a
            //   local transport the map's own panel drains each frame (AttachMapLayers). ⛔ Was: the host's hub only, which no local endpoint
            //   ever joined — the panel showed only its own clicks, so in 3-D (its own copy still read "2-D map") every click was dropped.
            var uiStates = new Fdp.Toolkit.Diagnostics.Gizmos.Hub.LocalGizmoUiStateTransport();
            var layerUi = new Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub();
            layerUi.AddEndpoint(uiStates);
            if (ctx.GizmoUiPublisher != null) layerUi.AddEndpoint(ctx.GizmoUiPublisher);
            UiStatesOf.AddOrUpdate(buffer, uiStates);
            var layerControl = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
                layerControlId, bus, new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
                layerUi, settings,   // ⭐ CE-3120 — the panel edits the family scopes in the shared settings
                saveSettings: () => MapLayerPreferences.Save(settings, layerSettingsPath));
            globalManager.Register(layerControlId, layerControl);
            LayerControlOf.AddOrUpdate(buffer, layerControl);   // ⭐ CE-1203 FL-8 — AttachMapLayers finds it by the buffer and hands it the view
            // ⭐ CE-1227 — the gizmo hands the buffer its mask only when it runs, AFTER the projectors: 📐 on demo-city the first frame
            //   therefore stored the hidden Cover (7 891 points) and SpatialGrid layers and dropped 17 943 primitives. Seeded here, the
            //   first frame refuses them like every later one (CE-1071's one authority, applied from the start).
            buffer.SetActiveLayers(layerControl.ActiveLayers);
            actions.Register(Hrot.Common.Constants.GlobalActionIds.OpenLayerControl, (_, _) =>
                bus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent()));
            // ⭐ CE-1033 — View › 2-D / 3-D Map, on every map host; AttachMapLayers' view switch drains the event.
            actions.Register(Hrot.Common.Constants.GlobalActionIds.ToggleMap3D, (_, _) =>
                bus.Publish(new Hrot.Common.Diagnostics.Gizmos.ToggleMap3DEvent()));
            // ⭐ CE-1201 (DESIGN_Entity_Control §8p OS-1) — View › First Person: Over the Shoulder; the view-mode layer drains it
            actions.Register(Hrot.Common.Constants.GlobalActionIds.ToggleShoulderView, (_, _) =>
                bus.Publish(new Hrot.Common.Diagnostics.Gizmos.ToggleShoulderViewEvent()));
            // ⭐ CE-3169 (DESIGN_Entity_Control §4.2) — Move Here · Engage · Stop, once for every LIVE map host. ⛔ Not in a replay
            //   (no live brain to order): there the menu does not offer them (OrdersOffered unset) and nothing is registered.
            var menuOrders = new MenuOrders(ctx.World) { Selected = () => selection.SelectedEntities };   // ⭐ CE-1059 — O6
            Hrot.UI.Common.AddEntity.EntityAuthoring? authoringForForm = null;
            Hrot.Presentation.Orders.OrderForm? orderForm = null;
            // ⭐ §6x (DESIGN_Map_3D_Mode FX-1..FX-3) — the fire mission's aim points: drawn from what each gun publishes, so a replay too
            globalManager.Register(GlobalGizmoManager.NewId(), new Hrot.Presentation.Orders.FireMissionOverlayGizmo(() => selection.SelectedEntities));
            if (ctx.IsReplayActive?.Invoke() != true)
            {
                menuOrders.RegisterOn(actions);
                ctx.World.SetSingletonManaged(new Hrot.Common.Orders.OrdersOffered());

                // ⭐ CE-1059 (P5b, DESIGN_Orders_Params_Entity_Classes §13.12) — THE order form (registered as IOrderForm on its first
                //   draw: offered where drawn) and the overlay that draws each selected unit's designations.
                orderForm = new Hrot.Presentation.Orders.OrderForm(ctx.World, () => menuOrders.PickService?.Invoke(),
                    () => authoringForForm?.Pickers);
                long overlayId = GlobalGizmoManager.NewId();
                globalManager.Register(overlayId, new Hrot.Presentation.Orders.DesignationOverlayGizmo(() => selection.SelectedEntities));

                // ⭐ CE-3171 (DESIGN_Entity_Control §8a) — the RTS command tool, armed from View › RTS Command Mode. It routes three
                //   gestures to three existing writes (the selection request, EntityOrders, the station's hit test) — Measure's shape.
                var rts = new RtsCommands(ctx.World, () => selection.SelectedEntities, () => menuOrders.PickService?.Invoke());
                var rtsMode = new RtsModeState();
                MapUiStates.Set(ctx.World, rtsMode);   // ⭐ CE-1132 — the map's overlay draws the legend and the attack reticle from it
                tools.Register(
                    new Hrot.ScenarioEditor.Tools.ToolDescriptor(Hrot.ScenarioEditor.Tools.ScenarioToolIds.RtsCommand, "RTS Command Mode",
                        Hrot.ScenarioEditor.Tools.ToolModality.Modal, Hrot.ScenarioEditor.Tools.ToolArbiter.Global, ToggleOnReactivate: true),
                    _ =>
                    {
                        if (menuOrders.PickService?.Invoke() is null)   // a headless host: nobody to click
                            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;
                        var id = GlobalGizmoManager.NewId();
                        globalManager.Register(id, new RtsCommandGizmo(rts, () => globalManager.Unregister(id), rtsMode));
                        return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
                    });
                actions.Register(Hrot.Common.Constants.GlobalActionIds.ToggleRtsCommand, (_, _) =>
                    tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.RtsCommand, Entity.Null));
            }

            // ⭐ CE-1017 — ONE entity-authoring surface per map, built here for every host that can author (Editor,
            //   CGF, SimHost, IG): spawn adapter, picker, Add Entity action and the canvas menu. The host only
            //   schedules mi.CanvasMenu and draws mi.EntityAuthoring.DrawFrame().
            Hrot.UI.Common.AddEntity.EntityAuthoring? authoring = null;
            if (ctx.EntityAuthoring is { } inputs)
            {
                authoring = new Hrot.UI.Common.AddEntity.EntityAuthoring(inputs, ctx.World.Bus, globalManager, tools);
                authoring.AddEntity.RegisterOn(actions);
                authoringRef = authoring;
                authoringForForm = authoring;   // ⭐ CE-1059 — the order form's entity list opens in the same picker registry
            }
            var canvasMenu = authoring?.CanvasMenu ?? new Hrot.Presentation.Systems.CanvasMenuUpdateSystem();

            var map = new MapInteraction(
                buffer, bus, gizmoRegistry, statelessRegistry, settings,
                globalManager, dataDriven, stateless, group, gate, selfCheck, tools,
                selection, selectionInteraction, selectionRequests, selectionNotifications,
                rubberBand, actions, actionDispatch, layerControl, authoring, canvasMenu)
            {
                IsReplayActive = ctx.IsReplayActive,   // ⭐ W1 (VE-R) — the effect systems stand down in a replay
                MenuOrders     = menuOrders,
                MenuShortcuts  = new MenuShortcuts(ctx.World, bus, selection, tools),   // ⭐ CE-1076
                OrderForm      = orderForm,
                Host           = host,   // ⭐ CE-1150
            };
            return map;
        }

        /// <summary>
        /// ⭐ Registers the schemas of the panels the pack's gizmos open (today: the layer control) with a
        /// host renderer's schema registry — one call instead of a hand-written block per host.
        /// </summary>
        /// <summary>
        /// ⭐⭐ <c>CE-1243</c> MB-4 (docs/UX/UX_Feature_Menu_Follows_Focus.md §MB) — a host's map menus (the gizmos' main-menu bindings:
        /// <i>View ▸ Tactical Map Layers…, 2-D / 3-D Map …</i>) become a CONTRIBUTOR of the one menu bar, bound to the host's perspective
        /// (UXI-05: they follow focus); a click is the same <c>GizmoMenuActionEvent</c> on the host's interaction bus as before. ⛔ SUPERSEDED:
        /// each host's own <c>BeginMainMenuBar</c> block (UXI-13). One registration for every map host.
        /// </summary>
        public static void RegisterMainMenu(Fdp.Presentation.WindowManager.GlobalMenuRegistry menu, string id, string perspective,
            Func<System.Collections.Generic.IReadOnlyList<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>?> consume,
            Func<FdpEventBus?> interactionBus)
        {
            ArgumentNullException.ThrowIfNull(menu);
            ArgumentNullException.ThrowIfNull(consume);
            ArgumentNullException.ThrowIfNull(interactionBus);
            menu.RegisterContributor(id,
                () => consume() ?? Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>(),
                actionId => interactionBus()?.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent { AnchorId = 0, ActionId = actionId }),
                perspective);
        }

        /// <summary>
        /// ⭐⭐ THE map renderer layer, built the same way on every host (user, 2026-10-07: "unify and share, as
        /// usual"). 📐 Five hosts built it by hand with different arguments: SimHost and CGF passed no panel
        /// schemas (the layer-control panel could not draw there), and CGF passed no shape library and no
        /// world (so a picked anchor could not resolve to an entity). 🔒 Silent-default rule: every input the
        /// renderer can use is passed here.
        /// </summary>
        public static Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer BuildRenderLayer(
            DebugPrimitiveBuffer buffer,
            FdpEventBus bus,
            Fdp.Toolkit.Vis2D.Components.MapCamera? camera,
            Func<EntityRepository?> worldProvider,
            int layerBitIndex = 31)
        {
            var schemas = new GizmoMap.Presentation.GizmoSchemaRegistry();
            RegisterGizmoSchemas(schemas);
            return new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(
                layerBitIndex, buffer, bus,
                camera: camera,
                shapeLibrary: new Hrot.Presentation.Map.EntityClassShapeLibrary(),   // ⭐ CE-2138 — the class table picks the avatar
                schemaRegistry: schemas,
                worldProvider: worldProvider);
        }

        /// <summary>
        /// ⭐⭐⭐ CE-1033 — THE map's layers, attached the same way on EVERY host: the gizmo render layer (as
        /// <see cref="BuildRenderLayer"/>), the canvas' draw buffer, and the 3-D mode — the terrain and entity layers and the
        /// animated 2-D ↔ 3-D switch, driven by View › 2-D / 3-D Map (<see cref="Hrot.Common.Constants.GlobalActionIds.ToggleMap3D"/>).
        /// 🔒 User, 2026-10-10: <i>"we should be unifying and sharing from the day zero so something like 'not on all host' can not
        /// happen by construction."</i> ⇒ a host that has a map calls THIS, and gets 3-D; there is no per-host 3-D wiring to forget
        /// (the rail <c>EveryMapHostAttachesTheSharedLayers</c> pins it). 📄 docs/DESIGN_Map_3D_Mode.md §3.1, §6b.
        /// </summary>
        /// <param name="tkbProvider">The TKB the entity bodies are sized and classified from; default: the world's
        /// <see cref="Fdp.Interfaces.ITkbDatabase"/> singleton (every host that spawns sets one).</param>
        public static MapLayers AttachMapLayers(
            Fdp.Toolkit.Vis2D.MapCanvas canvas,
            DebugPrimitiveBuffer buffer,
            FdpEventBus bus,
            Func<EntityRepository?> worldProvider,
            Func<Fdp.Interfaces.ITkbDatabase?>? tkbProvider = null,
            int layerBitIndex = 31,
            Func<bool>? gridAlsoWhen = null)
        {
            if (canvas is null) throw new ArgumentNullException(nameof(canvas));
            // ⭐⭐ CE-1227 (docs/DESIGN_Terrain_World.md §10) — the 2-D terrain FIRST, under every gizmo, as a retained layer: it takes no
            //   slot in the map buffer (demo-city's terrain alone was 73 188 primitives a frame against 16 384, and the symbols and the
            //   marquee were dropped). Its checkbox is the buffer's Terrain2D bit — the mask LayerControlGizmo hands the buffer (CE-1071).
            var terrain2D = new Hrot.UI.Common.Map2D.TerrainLayer2D(worldProvider,
                () => buffer.IsLayerActive(DebugTraceLayers.Terrain2D), () => canvas.Camera);
            canvas.AddLayer(terrain2D);
            // ⭐ CE-1288 (DESIGN_Map_3D_Mode.md §6ac) — the 2-D helper grid: above the terrain's fills (they would hide it),
            //   below every gizmo (the entities draw over it). Its checkbox is the buffer's Grid bit (the "Grid (2-D)" row of the layer
            //   panel, on by default); a host's own switch (the editor's Config window "Grid" tick) may also force it on.
            var grid = new Fdp.Toolkit.Vis2D.Layers.GridMapLayer(
                () => buffer.IsLayerActive(DebugTraceLayers.Grid) || (gridAlsoWhen?.Invoke() ?? false), () => canvas.Camera);
            canvas.AddLayer(grid);
            var gizmoLayer = BuildRenderLayer(buffer, bus, canvas.Camera, worldProvider, layerBitIndex);
            if (UiStatesOf.TryGetValue(buffer, out var uiStates)) gizmoLayer.UiStates = uiStates;   // ⭐ CE-1234 — the panel follows its gizmo
            gizmoLayer.CurrentCamera = () => canvas.Camera;   // ⭐ CE-1033 S2 — gizmo input picks in 3-D too
            canvas.AddLayer(gizmoLayer);
            canvas.DrawBuffer = buffer;
            // ⭐ CE-1289 — the cursor's reading, measured every frame in both views and shown by MapCursorStatusBarSection; it lives on the
            //   canvas, so a host finds it with `canvas.Get<MapCursorReadout>()`. The ground height is the 3-D camera's (one terrain
            //   resolution per tick, CE-1054), so the 2-D reading's altitude is the terrain's at that spot.
            var cursor = new MapCursorReadout();
            canvas.AddResource(cursor);

            var tkb = tkbProvider ?? (() => worldProvider() is { } w && w.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()
                                          ? w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null);
            var viewSwitch = new Fdp.Toolkit.Vis3D.MapViewSwitch(canvas, canvas.Camera);
            // ⭐ CE-1203 FL-8 (§5e) — the layer control shows and applies the set of the view on screen
            if (LayerControlOf.TryGetValue(buffer, out var layerControl)) layerControl.Is3D = () => viewSwitch.Is3D;
            // ⭐ CE-1054 — one terrain resolution per tick, not one per draped point (the 3-D map ran at ~1 FPS)
            viewSwitch.Camera3D.GroundHeight = new Hrot.UI.Common.Map3D.CachedGroundHeight(worldProvider).At;
            var terrain = new Fdp.Toolkit.Vis3D.TerrainLayer3D(() =>
                worldProvider() is { } w ? Fdp.Toolkit.World.WorldQuery.RenderGeometryOf(w) : null);
            // ⭐ WL-1 (DESIGN_Weapon_Layers W-9) — articulated kits are posed by their turret part's TurretPose.
            var turretPoses = new Hrot.UI.Common.Map3D.TurretPoseSource();
            // ⭐ CE-4031 (AQ93 §15.8) — SUMO's far people and cars drawn as ours, through the same figures and kits
            var bodies = new Hrot.UI.Common.Map3D.EntityBodyLayer3D(worldProvider, tkb, turretPoses.PoseOf, Hrot.UI.Common.Map3D.TrafficAmbientBodies.Of);
            // ⭐ CE-1033 S3 — gizmos in 3-D: z = 0 shapes drape on the ground (M18); labels sit on top of the bodies (M13).
            gizmoLayer.Renderer3D.GroundHeight = viewSwitch.Camera3D.GroundHeight;
            gizmoLayer.LabelLift = bodies.TopAbove;
            // ⭐ CE-1033 S2 (M17) — what a 3-D click can hit: the drawn terrain, then the entity boxes (nearest wins).
            viewSwitch.Camera3D.Pickers.Add(new Fdp.Toolkit.Vis3D.TerrainPicker(() =>
                worldProvider() is { } w ? Fdp.Toolkit.World.WorldQuery.RenderGeometryOf(w) : null));
            viewSwitch.Camera3D.Pickers.Add(new Hrot.UI.Common.Map3D.EntityBoxPicker(worldProvider, tkb, bodies));
            // ⭐ CE-1126 (DESIGN_Entity_Control §8g F-4) — the FPS crosshair's: the parts a round meets (PickFilter.EntityParts)
            viewSwitch.Camera3D.Pickers.Add(new Hrot.UI.Common.Map3D.EntityPartPicker(worldProvider, tkb, bodies));
            canvas.AddLayer(terrain);
            canvas.AddLayer(new MapCursorLayer(canvas, cursor, worldProvider, viewSwitch.Camera3D.GroundHeight));   // ⭐ CE-1289
            // ⭐ AQ93 R-4024 / V7 — the SUMO roads and lights, TerrainLayer3D's sibling (idle when the terrain has no SUMO net); its far
            //   people and cars are bodies (CE-4031).
            canvas.AddLayer(new Hrot.UI.Common.Map3D.TrafficLayer3D(worldProvider));
            canvas.AddLayer(bodies);
            // ⭐ CE-1042 E3 — muzzle flashes, tracers, explosions and decals, from their TKB type, in both modes (VE-F).
            var effects = new Hrot.UI.Common.Effects.EffectLayer(worldProvider, tkb, bodies) { GroundHeight = viewSwitch.Camera3D.GroundHeight };
            canvas.AddLayer(effects);
            // ⭐ P1 (DESIGN_Visual_Effects.md §8 PR-B) — rounds with a munition look (a grenade flying and lying, a mortar bomb, a rocket…).
            var projectiles = new Hrot.UI.Common.Effects.ProjectileLayer(worldProvider, tkb, () => viewSwitch.Is3D ? viewSwitch.Camera3D : null)
            {
                GroundHeight = viewSwitch.Camera3D.GroundHeight,
            };
            canvas.AddLayer(projectiles);
            var viewMode = new Hrot.UI.Common.Map3D.MapViewModeLayer(viewSwitch, bus);
            canvas.AddLayer(viewMode);
            // ⭐ CE-1033 S5 (R-260) — the scenario's camera entity, the same on every host: adopted when the scenario opens, REQUESTED
            //   (unowned, so the default processor creates and owns it — R-140) on the first camera move, moved by its owner.
            canvas.AddLayer(new Hrot.UI.Common.Map3D.MapCameraLayer(viewSwitch, worldProvider));
            canvas.AddLayer(new MapCanvasLink(canvas, worldProvider));   // ⭐ CE-1134 — Center View finds this canvas by the world
            canvas.AddLayer(new Hrot.UI.Common.Map3D.WeatherSkyLayer(viewSwitch, worldProvider));   // ⭐ CE-1146 — the weather's sky: the free camera at what it looks at, first person at the eye
            canvas.AddLayer(new Hrot.UI.Common.Map3D.RubberBandOverlay3D(viewSwitch, worldProvider));   // ⭐ CE-1136 — the 3-D marquee
            var stats = new Hrot.UI.Common.Map3D.MapStatsOverlay(viewSwitch, worldProvider, canvas.Camera);   // ⭐ CE-1262 — + the 2-D camera
            canvas.AddLayer(stats);   // ⭐ CE-1145 / CE-1132 — FPS, RTS legend
            // ⭐ CE-3174 (docs/DESIGN_Entity_Control.md §8d) — this map host as a first-person station: it follows the replicated
            //   PossessedBy, so a unit held by this node puts the map into its eyes (camera driver, cursor lock, HUD) and losing it
            //   leaves. Here, so no map host can lack it.
            var possession = new Hrot.UI.Common.Map3D.PossessionStation(viewSwitch, worldProvider, bodies);   // ⭐ CE-1073 — the drawn heads
            stats.Possession = possession;   // ⭐ CE-1262 (AV-1) — the view snapshot names the possessed unit and its crosshair target
            canvas.AddLayer(possession);
            viewMode.ToggleShoulder = () => possession.OverShoulder = !possession.OverShoulder;   // ⭐ CE-1201 (§8p OS-1)
            // ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-3) — the scene's sound, only while the station holds a unit
            var audio = Hrot.UI.Common.Audio.AudioLayer.For(possession, viewSwitch, worldProvider, tkb);
            canvas.AddLayer(audio);
            // ⭐ CE-1076 — the keys typed on the map go to the map's MenuShortcuts (the unit menu's M, A, H, P, C …), every host
            canvas.AddLayer(new MenuShortcutKeyLayer(bus, () => canvas.Camera));
            // ⭐ CE-3289 (§6ab, R-3032) — the film: a director camera and a clean capture of the scene, on every map host; LAST, so its
            //   update runs after every other layer's (the frame it captures is complete) and its director wins the camera.
            canvas.AddLayer(new Hrot.UI.Common.Map3D.FilmCaptureLayer(canvas, viewSwitch, worldProvider));
            return new MapLayers(gizmoLayer, viewSwitch, terrain, bodies, effects, projectiles, possession, audio, terrain2D, grid, cursor);
        }

        public static void RegisterGizmoSchemas(GizmoMap.Presentation.GizmoSchemaRegistry registry)
        {
            if (registry is null) throw new ArgumentNullException(nameof(registry));
            var editService = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
            using var session = editService.Open(
                new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Units = { Entities = true }, Ai = { VisionCones = true, AiHelpers = true } },
                typeof(Hrot.Common.Diagnostics.Gizmos.LayerControlDto));
            registry.Register(Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash, session.Document);
        }
    }
}
