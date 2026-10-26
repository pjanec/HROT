using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Constants;
using Fdp.Toolkit.Combat.Components;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Fdp.Core.Serialization;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// Emits a <see cref="Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.ContextMenuBinding"/>
    /// meta-primitive for every networked entity so that the IG dumb terminal can present a
    /// right-click context menu via the gizmo stream.
    ///
    /// Menu JSON strings are pre-serialised as static fields - one per distinct entity state
    /// permutation - and interned in the <see cref="Fdp.Toolkit.Diagnostics.Gizmos.StringInternMap"/>
    /// on the first frame each string is encountered. After that, only the 4-byte FNV-1a hash
    /// travels with every DebugPrimitive; the full string is cached on both sides.
    ///
    /// Currently two permutations are defined:
    /// <list type="bullet">
    ///   <item>Healthy   - unit is combat-effective (health &gt;= 50 %)</item>
    ///   <item>Degraded  - unit has taken significant damage (health &lt; 50 %)</item>
    /// </list>
    /// </summary>
    [GizmoProjector(typeof(NetworkIdentity))]
    public sealed class ContextMenuProjectorGizmo : IStatelessGizmo
    {
        // ---- Pre-serialised menu permutations (built once at class-load time) ----

        private static readonly JsonSerializerOptions SerializerOptions =
            // CE-3187 (R-2005): the shared options; every ContextMenuItemDto member carries its own ignore condition.
            FdpJsonOptionsRegistry.DefaultRelaxed;

        /// <summary>The unit menus already built: health (degraded or not) × brain (the AI trace submenu, ⭐ <c>CE-3123</c>) × ⭐
        /// <c>CE-3169</c> orders offered (only where <see cref="Hrot.Common.Orders.OrdersOffered"/> is set) × ⭐ <c>CE-2143</c> the
        /// entity's DIS type (which orders its class fulfils) × a body-less unit (no Possess). Built on first use; rebuilt when the
        /// order vocabulary changes (a hot-reloaded behaviour assembly).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Flags, ulong Dis, bool Unit), string> UnitMenus = new();
        private static object? _menusBuiltFor;

        private static string UnitMenuFor(bool degraded, bool brain, bool orders, DISEntityType dis, bool isUnit, bool form = false, bool manned = false,
                                          EmbarkItem embark = EmbarkItem.None, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags pins = default, bool traceOn = false)
        {
            var vocabulary = Hrot.Common.Orders.OrderMenu.Orders;
            if (!ReferenceEquals(vocabulary, System.Threading.Volatile.Read(ref _menusBuiltFor)))
            {
                UnitMenus.Clear();
                System.Threading.Volatile.Write(ref _menusBuiltFor, vocabulary);
            }
            form &= orders;
            manned &= orders;
            int flags = (degraded ? 1 : 0) | (brain ? 2 : 0) | (orders ? 4 : 0) | (form ? 8 : 0)   // ⭐ CE-1059 — 8: an order form here
                      | (manned ? 16 : 0)                                                          // ⭐ CE-1205 — 16: a weapon (manned, not possessed)
                      | ((int)embark << 5)                                                         // ⭐ CE-1230 — 32 / 64: Embark… / Disembark
                      | ((int)pins << 8)                                                           // ⭐ CE-1329 — the pinned families' checks
                      | (brain && traceOn ? 1 << 24 : 0);                                          // ⭐ CE-1329 — the trace switch's check
            return UnitMenus.GetOrAdd((flags, orders ? dis.Value : 0UL, orders && isUnit),
                _ => UnitMenu(degraded, brain, orders, dis, isUnit, form, manned, embark, pins, traceOn));
        }

        /// <summary>⭐ <c>CE-1230</c> (canvas context menu §11 OM-2, OM-3) — the embark item a unit's menu carries.</summary>
        public enum EmbarkItem : byte { None = 0, Embark = 1, Disembark = 2 }

        private static string UnitMenu(bool degraded, bool brain, bool orders, DISEntityType dis, bool isUnit, bool form, bool manned = false,
                                        EmbarkItem embark = EmbarkItem.None, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags pins = default, bool traceOn = false)
        {
            var items = new System.Collections.Generic.List<ContextMenuItemDto>();
            if (orders)
            {
                // ⭐⭐ CE-2143 (D4-D) — the orders THIS entity's class fulfils (the receiver's own answer, §8), not a fixed three.
                //   An order that needs the operator to point at something is disabled on a heavily damaged unit, as Move / Engage were.
                var offered = Hrot.Common.Orders.OrderMenu.OfferedFor(dis);
                foreach (var o in offered)
                    if (OrderItem(o, degraded, form) is { } item) items.Add(item);
                // ⭐ CE-1059 (§13.12 O5) — every offered order through the form, prefilled, to set its optional fields.
                if (form && offered.Count > 0)
                {
                    var withOptions = new ContextMenuItemDto[offered.Count];
                    for (int i = 0; i < offered.Count; i++)
                        withOptions[i] = new ContextMenuItemDto
                        {
                            Id = GlobalActionIds.OrderWithOptionsBase + (offered[i].ActionId - Hrot.Common.Orders.OrderMenu.ActionBase),
                            Label = offered[i].Name + "…",
                        };
                    items.Add(new ContextMenuItemDto { Label = "Order with options", Children = withOptions });
                }
                // ⭐ CE-2149 (F7 stage 1, §13.3) — the standing target / point / route / sector a later order may take; only on a unit
                //   that takes orders at all.
                if (items.Count > 0) items.Add(new ContextMenuItemDto
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
                });
                // ⭐ CE-2143 (D4-H) — no Possess on a body-less unit: there is nothing to drive.
                if (!isUnit) items.Add(manned   // ⭐ CE-1205 (§8m PK-7) — a weapon is manned: the item stays, greyed, saying why
                    ? new ContextMenuItemDto { Id = GlobalActionIds.Possess, Label = "Possess (FPS)", Enabled = false,
                                               Tooltip = Fdp.Toolkit.Behavior.Possession.Possessability.WeaponIsManned }
                    : new ContextMenuItemDto { Id = GlobalActionIds.Possess, Label = "Possess (FPS)", Shortcut = "P" });   // ⭐ CE-3174
                if (items.Count > 0) items.Add(new ContextMenuItemDto { IsSeparator = true });
            }
            // ⭐ CE-1230 (OM-2, OM-3, R-1019) — the ONE place a unit is embarked / disembarked by hand: the map's menu, and through it the
            //   ORBAT tree's (they are the same menu)
            if (embark == EmbarkItem.Disembark) items.Add(new ContextMenuItemDto { Id = GlobalActionIds.Disembark, Label = "Disembark" });
            else if (embark == EmbarkItem.Embark) items.Add(new ContextMenuItemDto { Id = GlobalActionIds.Embark, Label = "Embark…" });
            if (embark != EmbarkItem.None) items.Add(new ContextMenuItemDto { IsSeparator = true });
            items.AddRange(new[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View", Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.FollowUnit,     Label = "Follow with the camera" },   // ⭐ CE-1293 (UC-2)
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",      Shortcut = "Space" },
                new ContextMenuItemDto { Id = GlobalActionIds.Properties,     Label = "Properties…" },   // ⭐ CE-1135 — the inspector, on it
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Rotate,         Label = "Rotate",      Shortcut = "R" },
                new ContextMenuItemDto { IsSeparator = true },
                GizmoPins.Submenu(pins),   // ⭐ CE-3120 — pin gizmo families on this unit; ⭐ CE-1329 checked as pinned
            });
            if (brain) items.Add(AiTraceActions.Submenu(traceOn));   // ⭐ CE-3123 — the AI trace toggles, on every map host
            return JsonSerializer.Serialize(items.ToArray(), SerializerOptions);
        }

        /// <summary>
        /// One order's unit-menu item, or null when it is not offered here. ⭐ <c>CE-1059</c> (§13.12 O5) — an order the gesture cannot
        /// complete (<see cref="Hrot.Common.Orders.OrderPick.Several"/>) needs the ORDER FORM: <c>Name…</c> where this world has one
        /// (<paramref name="form"/>: registered by the host that draws it), hidden elsewhere (a headless node: HTTP only). An order
        /// that needs a pick is disabled on a heavily damaged unit, as Move / Engage were.
        /// </summary>
        public static ContextMenuItemDto? OrderItem(Hrot.Common.Orders.OrderInfo o, bool degraded, bool form)
        {
            if (o.Pick == Hrot.Common.Orders.OrderPick.Several)
                return form ? new ContextMenuItemDto { Id = o.ActionId, Label = o.Name + "…" } : null;
            return degraded && o.Pick != Hrot.Common.Orders.OrderPick.None
                ? new ContextMenuItemDto { Id = o.ActionId, Label = o.Name, Enabled = false, Tooltip = "Cannot " + o.Name.ToLowerInvariant() + ": unit is heavily damaged" }
                : new ContextMenuItemDto { Id = o.ActionId, Label = o.Name, Shortcut = o.Name.Substring(0, 1) };
        }

        /// <summary>Menu for a tactical graphics area overlay.</summary>
        private static readonly string MenuJsonArea = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View",  Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",       Shortcut = "Space" },
                new ContextMenuItemDto { Id = GlobalActionIds.Properties,     Label = "Properties…" },   // ⭐ CE-1135
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.EditOverlay,    Label = "Edit Shape",   Shortcut = "E" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Delete,         Label = "Delete",       Style = "destructive" },
            }, SerializerOptions);

        /// <summary>
        /// ⭐ CE-3024 (E2) — the area menu plus "Load zone", for a TERRAIN ZONE (<c>TkbEntityTypes.TerrainZone</c>).
        /// ⛔ Always enabled: the load is cluster-wide and this node's local marker cannot speak for the cluster
        /// (design §9.7 ③b) — the same ruling the shared populator obeys.
        /// </summary>
        private static readonly string MenuJsonZone = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View",  Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",       Shortcut = "Space" },
                new ContextMenuItemDto { Id = GlobalActionIds.Properties,     Label = "Properties…" },   // ⭐ CE-1135
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.EditOverlay,    Label = "Edit Shape",   Shortcut = "E" },
                new ContextMenuItemDto { Id = GlobalActionIds.LoadZone,       Label = "Load zone",    Shortcut = "L" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Delete,         Label = "Delete",       Style = "destructive" },
            }, SerializerOptions);

        /// <summary>Menu for a tactical route graphic.</summary>
        private static readonly string MenuJsonRoute = JsonSerializer.Serialize(
            new ContextMenuItemDto[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.CenterOnEntity, Label = "Center View",  Shortcut = "C" },
                new ContextMenuItemDto { Id = GlobalActionIds.Select,         Label = "Select",       Shortcut = "Space" },
                new ContextMenuItemDto { Id = GlobalActionIds.Properties,     Label = "Properties…" },   // ⭐ CE-1135
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.EditRoute,      Label = "Edit Route",   Shortcut = "E" },
                new ContextMenuItemDto { IsSeparator = true },
                new ContextMenuItemDto { Id = GlobalActionIds.Delete,         Label = "Delete",       Style = "destructive" },
            }, SerializerOptions);

        // ---- IStatelessGizmo --------------------------------------------------

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;
            if (networkId == 0) return;

            draw.DrawContextMenuBinding(networkId, MenuJsonFor(view, entity));
        }

        /// <summary>
        /// The menu permutation for <paramref name="entity"/> — what <see cref="Draw"/> binds. ⭐ CE-3024: factored out so a
        /// rail can assert WHICH menu an entity gets (the zone menu is the only one carrying "Load zone").
        /// </summary>
        public static string MenuJsonFor(ISimulationView view, Entity entity)
        {
            string menuJson;

            if (view.HasManagedComponent<EditablePolyline>(entity))
            {
                // ⭐ CE-3024 — a terrain zone is an area that can also be LOADED.
                bool isZone = view.HasComponent<TkbIdentity>(entity)
                    && view.GetComponentRO<TkbIdentity>(entity).TkbType == Hrot.Map.Common.TkbEntityTypes.TerrainZone;
                menuJson = isZone ? MenuJsonZone : MenuJsonArea;
            }
            else if (view.HasManagedComponent<RoutePlan>(entity))
            {
                menuJson = MenuJsonRoute;
            }
            else
            {
                // Select the menu permutation based on health state when available.
                // ⭐ CE-196 — derived from Health.Current/Max, not from a precomputed damage percentage.
                //   The threshold is unchanged: "degraded" is 50% or more damage, i.e. at or below half
                //   health. ⚠ Max <= 0 leaves the menu HEALTHY rather than dividing by zero.
                bool degraded = false;
                if (view.HasComponent<Health>(entity))
                {
                    ref readonly var health = ref view.GetComponentRO<Health>(entity);
                    degraded = health.Max > 0f && health.Current / health.Max <= 0.5f;
                }
                bool brain = AiTraceActions.HasBrain(view, entity);   // ⭐ CE-3123 — has a brain = can be traced
                var repo = view as EntityRepository;
                // ⭐ CE-3169 — a map that offers orders; ⭐ DAR F7 (D-E) — and never while it shows a recording (a review or a replay), read
                //   each frame from the world's flag (R-3064), so a host that enters a review AFTER its map was built hides them too.
                bool showsRecording = Fdp.Toolkit.Replay.ReplayState.IsActive(view);
                bool orders = Hrot.Common.Orders.OrdersOffered.IsOfferedIn(view);   // ⭐ CE-1363 — the one shared test (the View menu asks it too)
                var dis = repo != null ? repo.GetDisType(entity) : default;                                 // ⭐ CE-2143 — its class
                bool isUnit = view.HasComponent<Fdp.Core.CommandHierarchy.UnitEntity>(entity);
                bool form = orders && repo!.HasSingletonManaged<Hrot.Common.Orders.IOrderForm>();             // ⭐ CE-1059
                bool manned = Fdp.Toolkit.Behavior.Possession.Possessability.Refusal(view, entity) != null;   // ⭐ CE-1205
                var embark = repo == null || isUnit || showsRecording ? EmbarkItem.None                                // ⭐ CE-1230
                    : Hrot.Common.Orders.EmbarkOrders.IsAboard(repo, entity) ? EmbarkItem.Disembark
                    : Hrot.Common.Orders.EmbarkOrders.CanOffer(repo, entity) ? EmbarkItem.Embark : EmbarkItem.None;
                var pins = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.None;   // ⭐ CE-1329 — the same store the Brain tab reads
                foreach (var f in Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All)
                    if (GizmoPins.IsPinned(view, entity, f)) pins |= f;
                bool traceOn = brain && repo != null && Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.NetIdOf(repo, entity) is var nid and not 0
                               && Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Peek(repo)?.IsTraceSet(nid) == true;
                menuJson = UnitMenuFor(degraded, brain, orders, dis, isUnit, form, manned, embark, pins, traceOn);
            }

            return menuJson;
        }
    }
}
