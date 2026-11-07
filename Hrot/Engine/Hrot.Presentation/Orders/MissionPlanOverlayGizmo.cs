using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Diagnostics.Gizmos;
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ W-1 / P-8 8d (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.5 M3, §5.8 E2, §5.12; mockup <c>mission-plan-editor.svg</c>) — a SELECTED unit's
/// mission plan ON THE MAP: a numbered marker at each task's place — its FIRST place field (a point, an entity's position, an area's centre:
/// <c>MapPreviews.PlaceOf</c>, the one "has a place" table); a task with no place (<i>Hold</i>) sits at the previous one's — and the route joining
/// them from the unit, solid to the first task, dashed after, in the unit's side colour. A marker is green when its task is done, amber while it
/// runs, grey while planned (<see cref="MissionPlanQueue"/>). A passive global gizmo like <see cref="FireMissionOverlayGizmo"/>, registered by
/// <c>MapInteractionPack</c> on every map host. ⭐ The places are worked out once per plan VALUE (the record is replaced on every change, CE-4046);
/// a steady frame only re-reads positions.
/// </summary>
public sealed class MissionPlanOverlayGizmo : IEntityStatefulGizmo
{
    private static readonly Rgba32 DoneColour = new(91, 208, 106), RunningColour = new(242, 192, 51), PlannedColour = new(60, 60, 70), Rim = new(255, 255, 255);
    private static readonly FixedString32[] Numbers = BuildNumbers(MissionPlanQueue.MaxTasks);

    private readonly Func<IReadOnlyCollection<Entity>> _selected;
    private readonly Dictionary<Entity, (ActiveMissionPlan Plan, object?[] Places)> _cache = new();
    private readonly List<Vector3> _route = new();

    public MissionPlanOverlayGizmo(Func<IReadOnlyCollection<Entity>> selected)
        => _selected = selected ?? throw new ArgumentNullException(nameof(selected));

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <summary>The markers drawn by the last frame (rails).</summary>
    public int MarkersDrawn { get; private set; }

    /// <summary>The marker points of the last frame, in task order (rails).</summary>
    public IReadOnlyList<Vector3> LastRoute => _route;

    /// <summary>
    /// The place value of each task of <paramref name="plan"/> (null = no place: it sits at the previous one's): an order task's DTO
    /// (<c>Order.&lt;Kind&gt;</c>) hydrated from its params, its FIRST field whose type has a place on the map. A behaviour task has none (⏳ its params' places).
    /// </summary>
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

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        MarkersDrawn = 0;
        _route.Clear();
        if (view is not EntityRepository world || !world.TryGetTable(typeof(ActiveMissionPlan), out _)) return;
        var selected = _selected();
        if (selected.Count == 0) return;
        Fdp.Toolkit.Scenario.LiveRefScope? refs = null;
        foreach (var unit in selected)
        {
            if (!world.IsAlive(unit) || !world.HasManagedComponent<ActiveMissionPlan>(unit) || !world.HasComponent<SimTransform>(unit)) continue;
            if (view.GetManagedComponentRO<ActiveMissionPlan>(unit) is not { } amp || amp.Plan.Tasks.Count == 0) continue;
            if (!_cache.TryGetValue(unit, out var c) || !ReferenceEquals(c.Plan, amp)) _cache[unit] = c = (amp, PlacesOf(amp.Plan));
            refs ??= Hrot.UI.Common.Rules.AuthoredRules.ScopeOf(world);
            var colour = world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(unit) ? SidePalette.Of(world.GetComponentRO<EntityInfo>(unit).ForceId) : SidePalette.Neutral;
            int current = world.IsComponentTypeRegistered<MissionPlanQueue>() && world.HasComponent<MissionPlanQueue>(unit) ? world.GetComponentRO<MissionPlanQueue>(unit).CurrentTask : -1;
            var from = world.GetComponentRO<SimTransform>(unit).Position;
            var previous = from;
            int start = _route.Count;
            for (int i = 0; i < c.Places.Length && i < Numbers.Length; i++)
            {
                var at = Fdp.Presentation.Editing.MapPreviews.PlaceOf(c.Places[i], refs) ?? previous;   // E2: no place ⇒ the previous task's
                draw.DrawLine(previous, at, colour, 2f, style: i == 0 ? LineStyle.Solid : LineStyle.Dashed);
                previous = at;
                _route.Add(at);
            }
            for (int i = 0; i < _route.Count - start; i++)   // the markers over the route, numbered from 1
            {
                var at = _route[start + i];
                var fill = current < 0 ? PlannedColour : i < current ? DoneColour : i == current ? RunningColour : PlannedColour;
                draw.DrawSphere(at, 10f, Rim, thickness: 1.2f, sizeMode: SizeMode.ScreenPixels, fillColor: fill);
                // map text anchors at its top-left: the number sits just BELOW its marker (as the show-on-map labels do), white over the map
                draw.DrawText(at.X, at.Y, Numbers[i], new Rgba32(240, 240, 245), fontSizePx: 14f, lineOffsetPx: 12f);
                MarkersDrawn++;
            }
        }
    }

    private static FixedString32[] BuildNumbers(int n)
    {
        var a = new FixedString32[n];
        for (int i = 0; i < n; i++) a[i] = new FixedString32((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return a;
    }

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnDragUpdate(Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMenuAction(int actionId) { }
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    public void Dispose() { }
}
