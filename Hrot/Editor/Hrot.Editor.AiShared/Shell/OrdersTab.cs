using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Mission;
using Hrot.UI.Common.Facades;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7a (docs/UX/DESIGN_Mission_Plan_Editor.md §5.11, D8) — the <b>Orders</b> Details tab: the selected unit's plan as sentences
/// (<see cref="OrdersPlanModel"/>), the running task marked, and <i>Jump</i> / <i>Stop</i> through the ONE mission facade every host already passes
/// (<see cref="IMissionEditorService"/>). ⭐ On the shared Details bundle, so every host shows the same tab. Editing comes in 7b; ⛔ until 7d retires
/// them, the three <c>MissionPanel</c>s still edit.
/// </summary>
public sealed class OrdersTab : IDetailsViewInstance
{
    private readonly Func<IMissionEditorService?> _missions;
    private readonly Func<EntityRepository?> _world;

    public OrdersTab(Func<IMissionEditorService?> missions, Func<EntityRepository?> world) { _missions = missions; _world = world; }

    /// <summary>The rows the last frame drew (a rail reads them).</summary>
    public IReadOnlyList<OrdersTaskRow> LastRows { get; private set; } = Array.Empty<OrdersTaskRow>();

    /// <summary>The rows for <paramref name="entity"/>, read now (headless).</summary>
    public IReadOnlyList<OrdersTaskRow> RowsFor(Entity entity)
    {
        if (_missions() is not { } svc || _world() is not { } w || !w.IsAlive(entity) || !w.HasComponent<NetworkIdentity>(entity)) return Array.Empty<OrdersTaskRow>();
        int net = w.GetComponentRO<NetworkIdentity>(entity).Value;
        var (plan, _) = svc.GetMissionSnapshot(net);
        MissionPlanQueue? queue = w.IsComponentTypeRegistered<MissionPlanQueue>() && w.HasComponent<MissionPlanQueue>(entity) ? w.GetComponentRO<MissionPlanQueue>(entity) : null;
        return OrdersPlanModel.RowsOf(plan, queue);
    }

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities.Count != 1) return;
        var entity = context.Entities[0];
        var rows = LastRows = RowsFor(entity);
        if (rows.Count == 0) { ImGui.TextDisabled("No plan — this unit runs its standing behaviour."); return; }
        int net = _world() is { } w && w.HasComponent<NetworkIdentity>(entity) ? w.GetComponentRO<NetworkIdentity>(entity).Value : 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ImGui.PushID(i);
            ImGui.TextColored(ColourOf(r.State), $"{i + 1}.");
            ImGui.SameLine();
            ImGui.TextUnformatted(r.Sentence);
            if (r.Problem is { } why) { ImGui.SameLine(); ImGui.TextColored(new System.Numerics.Vector4(1f, 0.55f, 0.2f, 1f), "⚠"); if (ImGui.IsItemHovered()) ImGui.SetTooltip(why); }
            if (r.State is OrdersTaskState.Planned && _missions() is { } svc && net != 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Jump")) _ = svc.SendControlCommandAsync(net, eMissionCommandType.CMD_JUMP_TO_TASK, r.TaskId);
            }
            ImGui.PopID();
        }
        if (_missions() is { } s && net != 0 && ImGui.Button("Stop the plan")) _ = s.SendControlCommandAsync(net, eMissionCommandType.CMD_ABORT_ALL, Guid.Empty);
    }

    private static System.Numerics.Vector4 ColourOf(OrdersTaskState s) => s switch
    {
        OrdersTaskState.Running => new(0.35f, 0.75f, 1f, 1f),
        OrdersTaskState.Done    => new(0.4f, 0.85f, 0.4f, 1f),
        OrdersTaskState.Failed  => new(0.95f, 0.35f, 0.35f, 1f),
        OrdersTaskState.Halted  => new(1f, 0.7f, 0.2f, 1f),
        _                       => new(0.7f, 0.7f, 0.7f, 1f),
    };

    public void Dispose() { }
}

/// <summary>⭐ P-7 / 7a — the descriptor: offered when ONE selected unit has a plan (rank 32, beside the Rule tab).</summary>
public static class OrdersTabDescriptor
{
    public const string Id = "details.orders";

    public static DetailsViewDescriptor For(Func<IMissionEditorService?> missions, Func<EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(world);
        var instance = new OrdersTab(missions, world);
        return new DetailsViewDescriptor(
            Id:        Id,
            Title:     "Orders",
            Rank:      32,
            AppliesTo: ctx => ctx.Entities.Count == 1 && instance.RowsFor(ctx.Entities[0]).Count > 0,
            Create:    () => instance);
    }
}
