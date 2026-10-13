using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Replication;
using Hrot.Common.Orders;
using Hrot.Editor.DebugApi;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior.Orders;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐ <c>CE-2140</c> — the order vocabulary is one-to-one (docs/DESIGN_Orders_Params_Entity_Classes.md §1 O1, §2): every
/// <see cref="OrderKind"/> has exactly ONE order DTO marked <c>[Order]</c>, and every <c>[Order]</c> names an <see cref="OrderKind"/>.
/// ⭐ A new order is one enum member plus one DTO; this rail fails if either half is forgotten.
/// </summary>
public sealed class OrderVocabularyTests
{
    private static (Type Dto, OrderAttribute Order)[] OrderDtos()
        => typeof(OrderKind).Assembly.GetTypes()
            .Select(t => (Dto: t, Order: t.GetCustomAttribute<OrderAttribute>()))
            .Where(x => x.Order is not null)
            .Select(x => (x.Dto, x.Order!))
            .ToArray();

    [Fact]
    public void CE2140_EveryOrderKindHasExactlyOneOrderDto_AndEveryOrderDtoNamesAKind()
    {
        var dtos = OrderDtos();
        var kinds = Enum.GetNames<OrderKind>();

        foreach (var kind in kinds)
            Assert.True(dtos.Count(d => d.Order.Name == kind) == 1,
                $"OrderKind.{kind} needs exactly one [Order(nameof(OrderKind.{kind}))] DTO; found {dtos.Count(d => d.Order.Name == kind)}");

        foreach (var (dto, order) in dtos)
            Assert.True(kinds.Contains(order.Name), $"{dto.Name} is [Order(\"{order.Name}\")] but OrderKind has no {order.Name}");
    }

    /// <summary>Order DTOs are stored as a behaviour's <c>Params</c> when it has no resolver (§1 P3), so they are value types.</summary>
    [Fact]
    public void CE2140_OrderDtosAreValueTypes()
    {
        foreach (var (dto, _) in OrderDtos())
            Assert.True(dto.IsValueType, $"{dto.Name} must be a struct: an order behaviour without a resolver stores it as Params");
    }

    /// <summary>
    /// ⭐ <c>CE-2142</c> (rule O2) — a behaviour NAMED <c>&lt;Class&gt;&lt;Order&gt;</c> (a class of the one table, an order of the
    /// vocabulary) is what the receiver starts for that order, so it must take that order's DTO — otherwise the order's JSON lands
    /// in the wrong shape. Checked over the PRODUCTION registry; and the shipped squad's three are found (the rail is not vacuous).
    /// </summary>
    [Fact]
    public void CE2142_EveryClassOrderBehaviour_TakesItsOrdersDto()
    {
        var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
        Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
        var found = new System.Collections.Generic.List<string>();
        foreach (var cls in Hrot.Core.Tkb.EntityClassTable.Default.Classes)
            foreach (var (dto, order) in OrderDtos())
            {
                string name = cls.Name + order.Name;
                if (!registry.TryGetId(name, out int id) || !registry.TryGetDefinition(id, out var def)) continue;
                found.Add(name);
                Assert.True(def.JsonParamsDtoType == dto,
                    $"{name} fulfils order {order.Name} for class {cls.Name}, so it must take {dto.Name}; it takes {def.JsonParamsDtoType?.Name ?? "nothing"}");
            }
        Assert.Contains("InfantrySquadMove", found);
        Assert.Contains("InfantrySquadHold", found);
        Assert.Contains("InfantrySquadAttack", found);
    }

    // ── CE-2148 (P5, design §13.9) — mandatory fields, the completeness rule, the draft ──────────────────────────────

    private struct TwoPicks { [Required] public EntityRef Who; [Required] public PickableGeoPoint Where; }
    private struct NeedsANumber { [Required] public float Range; public EntityRef Optional; }
    private struct OptionalPick { [MapPickableEntity("tactical_graphics")] public EntityRef Area; }

    private static OrderInfo Probe(Type dto)
    {
        var fields = OrderMenu.FieldsOf(dto);
        var (pick, field) = OrderMenu.PickOf(dto);
        return new OrderInfo("Probe", dto, 0, OrderGesture.None, pick, field, fields);
    }

    /// <summary>⭐ F5 — Move's point and Attack's target are <c>[Required]</c>, so each needs its one pick; Hold needs nothing.</summary>
    [Fact]
    public void CE2148_MoveAndAttack_RequireTheirPick_HoldNeedsNothing()
    {
        Assert.True(OrderMenu.TryGetByName("Move", out var move));
        Assert.Equal((OrderPick.Point, "Location"), (move.Pick, move.PickField));
        Assert.True(move.Fields.Single(f => f.Name == "Location").Required);
        Assert.False(move.Fields.Single(f => f.Name == "Speed").Required);
        Assert.True(OrderMenu.TryGetByName("Attack", out var attack));
        Assert.Equal((OrderPick.Entity, "Target"), (attack.Pick, attack.PickField));
        Assert.True(OrderMenu.TryGetByName("Hold", out var hold));
        Assert.Equal(OrderPick.None, hold.Pick);
        Assert.Empty(hold.Fields);
    }

