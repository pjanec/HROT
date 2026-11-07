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
            const ActionTarget Unit = ActionTarget.Unit, Graphic = ActionTarget.Graphic, Everything = Unit | Graphic;

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
                Shortcut = "P",
                Offer = ctx => !ctx.Unit.Orders || ctx.Unit.IsUnit ? null
                    : ctx.Unit.Manned
                        ? new ContextMenuItemDto { Id = GlobalActionIds.Possess, Label = "Possess (FPS)", Enabled = false,
                                                   Tooltip = Fdp.Toolkit.Behavior.Possession.Possessability.WeaponIsManned }
                        : new ContextMenuItemDto { Id = GlobalActionIds.Possess, Label = "Possess (FPS)", Shortcut = "P" },
            });

            // ── Embark (⭐ CE-1230 OM-2, OM-3, R-1019 — the ONE place a unit is embarked / disembarked by hand) ─────────────────────────
            catalogue.Declare(new ActionDecl(GlobalActionIds.Disembark, "Disembark", ActionGroup.Embark, First, Unit)
            {
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
            catalogue.Declare(new ActionDecl(GlobalActionIds.SaveSelectionAsMacro, "Save selection as macro…", ActionGroup.View, First + 4, Unit));   // ⭐ CE-4210 MA-9

            // ── Edit ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
            catalogue.Declare(new ActionDecl(GlobalActionIds.Rotate, "Rotate", ActionGroup.Edit, First, Unit) { Shortcut = "R", Modes = ActionModes.InEdit });   // ⭐ A3 — authoring
            catalogue.Declare(new ActionDecl(GlobalActionIds.EditOverlay, "Edit Shape", ActionGroup.Edit, First, ActionTarget.Area | ActionTarget.Zone) { Shortcut = "E", Modes = ActionModes.InEdit | ActionModes.InLive });
            catalogue.Declare(new ActionDecl(GlobalActionIds.LoadZone, "Load zone", ActionGroup.Edit, First + 1, ActionTarget.Zone) { Shortcut = "L", Modes = ActionModes.InEdit | ActionModes.InLive });   // ⭐ CE-3024 (E2) — always enabled: the load is cluster-wide
            catalogue.Declare(new ActionDecl(GlobalActionIds.EditRoute, "Edit Route", ActionGroup.Edit, First, ActionTarget.Route) { Shortcut = "E", Modes = ActionModes.InEdit | ActionModes.InLive });

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
            catalogue.Declare(new ActionDecl(GlobalActionIds.Delete, "Delete", ActionGroup.Destructive, First, Graphic) { Style = "destructive", Modes = ActionModes.InEdit | ActionModes.InLive });
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
        /// hidden elsewhere. An order that needs a pick is disabled on a heavily damaged unit. ⚠ The shortcut is still derived from the order's
        /// first letter (design §2.1 "no auto-derived letters" — B-11 A3 / B-10 K5 declare it).
        /// </summary>
        public static ContextMenuItemDto? OrderItem(Hrot.Common.Orders.OrderInfo o, bool degraded, bool form)
        {
            if (o.Pick == Hrot.Common.Orders.OrderPick.Several)
                return form ? new ContextMenuItemDto { Id = o.ActionId, Label = o.Name + "…" } : null;
            return degraded && o.Pick != Hrot.Common.Orders.OrderPick.None
                ? new ContextMenuItemDto { Id = o.ActionId, Label = o.Name, Enabled = false, Tooltip = "Cannot " + o.Name.ToLowerInvariant() + ": unit is heavily damaged" }
                : new ContextMenuItemDto { Id = o.ActionId, Label = o.Name, Shortcut = o.Name.Substring(0, 1) };
        }

        // ── the context, and the cache (as the old per-flag unit menus) ──────────────────────────────────────────────────────────────

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ActionTarget Target, int Flags, ulong Dis, bool Unit), string> Menus = new();
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
            int modeBits = (int)ctx.Mode;
            int flags = ctx.Target != ActionTarget.Unit ? 0
                : (u.Degraded ? 1 : 0) | (u.Brain ? 2 : 0) | (u.Orders ? 4 : 0) | (u.Form ? 8 : 0) | (u.Manned ? 16 : 0)
                  | ((int)u.Embark << 5) | ((int)u.Pins << 8) | (u.TraceOn ? 1 << 24 : 0);
            var key = (ctx.Target, flags | (modeBits << 26), u.Orders ? u.Dis.Value : 0UL, u.Orders && u.IsUnit);
            return Menus.GetOrAdd(key, _ => MenuComposer.Json(ctx));
        }

        /// <summary>⭐ B-11 A1 — the context of one entity's menu: what it is (target), the mode, and a unit's facts.</summary>
        public static ActionContext ContextFor(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository;
            bool showsRecording = Fdp.Toolkit.Replay.ReplayState.IsActive(view);
            bool orders = Hrot.Common.Orders.OrdersOffered.IsOfferedIn(view);   // ⭐ CE-1363 — the one shared test (the View menu asks it too)
            var mode = StationMode.Of(view);   // ⭐ B-11 A3 — the ONE station mode (the gate reads the same)

            if (view.HasManagedComponent<EditablePolyline>(entity))
            {
                bool isZone = view.HasComponent<TkbIdentity>(entity)   // ⭐ CE-3024 — a terrain zone is an area that can also be LOADED
                    && view.GetComponentRO<TkbIdentity>(entity).TkbType == Hrot.Map.Common.TkbEntityTypes.TerrainZone;
                return new ActionContext(view, entity, isZone ? ActionTarget.Zone : ActionTarget.Area, mode, default);
            }
            if (view.HasManagedComponent<RoutePlan>(entity))
                return new ActionContext(view, entity, ActionTarget.Route, mode, default);

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
            return new ActionContext(view, entity, ActionTarget.Unit, mode,
                new UnitFacts(degraded, brain, orders, dis, isUnit, form && orders, manned && orders, embark, pins, traceOn));
        }
    }
}
