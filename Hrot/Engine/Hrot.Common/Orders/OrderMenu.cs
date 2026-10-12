using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Replication;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Behavior;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.Common.Orders
{
    /// <summary>What a front-end must ask the operator before an order can go: nothing, a point, an entity — or more than one
    /// thing (then the order is HTTP-only).</summary>
    public enum OrderPick : byte { None = 0, Point = 1, Entity = 2, Several = 3 }

    /// <summary>One order of the vocabulary, as a front-end sees it.</summary>
    /// <param name="ActionId">Its unit-menu action id (<see cref="OrderMenu.ActionBase"/> + its index in name order).</param>
    /// <param name="PickField">The JSON name of the field the pick fills (null when <paramref name="Pick"/> is None or Several).</param>
    public sealed record OrderInfo(string Name, Type Dto, int ActionId, OrderGesture Gesture, OrderPick Pick, string? PickField);

    /// <summary>
    /// ⭐⭐ <c>CE-2143</c> (D4, docs/DESIGN_Orders_Params_Entity_Classes.md §9) — THE order vocabulary for every front-end (the unit
    /// menu, the RTS tool, the HTTP route): the DTOs marked <c>[Order]</c> in <see cref="ParamsDtoRegistry"/>, and which of them an
    /// entity's class fulfils — answered by the SAME <see cref="OrderResolver"/> the receiver uses (§8), over the behaviours the
    /// registry knows. ⇒ a menu can never offer an order the unit would then ignore, and no front-end names a behaviour.
    /// <para>⚠ Built from <see cref="ParamsDtoRegistry.Default"/>, which finds the behaviours only where <c>Hrot.AI.Behaviors</c>
    /// loads; a host without it offers no orders rather than wrong ones (§9.3).</para>
    /// </summary>
    public static class OrderMenu
    {
        /// <summary>The first unit-menu action id of an order (the fixed ids end at 268); <see cref="MaxOrders"/> are reserved.</summary>
        public const int ActionBase = 400;
        public const int MaxOrders = 64;

        private static readonly object Gate = new();
        private static ParamsDtoRegistry? _builtFrom;
        private static OrderInfo[] _orders = Array.Empty<OrderInfo>();

        /// <summary>Every order, in name order (the index is stable while the vocabulary is).</summary>
        public static IReadOnlyList<OrderInfo> Orders
        {
            get
            {
                var registry = ParamsDtoRegistry.Default;
                lock (Gate)
                {
                    if (!ReferenceEquals(registry, _builtFrom))
                    {
                        _orders = Build(registry);
                        _builtFrom = registry;
                    }
                    return _orders;
                }
            }
        }

        private static OrderInfo[] Build(ParamsDtoRegistry registry)
        {
            var dtos = registry.Dtos.Where(d => d.OrderName != null).OrderBy(d => d.OrderName, StringComparer.Ordinal).Take(MaxOrders).ToArray();
            var list = new OrderInfo[dtos.Length];
            for (int i = 0; i < dtos.Length; i++)
            {
                var type = dtos[i].Type;
                var (pick, field) = PickOf(type);
                list[i] = new OrderInfo(dtos[i].OrderName!, type, ActionBase + i,
                    type.GetCustomAttribute<OrderAttribute>()?.Gesture ?? OrderGesture.None, pick, field);
            }
            return list;
        }

        /// <summary>⭐ D4-E — the pick an order's DTO needs, from its field TYPES: a <see cref="PickableGeoPoint"/> is a point, an
        /// <see cref="EntityRef"/> an entity (DESIGN_Map_Picking_Unification P5).</summary>
        public static (OrderPick Pick, string? Field) PickOf(Type dto)
        {
            var picks = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Prop: p, Kind: p.PropertyType == typeof(PickableGeoPoint) ? OrderPick.Point
                                           : p.PropertyType == typeof(EntityRef) ? OrderPick.Entity : OrderPick.None))
                .Where(x => x.Kind != OrderPick.None).ToArray();
            if (picks.Length == 0) return (OrderPick.None, null);
            if (picks.Length > 1) return (OrderPick.Several, null);
            var p0 = picks[0].Prop;
            return (picks[0].Kind, p0.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p0.Name);
        }

        public static bool TryGetById(int actionId, out OrderInfo order)
        {
            var all = Orders;
            int i = actionId - ActionBase;
            order = i >= 0 && i < all.Count ? all[i] : null!;
            return order != null;
        }

        /// <summary>The order named <paramref name="name"/> (case-insensitive).</summary>
        public static bool TryGetByName(string name, out OrderInfo order)
        {
            order = Orders.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))!;
            return order != null;
        }

        /// <summary>The order a map gesture issues (<see cref="OrderGesture"/>), or null.</summary>
        public static OrderInfo? ByGesture(OrderGesture gesture)
            => gesture == OrderGesture.None ? null : Orders.FirstOrDefault(o => o.Gesture == gesture);

        /// <summary>How <paramref name="order"/> resolves for an entity of type <paramref name="dis"/> — the receiver's own answer (§8).</summary>
        public static OrderResolver.Result Resolve(DISEntityType dis, string order)
        {
            var registry = ParamsDtoRegistry.Default;
            return OrderResolver.Resolve(EntityClassTable.Default, dis, order, n => registry.TryGetBehavior(n, out _));
        }

        /// <summary>⭐ D4-D — the orders an entity of type <paramref name="dis"/> fulfils, in name order.</summary>
        public static IReadOnlyList<OrderInfo> OfferedFor(DISEntityType dis)
            => Orders.Where(o => Resolve(dis, o.Name).Outcome == OrderResolution.Resolved).ToList();

        /// <summary>The order's JSON with its one picked field set (<c>{}</c> when it needs no pick).</summary>
        public static string Json(OrderInfo order, JsonNode? picked)
        {
            var o = new JsonObject();
            if (order.PickField != null && picked != null) o[order.PickField] = picked;
            return o.ToJsonString();
        }
    }
}
