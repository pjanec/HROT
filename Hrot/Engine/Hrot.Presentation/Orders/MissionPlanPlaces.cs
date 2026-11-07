using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ W-1 / P-8 8d (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8 E2, §5.12) — WHERE each task of a plan is: an order task's (<c>Order.&lt;Kind&gt;</c>) DTO,
/// hydrated from its params, its FIRST field whose type has a place on the map (<c>MapPreviews.Has</c> — the one table); a behaviour task has none.
/// The value (a <c>PointLink</c>, an <c>EntityLink</c>, an <c>AreaLink</c>…) is resolved to a position by <c>MapPreviews.PlaceOf</c> at draw time, so a
/// moving target is followed. ⭐ Worked out ONCE per plan VALUE (the record is replaced on every change, CE-4046) — <see cref="Of"/> caches it.
/// </summary>
public static class MissionPlanPlaces
{
    private static readonly ConditionalWeakTable<ActiveMissionPlan, object?[]> Cache = new();

    /// <summary>The place values of <paramref name="plan"/>'s tasks (null = no place), cached per plan value.</summary>
    public static object?[] Of(ActiveMissionPlan plan) => Cache.GetValue(plan, p => PlacesOf(p.Plan));

    /// <summary>The place value of each task (null = none: it sits at the previous task's place).</summary>
    public static object?[] PlacesOf(DomainMissionPlan plan)
    {
        var places = new object?[plan.Tasks.Count];
        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var t = plan.Tasks[i];
            if (!t.BehaviorName.StartsWith("Order.", StringComparison.Ordinal)
                || !Hrot.Common.Orders.OrderMenu.TryGetByName(t.BehaviorName.Substring("Order.".Length), out var order)) continue;
            object dto;
            try { dto = Hrot.Presentation.Behavior.BehaviorParamsForm.Hydrate(order.Dto, string.IsNullOrWhiteSpace(t.BehaviorParams) ? "{}" : t.BehaviorParams); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidCastException or NullReferenceException or NotSupportedException) { continue; }
            places[i] = FirstPlace(dto);
        }
        return places;
    }

    private static object? FirstPlace(object dto)
    {
        var members = new List<(int Order, MemberInfo M)>();
        foreach (var p in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) if (p.CanRead && Fdp.Presentation.Editing.MapPreviews.Has(p.PropertyType)) members.Add((p.MetadataToken, p));
        foreach (var f in dto.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) if (Fdp.Presentation.Editing.MapPreviews.Has(f.FieldType)) members.Add((f.MetadataToken, f));
        members.Sort((a, b) => a.Order.CompareTo(b.Order));   // declaration order: the FIRST place field (E2)
        foreach (var (_, m) in members)
        {
            var v = m is PropertyInfo pi ? pi.GetValue(dto) : ((FieldInfo)m).GetValue(dto);
            if (v != null) return v;
        }
        return null;
    }
}
