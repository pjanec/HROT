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

    /// <summary>Whether a task's place (a value of <see cref="Of"/>) can be dragged on the map: a POINT. ⛔ An entity is where the entity is, and an
    /// area is reshaped by its own editor — neither moves by dragging the task's marker.</summary>
    public static bool IsMovable(object? place) => place is Fdp.Toolkit.Replication.PointLink;

    /// <summary>
    /// ⭐ W-1 / P-8 8d-2 (E2: <i>"dragging a marker writes that field"</i>) — <paramref name="paramsJson"/> of an order task with its FIRST place field
    /// moved to <paramref name="to"/> (x, y; the field's own height kept), written as coordinates. <c>false</c> (and <paramref name="json"/> unchanged)
    /// when the task is not an order, its params do not read, or its first place is not a point (<see cref="IsMovable"/>).
    /// </summary>
    public static bool MovePlace(string behaviorName, string? paramsJson, System.Numerics.Vector2 to, out string json)
    {
        json = paramsJson ?? string.Empty;
        if (!behaviorName.StartsWith("Order.", StringComparison.Ordinal)
            || !Hrot.Common.Orders.OrderMenu.TryGetByName(behaviorName.Substring("Order.".Length), out var order)) return false;
        object dto;
        try { dto = Hrot.Presentation.Behavior.BehaviorParamsForm.Hydrate(order.Dto, string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidCastException or NullReferenceException or NotSupportedException) { return false; }
        if (FirstPlaceMember(dto) is not { } m) return false;
        var value = m is PropertyInfo pi ? pi.GetValue(dto) : ((FieldInfo)m).GetValue(dto);
        if (value is not Fdp.Toolkit.Replication.PointLink old) return false;
        var moved = new Fdp.Toolkit.Replication.PointLink(new System.Numerics.Vector3(to.X, to.Y, ((System.Numerics.Vector3)old).Z));   // a drag is coordinates: a named point's binding is replaced
        if (m is PropertyInfo p) { if (!p.CanWrite) return false; p.SetValue(dto, moved); } else ((FieldInfo)m).SetValue(dto, moved);
        json = Hrot.Presentation.Behavior.BehaviorParamsForm.ToJson(dto, order.Dto);
        return true;
    }

    private static object? FirstPlace(object dto)
        => FirstPlaceMember(dto) is { } m ? (m is PropertyInfo pi ? pi.GetValue(dto) : ((FieldInfo)m).GetValue(dto)) : null;

    /// <summary>The first place member (declaration order) holding a value — the ONE rule <see cref="PlacesOf"/> and <see cref="MovePlace"/> share.</summary>
    private static MemberInfo? FirstPlaceMember(object dto)
    {
        var members = new List<(int Order, MemberInfo M)>();
        foreach (var p in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) if (p.CanRead && Fdp.Presentation.Editing.MapPreviews.Has(p.PropertyType)) members.Add((p.MetadataToken, p));
        foreach (var f in dto.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) if (Fdp.Presentation.Editing.MapPreviews.Has(f.FieldType)) members.Add((f.MetadataToken, f));
        members.Sort((a, b) => a.Order.CompareTo(b.Order));   // declaration order: the FIRST place field (E2)
        foreach (var (_, m) in members)
        {
            var v = m is PropertyInfo pi ? pi.GetValue(dto) : ((FieldInfo)m).GetValue(dto);
            if (v != null) return m;
        }
        return null;
    }
}
