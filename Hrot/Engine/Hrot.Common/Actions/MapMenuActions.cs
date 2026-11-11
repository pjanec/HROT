using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Common.Editing;
using Hrot.Map.Common.Components;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐⭐ B-11 A1 (docs/UX/DESIGN_Action_Catalogue.md §6) — TODAY's map menu items (unit, area, terrain zone, route) declared ONCE in the catalogue,
    /// exactly as <c>ContextMenuProjectorGizmo.MenuJsonFor</c> built them by hand (the golden <c>TheMenusAreGoldenTests</c> holds it byte-identical).
    /// ⛔ SUPERSEDED: the four hand-built menus (an <c>if</c> chain, four static JSON strings and a per-flag unit builder).
    /// </summary>
    public static class MapMenuActions
    {
        // Order within a group — shared across targets, so one declaration serves a unit and a graphic (Center, Select, Properties).
        private const int First = 0;

        /// <summary>Declares today's map menu actions into <paramref name="catalogue"/>.</summary>
        public static void DeclareInto(ActionCatalogue catalogue)
        {
            const ActionTarget Unit = ActionTarget.Unit, Graphic = ActionTarget.Graphic, Everything = Unit | Graphic, Block = ActionTarget.TerrainBlock;

            // ── Orders (a unit, while the map offers orders) ────────────────────────────────────────────────────────────────────────────
            // ⭐⭐ CE-2143 (D4-D) — the orders THIS entity's class fulfils (the receiver's own answer), as data: one declaration, many rows.
            // ⭐ B-11 A3 (D15) — map orders only while running (Preview, Live): in Edit a unit's plan is its Orders tab; in Review there is no live brain.
            catalogue.Declare(new ActionDecl(0, "Orders", ActionGroup.Orders, First, Unit)
            {
                Modes = ActionModes.Running,
                Expand = OrderRows,
            });
            // ⭐ CE-1059 (§13.12 O5) — every offered order through the form, prefilled, to set its optional fields.
            catalogue.Declare(new ActionDecl(0, "Order with options", ActionGroup.Orders, First + 1, Unit)
            {
                Modes = ActionModes.Running,
                Offer = ctx =>
                {
                    if (!ctx.Unit.Orders || !ctx.Unit.Form) return null;
                    var offered = Hrot.Common.Orders.OrderMenu.OfferedFor(ctx.Unit.Dis);
                    if (offered.Count == 0) return null;
                    var withOptions = new ContextMenuItemDto[offered.Count];
                    for (int i = 0; i < offered.Count; i++)
                        withOptions[i] = new ContextMenuItemDto
                        {
                            Id = GlobalActionIds.OrderWithOptionsBase + (offered[i].ActionId - Hrot.Common.Orders.OrderMenu.ActionBase),
                            Label = offered[i].Name + "…",
                        };
                    return new ContextMenuItemDto { Label = "Order with options", Children = withOptions };
                },
            });
            // ⭐ CE-2149 (F7 stage 1, §13.3) — the standing target / point / route / sector a later order may take; only on a unit that takes orders.
            catalogue.Declare(new ActionDecl(0, "Designate", ActionGroup.Orders, First + 2, Unit)
            {
                Modes = ActionModes.Running,
                Offer = ctx => !HasOrderRows(ctx) ? null : new ContextMenuItemDto
                {
                    Label = "Designate",
                    Children = new[]
                    {
                        new ContextMenuItemDto { Id = GlobalActionIds.DesignateTarget, Label = "Set target…" },
                        new ContextMenuItemDto { Id = GlobalActionIds.DesignatePoint,  Label = "Set point…" },
                        new ContextMenuItemDto { Id = GlobalActionIds.DesignateRoute,  Label = "Set route…" },
                        new ContextMenuItemDto { Id = GlobalActionIds.DesignateArea,   Label = "Set sector…" },
                        new ContextMenuItemDto { IsSeparator = true },
                        new ContextMenuItemDto { Id = GlobalActionIds.DesignateClear,  Label = "Clear designations" },
                    },
                },
            });
            // ⭐ CE-3174 — Possess; ⭐ CE-2143 (D4-H) not on a body-less unit; ⭐ CE-1205 (§8m PK-7) a manned weapon keeps it greyed, saying why.
            catalogue.Declare(new ActionDecl(GlobalActionIds.Possess, "Possess (FPS)", ActionGroup.Orders, First + 3, Unit)
            {
                Modes = ActionModes.Running,
                NeedsHat = Hat.Embodied,   // ⭐ Seats H6 — a Side controller / Observer seat sees it greyed: "…cannot possess a unit"
                Shortcut = "P",
                Offer = ctx => !ctx.Unit.Orders || ctx.Unit.IsUnit ? null
                    : ctx.Unit.Manned
                        ? new ContextMenuItemDto { Id = GlobalActionIds.Possess, Label = "Possess (FPS)", Enabled = false,
                                                   Tooltip = Fdp.Toolkit.Behavior.Possession.Possessability.WeaponIsManned }
                        : new ContextMenuItemDto { Id = GlobalActionIds.Possess, Label = "Possess (FPS)", Shortcut = "P" },
            });

            // ⭐ L-14 / CE-4314 (LC-6) — a plan copied onto other units: planned in Edit, re-planned in Live (the Orders tab's modes).
            catalogue.Declare(new ActionDecl(GlobalActionIds.CopyPlan,  "Copy plan",  ActionGroup.Orders, First + 4, Unit) { Modes = ActionModes.InEdit | ActionModes.InLive,
                Offer = ctx => ctx.Unit.Orders ? new ContextMenuItemDto { Id = GlobalActionIds.CopyPlan, Label = "Copy plan" } : null });
            catalogue.Declare(new ActionDecl(GlobalActionIds.PastePlan, "Paste plan", ActionGroup.Orders, First + 5, Unit) { Modes = ActionModes.InEdit | ActionModes.InLive,
                Offer = ctx => ctx.Unit.Orders ? new ContextMenuItemDto { Id = GlobalActionIds.PastePlan, Label = "Paste plan" } : null });

            // ── Embark (⭐ CE-1230 OM-2, OM-3, R-1019 — the ONE place a unit is embarked / disembarked by hand) ─────────────────────────
            catalogue.Declare(new ActionDecl(GlobalActionIds.Disembark, "Disembark", ActionGroup.Embark, First, Unit)
            {
                Execution = ActionExecution.PerEntity,   // ⭐ A4 (R-4162) — every selected unit gets off
                Modes = ActionModes.InEdit | ActionModes.InLive,   // ⭐ A3 — planned in Edit, ordered in Live; not in a Preview, not in a recording
                Offer = ctx => ctx.Unit.Embark == EmbarkOffer.Disembark ? new ContextMenuItemDto { Id = GlobalActionIds.Disembark, Label = "Disembark" } : null,
            });
            catalogue.Declare(new ActionDecl(GlobalActionIds.Embark, "Embark…", ActionGroup.Embark, First + 1, Unit)
            {
                Modes = ActionModes.InEdit | ActionModes.InLive,
                Offer = ctx => ctx.Unit.Embark == EmbarkOffer.Embark ? new ContextMenuItemDto { Id = GlobalActionIds.Embark, Label = "Embark…" } : null,
            });

            // ── View ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
            catalogue.Declare(new ActionDecl(GlobalActionIds.CenterOnEntity, "Center View", ActionGroup.View, First, Everything) { Shortcut = "C" });
            catalogue.Declare(new ActionDecl(GlobalActionIds.FollowUnit, "Follow with the camera", ActionGroup.View, First + 1, Unit));   // ⭐ CE-1293 (UC-2)
            catalogue.Declare(new ActionDecl(GlobalActionIds.Select, "Select", ActionGroup.View, First + 2, Everything) { Shortcut = "Space" });
            catalogue.Declare(new ActionDecl(GlobalActionIds.Properties, "Properties…", ActionGroup.View, First + 3, Everything));   // ⭐ CE-1135
            catalogue.Declare(new ActionDecl(GlobalActionIds.SaveSelectionAsMacro, "Save selection as macro…", ActionGroup.View, First + 4, Unit) { Level = EditingLevel.Full, Modes = ActionModes.InEdit });   // ⭐ CE-4210 MA-9 · R-4162: authoring only

            // ── Edit ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
            // ⭐ A5 / LC-3 — Copy / Paste / Duplicate (units and graphics): one path in Edit and Live (DESIGN_Entity_Copy_Paste §2); the paste is the placement gesture
            catalogue.Declare(new ActionDecl(GlobalActionIds.CopyEntities, "Copy", ActionGroup.Edit, First + 2, Everything) { Shortcut = "Ctrl+C", Modes = ActionModes.InEdit | ActionModes.InLive });
            catalogue.Declare(new ActionDecl(GlobalActionIds.PasteEntities, "Paste", ActionGroup.Edit, First + 3, Everything) { Shortcut = "Ctrl+V", Modes = ActionModes.InEdit | ActionModes.InLive });
            catalogue.Declare(new ActionDecl(GlobalActionIds.DuplicateEntities, "Duplicate", ActionGroup.Edit, First + 4, Everything) { Shortcut = "Ctrl+D", Modes = ActionModes.InEdit | ActionModes.InLive });
            catalogue.Declare(new ActionDecl(GlobalActionIds.Rotate, "Rotate", ActionGroup.Edit, First, Unit) { Shortcut = "R", Modes = ActionModes.InEdit | ActionModes.InLive });   // ⭐ R-4162 — the same modes as moving by drag and drop
            catalogue.Declare(new ActionDecl(GlobalActionIds.EditOverlay, "Edit Shape", ActionGroup.Edit, First, ActionTarget.Area | ActionTarget.Zone) { Shortcut = "E", Modes = ActionModes.InEdit | ActionModes.InLive });
            catalogue.Declare(new ActionDecl(GlobalActionIds.LoadZone, "Load zone", ActionGroup.Edit, First + 1, ActionTarget.Zone) { Shortcut = "L", Modes = ActionModes.InEdit | ActionModes.InLive });   // ⭐ CE-3024 (E2) — always enabled: the load is cluster-wide
            catalogue.Declare(new ActionDecl(GlobalActionIds.EditRoute, "Edit Route", ActionGroup.Edit, First, ActionTarget.Route) { Shortcut = "E", Modes = ActionModes.InEdit | ActionModes.InLive });
            // ⭐ W-1 / P-8 8d-2 (DESIGN_Mission_Plan_Editor §5.8 E2) — drag a task's marker to move its place; the modes the plan is edited in (the Orders tab's)
            catalogue.Declare(new ActionDecl(GlobalActionIds.EditPlan, "Edit plan on the map", ActionGroup.Edit, First + 1, Unit) { Modes = ActionModes.InEdit | ActionModes.InLive,
                Offer = ctx => ctx.Unit.Plan ? new ContextMenuItemDto { Id = GlobalActionIds.EditPlan, Label = "Edit plan on the map" } : null });

            // ── Terrain block (U-3): the block menu of mockup 2 — Re-roll, Rotate, Unpack (greyed until backend's T-4), Change recipe… (+ Select / Properties… / Delete above). Authoring only.
            catalogue.Declare(new ActionDecl(GlobalActionIds.TerrainReroll, "Re-roll layout", ActionGroup.Edit, First, Block) { Icon = "terrain/reroll", Modes = ActionModes.InEdit });
            catalogue.Declare(new ActionDecl(GlobalActionIds.TerrainRotate, "Rotate", ActionGroup.Edit, First + 1, Block) { Icon = "terrain/rotate", Modes = ActionModes.InEdit });
            catalogue.Declare(new ActionDecl(GlobalActionIds.TerrainMove, "Move", ActionGroup.Edit, First + 2, Block)
            {
                Modes = ActionModes.InEdit,
                Offer = _ => new ContextMenuItemDto { Id = GlobalActionIds.TerrainMove, Label = "Move", Icon = "terrain/move", Shortcut = "drag", Enabled = false, Tooltip = "Drag the block on the map." },
            });
            catalogue.Declare(new ActionDecl(GlobalActionIds.TerrainUnpack, "Unpack into houses and features", ActionGroup.Edit, First + 3, Block)
            {
                Modes = ActionModes.InEdit,
                Offer = _ => new ContextMenuItemDto { Id = GlobalActionIds.TerrainUnpack, Label = "Unpack into houses and features", Icon = "terrain/unpack", Enabled = false, Tooltip = "Needs the block operations of backend's T-4 (CE-4431) — not built yet." },
            });
            catalogue.Declare(new ActionDecl(GlobalActionIds.TerrainChangeRecipe, "Change recipe…", ActionGroup.Edit, First + 4, Block) { Icon = "terrain/block", Modes = ActionModes.InEdit });

            // ── Debug (a unit) ────────────────────────────────────────────────────────────────────────────────────────────────────────
            catalogue.Declare(new ActionDecl(0, "Pin gizmos", ActionGroup.Debug, First, Unit)
            {
                Offer = ctx => GizmoPins.Submenu(ctx.Unit.Pins),   // ⭐ CE-3120 — pin gizmo families on this unit; ⭐ CE-1329 checked as pinned
            });
            catalogue.Declare(new ActionDecl(0, "AI trace", ActionGroup.Debug, First + 1, Unit)
            {
                Offer = ctx => ctx.Unit.Brain ? AiTraceActions.Submenu(ctx.Unit.TraceOn) : null,   // ⭐ CE-3123 — on every map host
            });

            // ── Destructive (a graphic) ───────────────────────────────────────────────────────────────────────────────────────────────
            catalogue.Declare(new ActionDecl(GlobalActionIds.Delete, "Delete", ActionGroup.Destructive, First, Everything | Block) { Shortcut = "Delete", Style = "destructive", Modes = ActionModes.InEdit | ActionModes.InLive, Execution = ActionExecution.PerEntity,
                Offer = ctx => new ContextMenuItemDto { Id = GlobalActionIds.Delete, Label = "Delete", Shortcut = "Delete", Style = "destructive", Icon = ctx.Target == Block ? "terrain/delete" : null } });   // ⭐ A4 — every selected one · ⭐ CE-4410 — a UNIT too (it was offered on a graphic only, so a right-click on an entity had no Delete); the handler is the Delete key's: Edits.Submit(EditOp.Delete)
        }

        private static IEnumerable<ContextMenuItemDto> OrderRows(ActionContext ctx)
        {
            if (!ctx.Unit.Orders) yield break;
            foreach (var o in Hrot.Common.Orders.OrderMenu.OfferedFor(ctx.Unit.Dis))
                if (OrderItem(o, ctx.Unit.Degraded, ctx.Unit.Form) is { } item) yield return item;
        }

        /// <summary>Whether the orders group has an order row (the Designate submenu needs one).</summary>
        private static bool HasOrderRows(ActionContext ctx)
        {
            if (!ctx.Unit.Orders) return false;
            var offered = Hrot.Common.Orders.OrderMenu.OfferedFor(ctx.Unit.Dis);
            if (ctx.Unit.Form && offered.Count > 0) return true;
            foreach (var o in offered)
                if (OrderItem(o, ctx.Unit.Degraded, ctx.Unit.Form) != null) return true;
            return false;
        }

        /// <summary>
        /// One order's unit-menu row, or null when it is not offered here. ⭐ <c>CE-1059</c> (§13.12 O5) — an order the gesture cannot complete
        /// (<see cref="Hrot.Common.Orders.OrderPick.Several"/>) needs the ORDER FORM: <c>Name…</c> where this world has one (<paramref name="form"/>),
        /// hidden elsewhere. An order that needs a pick is disabled on a heavily damaged unit. ⭐ The shortcut is the order's DECLARED letter
        /// (<see cref="OrderShortcuts"/>, B-10 K2) — never derived from its name.
        /// </summary>
        public static ContextMenuItemDto? OrderItem(Hrot.Common.Orders.OrderInfo o, bool degraded, bool form)
        {
            if (o.Pick == Hrot.Common.Orders.OrderPick.Several)
                return form ? new ContextMenuItemDto { Id = o.ActionId, Label = o.Name + "…" } : null;
            return degraded && o.Pick != Hrot.Common.Orders.OrderPick.None
                ? new ContextMenuItemDto { Id = o.ActionId, Label = o.Name, Enabled = false, Tooltip = "Cannot " + o.Name.ToLowerInvariant() + ": unit is heavily damaged" }
                : new ContextMenuItemDto { Id = o.ActionId, Label = o.Name, Shortcut = OrderShortcuts.TryGetValue(o.Name, out var letter) ? letter : null };
        }

        /// <summary>
        /// ⭐ B-10 K2 (docs/UX/DESIGN_Input_And_Commands.md §6, CE-4281) — the letter each order DECLARES on the map (the unit menu shows it, <c>MenuShortcuts</c> runs it).
        /// ⛔ SUPERSEDES the letter derived from the order's first character: two orders starting alike collided silently. An order with no entry shows NO letter,
        /// and the rail <c>OrderLettersAreDeclaredAndDistinct</c> names it — a new order must declare one here, distinct from the unit menu's other letters.
        /// </summary>
        /// <summary>
        /// ⭐ B-10 K6 (§6f) — declares the map menu's letters as rows of <paramref name="keymap"/> (scope Map, kind = the target class: unit / area / zone / route), so the Keyboard shortcuts window
        /// lists them and a letter bound twice for one target class is a conflict <c>Keymap.Validate</c> refuses. The ids are the actions' (<c>ActionRef.Global</c>); the label is the action's.
        /// </summary>
        public static void DeclareKeys(Fdp.Toolkit.Input.Keymap keymap)
        {
            var catalogue = new ActionCatalogue();
            DeclareInto(catalogue);
            foreach (var d in catalogue.All)
            {
                if (d.Id == 0 || d.Shortcut is null || !Fdp.Toolkit.Input.ChordText.TryParse(d.Shortcut, out var chord)) continue;
                DeclareRows(keymap, chord, d.Id, d.Label, d.Targets);
            }
            foreach (var kv in OrderShortcuts)
                if (Hrot.Common.Orders.OrderMenu.TryGetByName(kv.Key, out var order) && Fdp.Toolkit.Input.ChordText.TryParse(kv.Value, out var chord))
                    DeclareRows(keymap, chord, order.ActionId, order.Name, ActionTarget.Unit);
        }

        private static void DeclareRows(Fdp.Toolkit.Input.Keymap keymap, NodeEditor.Core.Action.KeyBinding chord, int id, string label, ActionTarget targets)
        {
            var action = Fdp.Toolkit.Input.ActionRef.Global(id);
            keymap.SetLabel(action, label);
            foreach (var (bit, name) in new[] { (ActionTarget.Unit, "unit"), (ActionTarget.Area, "area"), (ActionTarget.Zone, "zone"), (ActionTarget.Route, "route") })
                if ((targets & bit) != 0)
                    keymap.Declare(new Fdp.Toolkit.Input.KeymapEntry(chord, Fdp.Toolkit.Input.InputScope.Map, name, action));
        }

        public static readonly IReadOnlyDictionary<string, string> OrderShortcuts = new Dictionary<string, string>
        {
            ["Move"] = "M", ["Attack"] = "A", ["Hold"] = "H", ["Strike"] = "S", ["ObserveFire"] = "O",
        };

        // ── the context, and the cache (as the old per-flag unit menus) ──────────────────────────────────────────────────────────────

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ActionTarget Target, long Flags, ulong Dis, bool Unit), string> Menus = new();
        private static object? _menusBuiltFor;

        /// <summary>
        /// ⭐ B-11 A1 — the menu JSON for <paramref name="entity"/> (what <c>MenuJsonFor</c> returns): its target kind, its mode and — for a unit — the
        /// facts its rows depend on, read once; composed from the catalogue; cached per (target, facts) and rebuilt when the order vocabulary
        /// changes (a hot-reloaded behaviour assembly).
        /// </summary>
        public static string JsonFor(ISimulationView view, Entity entity, System.Func<int, bool>? runsHere = null)
        {
            // ⭐ B-11 A2 — a station's view of the menu (greyed where it binds no handler): composed per right-click, not cached (the cache
            //   holds the station-free menu, which MenuShortcuts and the goldens read)
            if (runsHere != null) return MenuComposer.Json(ContextFor(view, entity), runsHere);

            var vocabulary = Hrot.Common.Orders.OrderMenu.Orders;
            if (!ReferenceEquals(vocabulary, System.Threading.Volatile.Read(ref _menusBuiltFor)))
            {
                Menus.Clear();
                System.Threading.Volatile.Write(ref _menusBuiltFor, vocabulary);
            }
            var ctx = ContextFor(view, entity);
            var u = ctx.Unit;
            int modeBits = (int)ctx.Mode | ((int)ctx.Level << 2) | (ctx.Side is ForceId sd && ctx.UnitSide != sd ? 1 << 5 : 0);   // ⭐ Seats H2 — the seat greys rows: part of the key
            int flags = ctx.Target != ActionTarget.Unit ? 0
                : (u.Degraded ? 1 : 0) | (u.Brain ? 2 : 0) | (u.Orders ? 4 : 0) | (u.Form ? 8 : 0) | (u.Manned ? 16 : 0)
                  | ((int)u.Embark << 5) | ((int)u.Pins << 8) | (u.TraceOn ? 1 << 24 : 0) | (u.Plan ? 1 << 25 : 0);
            var key = (ctx.Target, (long)flags | ((long)modeBits << 26) | (SeatHats.Allows(ctx.Seat, Hat.Embodied) ? 0L : 1L << 40), u.Orders ? u.Dis.Value : 0UL, u.Orders && u.IsUnit);   // ⭐ H6 — the seat's hats grey Possess: part of the key
            return Menus.GetOrAdd(key, _ => MenuComposer.Json(ctx));
        }

        /// <summary>⭐ B-11 A1 — the context of one entity's menu: what it is (target), the mode, and a unit's facts.</summary>
        public static ActionContext ContextFor(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository;
            bool showsRecording = Fdp.Toolkit.Replay.ReplayState.IsActive(view);
            bool orders = Hrot.Common.Orders.OrdersOffered.IsOfferedIn(view);   // ⭐ CE-1363 — the one shared test (the View menu asks it too)
            var mode = StationMode.Of(view);   // ⭐ B-11 A3 — the ONE station mode (the gate reads the same)
            var level = StationMode.LevelOf(view);   // ⭐ Seats H2 — and the ONE seat: its level and side bound what the menu offers
            var side = StationMode.SideOf(view);
            var seat = StationMode.SeatOf(view);   // ⭐ Seats H6

            // ⭐ Terrain authoring U-3 — a placed block has its own menu (the TerrainBlock component decides, not the TKB type: a block is one on every host that loads the scenario)
            if (repo != null && repo.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.TerrainBlock>() && view.HasComponent<Fdp.Toolkit.Terrain.TerrainBlock>(entity))
                return new ActionContext(view, entity, ActionTarget.TerrainBlock, mode, default) { Level = level, Side = side, Seat = seat };

            if (view.HasManagedComponent<EditablePolyline>(entity))
            {
                bool isZone = view.HasComponent<TkbIdentity>(entity)   // ⭐ CE-3024 — a terrain zone is an area that can also be LOADED
                    && view.GetComponentRO<TkbIdentity>(entity).TkbType == Hrot.Map.Common.TkbEntityTypes.TerrainZone;
                return new ActionContext(view, entity, isZone ? ActionTarget.Zone : ActionTarget.Area, mode, default) { Level = level, Side = side, Seat = seat };
            }
            if (view.HasManagedComponent<RoutePlan>(entity))
                return new ActionContext(view, entity, ActionTarget.Route, mode, default) { Level = level, Side = side, Seat = seat };

            // ⭐ CE-196 — "degraded" is at or below half health; ⚠ Max <= 0 leaves it healthy.
            bool degraded = false;
            if (view.HasComponent<Health>(entity))
            {
                ref readonly var health = ref view.GetComponentRO<Health>(entity);
                degraded = health.Max > 0f && health.Current / health.Max <= 0.5f;
            }
            bool brain = AiTraceActions.HasBrain(view, entity);                                           // ⭐ CE-3123
            var dis = repo != null ? repo.GetDisType(entity) : default;                                   // ⭐ CE-2143
            bool isUnit = view.HasComponent<Fdp.Core.CommandHierarchy.UnitEntity>(entity);
            bool form = orders && repo!.HasSingletonManaged<Hrot.Common.Orders.IOrderForm>();            // ⭐ CE-1059
            bool manned = Fdp.Toolkit.Behavior.Possession.Possessability.Refusal(view, entity) != null;   // ⭐ CE-1205
            var embark = repo == null || isUnit || showsRecording ? EmbarkOffer.None                      // ⭐ CE-1230
                : Hrot.Common.Orders.EmbarkOrders.IsAboard(repo, entity) ? EmbarkOffer.Disembark
                : Hrot.Common.Orders.EmbarkOrders.CanOffer(repo, entity) ? EmbarkOffer.Embark : EmbarkOffer.None;
            var pins = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.None;                             // ⭐ CE-1329
            foreach (var f in Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All)
                if (GizmoPins.IsPinned(view, entity, f)) pins |= f;
            bool traceOn = brain && repo != null && Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.NetIdOf(repo, entity) is var nid and not 0
                           && Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Peek(repo)?.IsTraceSet(nid) == true;
            // ⭐ as before: form and manned matter only where orders are offered
            ForceId? unitSide = side is null ? null : repo != null && repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(entity) ? repo.GetComponentRO<EntityInfo>(entity).ForceId : ForceId.Neutral;   // ⭐ Seats H2 — as EntityOrders' side lock reads it
            return new ActionContext(view, entity, ActionTarget.Unit, mode,
                new UnitFacts(degraded, brain, orders, dis, isUnit, form && orders, manned && orders, embark, pins, traceOn,
                    Plan: view.HasManagedComponent<Fdp.Toolkit.Behavior.Components.ActiveMissionPlan>(entity)))
                { Level = level, Side = side, UnitSide = unitSide, Seat = seat };
        }
    }
}
