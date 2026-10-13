using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Common.Orders
{
    /// <summary>⭐ <c>CE-2149</c> (F7 stage 1, docs/DESIGN_Orders_Params_Entity_Classes.md §13.3) — what a designation stands for. Each maps to
    /// an order-field shape: <see cref="Target"/> an <c>EntityRef</c> with no route / area preset, <see cref="MapPoint"/> a
    /// <c>PickableGeoPoint</c>, <see cref="Route"/> an <c>EntityRef</c> on <c>road_graphs</c>, <see cref="Area"/> an <c>EntityRef</c> on
    /// <c>tactical_graphics</c> (a sector, F10).</summary>
    public enum DesignationKind : byte { Target = 0, MapPoint = 1, Route = 2, Area = 3 }

    /// <summary>
    /// ⭐⭐ <c>CE-2149</c> (F7 stage 1, R-2003, design §13.3) — the operator's standing designations per unit: the target, point, route and
    /// sector a later order may take when its gesture did not say. A world singleton per STATION (operator UI state — not replicated, not
    /// saved, R-136), keyed by the unit's network id. ⭐ Read ONLY by <see cref="OrderMenu.Draft"/> for an operator's draft, so the order
    /// on the wire is complete and the HTTP / AI path (<see cref="EntityOrders"/>) never sees a designation.
    /// </summary>
    [ComponentId(GlobalComponentIds.OrderDesignations)]
    public sealed class OrderDesignations
    {
        /// <summary>The <c>[MapPickableEntity]</c> preset that makes an entity field a route.</summary>
        public const string RoutePreset = "road_graphs";

        /// <summary>The <c>[MapPickableEntity]</c> preset that makes an entity field an area (a sector).</summary>
        public const string AreaPreset = "tactical_graphics";

        private readonly Dictionary<(long Unit, DesignationKind Kind), JsonNode> _values = new();

        /// <summary>Designates <paramref name="value"/> (a network id for an entity kind, <c>[lat, lon]</c> for a point) for the unit.</summary>
        public void Set(long unit, DesignationKind kind, JsonNode value) => _values[(unit, kind)] = value.DeepClone();

        /// <summary>The raw designation, stale or not.</summary>
        public bool TryGet(long unit, DesignationKind kind, out JsonNode value)
        {
            if (_values.TryGetValue((unit, kind), out var v)) { value = v.DeepClone(); return true; }
            value = null!;
            return false;
        }

        /// <summary>Forgets every designation of the unit; answers how many there were.</summary>
        public int ClearAll(long unit)
        {
            var keys = _values.Keys.Where(k => k.Unit == unit).ToArray();
            foreach (var k in keys) _values.Remove(k);
            return keys.Length;
        }

        /// <summary>Every designation of the unit (what an overlay draws).</summary>
        public IReadOnlyList<(DesignationKind Kind, JsonNode Value)> Of(long unit)
            => _values.Where(kv => kv.Key.Unit == unit).Select(kv => (kv.Key.Kind, kv.Value.DeepClone())).ToArray();

        /// <summary>
        /// ⭐ The lookup <see cref="OrderMenu.Draft"/> takes: the unit's designation of a kind, or null. An ENTITY designation that is no
        /// longer live (<paramref name="isLive"/>) is DROPPED here and not filled — a dead target must not silently become an order
        /// (§13.3 staleness); the gesture then picks, or the form opens.
        /// </summary>
        public Func<long, DesignationKind, JsonNode?> Lookup(Func<long, bool> isLive)
            => (unit, kind) =>
            {
                if (!_values.TryGetValue((unit, kind), out var v)) return null;
                if (kind != DesignationKind.MapPoint && !(v is JsonValue jv && jv.TryGetValue<long>(out long id) && isLive(id)))
                {
                    _values.Remove((unit, kind));
                    return null;
                }
                return v.DeepClone();
            };

        /// <summary>The designation kind an order field takes, or null for a value typed in (§13.3 the kinds).</summary>
        public static DesignationKind? KindOf(OrderField field) => field.Kind switch
        {
            OrderFieldKind.MapPoint => DesignationKind.MapPoint,
            OrderFieldKind.MapEntity when field.Presets.Contains(RoutePreset) => DesignationKind.Route,
            OrderFieldKind.MapEntity when field.Presets.Contains(AreaPreset) => DesignationKind.Area,
            OrderFieldKind.MapEntity => DesignationKind.Target,
            _ => null,
        };

        /// <summary>The station's store, or null when nothing was ever designated on this world.</summary>
        public static OrderDesignations? Of(EntityRepository world)
            => world.HasSingletonManaged<OrderDesignations>() ? world.GetSingletonManaged<OrderDesignations>() : null;

        /// <summary>The station's store, created on first use.</summary>
        public static OrderDesignations For(EntityRepository world)
        {
            if (Of(world) is { } existing) return existing;
            var created = new OrderDesignations();
            world.SetSingletonManaged(created);
            return created;
        }

        /// <summary>⭐ A designated entity is live while it exists on this node and is not killed (<c>Health</c> ≤ 0 — the CE-466 meaning of
        /// alive for a target); an entity without <c>Health</c> (a route, an area) is live while it exists.</summary>
        public static bool IsLive(EntityRepository world, NetworkEntityMap map, long networkId)
        {
            if (!map.TryGetEntity(networkId, out var e) || !world.IsAlive(e)) return false;
            return !(world.IsComponentTypeRegistered<Health>() && world.HasComponent<Health>(e) && world.GetComponentRO<Health>(e).Current <= 0);
        }
    }
}
