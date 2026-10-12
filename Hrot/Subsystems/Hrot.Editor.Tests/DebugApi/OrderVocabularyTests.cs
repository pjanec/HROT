using System;
using System.Linq;
using System.Reflection;
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
}
