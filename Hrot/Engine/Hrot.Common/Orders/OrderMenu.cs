using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    /// <summary>What a front-end must ask the operator before an order can go: nothing, a point, an entity — or more
    /// (⭐ <c>CE-2148</c>: then it needs the order FORM, <see cref="IOrderForm"/>). Counted over the <c>[Required]</c> fields only.</summary>
    public enum OrderPick : byte { None = 0, Point = 1, Entity = 2, Several = 3 }

    /// <summary>⭐ <c>CE-2148</c> — what kind of value an order field takes: a map point, an entity, or a value typed in.
    /// ⚠ The member names must differ from <see cref="OrderPick"/>'s: the DDS code generator emits every enum of this assembly into ONE
    /// IDL scope per file, where enumerators share a namespace.</summary>
    public enum OrderFieldKind : byte { Value = 0, MapPoint = 1, MapEntity = 2 }

    /// <summary>⭐ <c>CE-2148</c> (F5, F6) — one field of an order's DTO, as a front-end sees it.</summary>
    /// <param name="Name">Its JSON name.</param>
    /// <param name="Required">Marked <c>[Required]</c> (R-2002): the order cannot go without it.</param>
    /// <param name="Presets">The <c>[MapPickableEntity]</c> presets narrowing an entity pick (empty = any entity).</param>
    public sealed record OrderField(string Name, Type Type, OrderFieldKind Kind, bool Required, IReadOnlyList<string> Presets);

    /// <summary>One order of the vocabulary, as a front-end sees it.</summary>
    /// <param name="ActionId">Its unit-menu action id (<see cref="OrderMenu.ActionBase"/> + its index in name order).</param>
    /// <param name="PickField">The JSON name of the field the pick fills (null when <paramref name="Pick"/> is None or Several).</param>
    /// <param name="Fields">⭐ <c>CE-2148</c> — every field of the DTO.</param>
    public sealed record OrderInfo(string Name, Type Dto, int ActionId, OrderGesture Gesture, OrderPick Pick, string? PickField,
        IReadOnlyList<OrderField> Fields)
    {
        /// <summary>The field the one pick fills (null when <see cref="Pick"/> is None or Several).</summary>
        public OrderField? PickTarget => PickField == null ? null : Fields.FirstOrDefault(f => f.Name == PickField);
    }

    /// <summary>⭐ <c>CE-2148</c> (F6) — what the operator's gesture points at: the clicked entity (network id) and / or the clicked
    /// point (geodetic <c>[lat, lon]</c>, or <c>[y, x]</c> on a node without a transform — OrderGeo's fallback). It fills a missing
    /// <c>[Required]</c> field of the same kind.</summary>
    public readonly record struct OrderContext(long? Entity, JsonArray? Point)
    {
        public static OrderContext None => default;
    }

    /// <summary>
    /// ⭐ <c>CE-2148</c> (F6, P5b seam) — an order on its way: the units, the order, its params as filled so far and the
    /// <c>[Required]</c> fields still <see cref="Missing"/>. <see cref="Send"/> issues it to every unit (through <c>EntityOrders</c>)
    /// and answers one result per unit — what an order form calls when the operator presses Send.
    /// </summary>
    public sealed record OrderDraft(IReadOnlyList<long> Units, OrderInfo Order, JsonObject Params, IReadOnlyList<OrderField> Missing,
        Func<string, IReadOnlyList<OrderResult>> Send)
    {
        /// <summary>True when nothing required is missing: the order can go as it is.</summary>
        public bool IsComplete => Missing.Count == 0;
    }

    /// <summary>
    /// ⭐ <c>CE-2148</c> (F6) — THE order form: opened for a draft the gesture could not complete (more than one missing field, or a
    /// value to type in). Modeless; it prefills from <see cref="OrderDraft.Params"/>, lets the operator pick and type, and sends
    /// through <see cref="OrderDraft.Send"/>. Implemented by the ui lane (P5b) and registered as a world singleton; absent, an
    /// incomplete order is refused with "needs the order form".
    /// </summary>
    [ComponentId(GlobalComponentIds.IOrderForm)]
    public interface IOrderForm
    {
        void Open(OrderDraft draft);
    }

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
                var fields = FieldsOf(type);
                var (pick, field) = PickOf(fields);
                list[i] = new OrderInfo(dtos[i].OrderName!, type, ActionBase + i,
                    type.GetCustomAttribute<OrderAttribute>()?.Gesture ?? OrderGesture.None, pick, field, fields);
            }
            return list;
        }

        /// <summary>⭐ <c>CE-2148</c> — the DTO's public fields as order fields (a property is still read, R-2002 forbids them; a
        /// <c>[JsonIgnore]</c> member is never a field). The KIND comes from the type: a <see cref="PickableGeoPoint"/> is a point, an
        /// <see cref="EntityRef"/> an entity (DESIGN_Map_Picking_Unification P5); <c>[Required]</c> marks it mandatory (F5).</summary>
        public static IReadOnlyList<OrderField> FieldsOf(Type dto)
        {
            const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
            var members = dto.GetFields(Public).Select(f => ((MemberInfo)f, f.FieldType))
                .Concat(dto.GetProperties(Public).Select(p => ((MemberInfo)p, p.PropertyType)));
            return members
                .Where(m => !m.Item1.IsDefined(typeof(JsonIgnoreAttribute), inherit: true))
                .Select(m => new OrderField(
                    m.Item1.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? m.Item1.Name,
                    m.Item2,
                    m.Item2 == typeof(PickableGeoPoint) ? OrderFieldKind.MapPoint : m.Item2 == typeof(EntityRef) ? OrderFieldKind.MapEntity : OrderFieldKind.Value,
                    m.Item1.IsDefined(typeof(RequiredAttribute), inherit: true),
                    m.Item1.GetCustomAttribute<Fdp.Toolkit.Behavior.Attributes.MapPickableEntityAttribute>()?.FilterPresets ?? Array.Empty<string>()))
                .ToArray();
        }

        /// <summary>⭐ D4-E / <c>CE-2148</c> — the pick an order needs before it can go, over its <c>[Required]</c> fields: none, one
        /// point, one entity — or <see cref="OrderPick.Several"/> (two picks, or a value to type in): the order form's job.</summary>
        public static (OrderPick Pick, string? Field) PickOf(Type dto) => PickOf(FieldsOf(dto));

        private static (OrderPick Pick, string? Field) PickOf(IReadOnlyList<OrderField> fields)
        {
            var required = fields.Where(f => f.Required).ToArray();
            if (required.Length == 0) return (OrderPick.None, null);
            if (required.Length > 1 || required[0].Kind == OrderFieldKind.Value) return (OrderPick.Several, null);
            return (required[0].Kind == OrderFieldKind.MapPoint ? OrderPick.Point : OrderPick.Entity, required[0].Name);
        }

        /// <summary>⭐ <c>CE-2148</c> (P5-a) — the <c>[Required]</c> fields <paramref name="json"/> does not set: absent (keys match
        /// case-insensitively, R-2005), null, or an entity of 0. THE one rule for "complete" — the front-ends ask it to choose send /
        /// pick / form, and <c>EntityOrders</c> refuses with it.</summary>
        public static IReadOnlyList<OrderField> MissingIn(OrderInfo order, JsonObject? json)
        {
            List<OrderField>? missing = null;
            foreach (var f in order.Fields)
            {
                if (!f.Required) continue;
                JsonNode? value = null;
                bool found = false;
                if (json != null)
                    foreach (var kv in json)
                        if (string.Equals(kv.Key, f.Name, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; found = true; break; }
                // ⚠ an entity of 0 is no entity: compared as JSON text, because a JsonValue built from an int does not answer
                //   TryGetValue<long> (the CE-2148 rail caught it).
                bool set = found && value != null
                    && !(f.Kind == OrderFieldKind.MapEntity && value is JsonValue && value.ToJsonString() == "0");
                if (!set) (missing ??= new List<OrderField>()).Add(f);
            }
            return missing ?? (IReadOnlyList<OrderField>)Array.Empty<OrderField>();
        }

        /// <summary>
        /// ⭐ <c>CE-2148</c> (F6, P5-b) — an order on its way: <paramref name="given"/> (explicit JSON, may be null), then the gesture's
        /// <paramref name="context"/> fills a missing <c>[Required]</c> field of its kind when exactly one such field is missing.
        /// Explicit values always win. <paramref name="send"/> is what <see cref="OrderDraft.Send"/> calls.
        /// </summary>
        public static OrderDraft Draft(OrderInfo order, IReadOnlyList<long> units, OrderContext context,
            Func<string, IReadOnlyList<OrderResult>> send, JsonObject? given = null)
        {
            var json = given != null ? (JsonObject)given.DeepClone() : new JsonObject();
            var missing = MissingIn(order, json);
            Fill(OrderFieldKind.MapEntity, context.Entity is long e && e != 0 ? JsonValue.Create(e) : null);
            Fill(OrderFieldKind.MapPoint, context.Point?.DeepClone());
            return new OrderDraft(units, order, json, MissingIn(order, json), send);

            void Fill(OrderFieldKind kind, JsonNode? value)
            {
                if (value == null) return;
                var candidates = missing.Where(f => f.Kind == kind).ToArray();
                if (candidates.Length == 1) json[candidates[0].Name] = value;
            }
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
    }
}
