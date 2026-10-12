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
}