    /// <summary>⭐ P5-c — only REQUIRED fields count: two required picks, or a required value to type in, need the order FORM
    /// (<c>Several</c>); an optional pick needs nothing. The entity-pick presets are read from <c>[MapPickableEntity]</c>.</summary>
    [Fact]
    public void CE2148_OnlyRequiredFieldsDecideThePick_SeveralMeansTheForm()
    {
        Assert.Equal(OrderPick.Several, OrderMenu.PickOf(typeof(TwoPicks)).Pick);
        Assert.Equal(OrderPick.Several, OrderMenu.PickOf(typeof(NeedsANumber)).Pick);
        Assert.Equal(OrderPick.None, OrderMenu.PickOf(typeof(OptionalPick)).Pick);
        var area = OrderMenu.FieldsOf(typeof(OptionalPick)).Single();
        Assert.Equal((OrderFieldKind.MapEntity, false), (area.Kind, area.Required));
        Assert.Equal(new[] { "tactical_graphics" }, area.Presets);
    }

    /// <summary>⭐ P5-a — THE completeness rule: a required key matches case-insensitively (R-2005); absent, null, or an entity of 0
    /// is missing.</summary>
    [Fact]
    public void CE2148_MissingIn_IsCaseInsensitive_AndNullOrAZeroEntityIsMissing()
    {
        Assert.True(OrderMenu.TryGetByName("Attack", out var attack));
        Assert.Empty(OrderMenu.MissingIn(attack, new JsonObject { ["target"] = 88 }));
        Assert.Equal("Target", OrderMenu.MissingIn(attack, new JsonObject()).Single().Name);
        Assert.Single(OrderMenu.MissingIn(attack, new JsonObject { ["Target"] = 0 }));
        Assert.Single(OrderMenu.MissingIn(attack, new JsonObject { ["Target"] = null }));
        Assert.Single(OrderMenu.MissingIn(attack, null));
    }

    /// <summary>⭐ P5-b — the gesture's context fills the ONE missing required field of its kind; it never overwrites an explicit
    /// value and never fills an optional field; two missing fields of one kind stay missing (the form's job).</summary>
    [Fact]
    public void CE2148_Draft_TheContextFillsOnlyTheOneMissingRequiredFieldOfItsKind()
    {
        Assert.True(OrderMenu.TryGetByName("Move", out var move));
        var point = new JsonArray(50.0, 14.0);
        var d = OrderMenu.Draft(move, new long[] { 7 }, new OrderContext(88, point), _ => Array.Empty<OrderResult>());
        Assert.True(d.IsComplete);
        Assert.Equal("[50,14]", d.Params["Location"]!.ToJsonString());
        Assert.False(d.Params.ContainsKey("Target"));

        var explicitPoint = new JsonObject { ["location"] = new JsonArray(1.0, 2.0) };
        d = OrderMenu.Draft(move, new long[] { 7 }, new OrderContext(null, point), _ => Array.Empty<OrderResult>(), explicitPoint);
        Assert.Equal("[1,2]", d.Params["location"]!.ToJsonString());
        Assert.False(d.Params.ContainsKey("Location"));

        var two = Probe(typeof(TwoPicks));
        d = OrderMenu.Draft(two, new long[] { 7 }, new OrderContext(88, null), _ => Array.Empty<OrderResult>());
        Assert.Equal(88, d.Params["Who"]!.GetValue<long>());
        Assert.Equal("Where", d.Missing.Single().Name);

        var optional = Probe(typeof(OptionalPick));
        d = OrderMenu.Draft(optional, new long[] { 7 }, new OrderContext(88, null), _ => Array.Empty<OrderResult>());
        Assert.True(d.IsComplete);
        Assert.Empty(d.Params);
    }

    /// <summary>⭐ F5 — the params schema lists the required fields (what <c>GET /orders</c> and <c>GET /behaviors</c> publish).</summary>
    [Fact]
    public void CE2148_TheSchema_ListsTheRequiredFields()
    {
        var schema = DtoJsonSchemaExtractor.ExtractParams(typeof(MoveOrder));
        Assert.Equal("[\"Location\"]", schema["required"]!.ToJsonString());
        Assert.Null(DtoJsonSchemaExtractor.ExtractParams(typeof(HoldOrder))["required"]);
    }
}
