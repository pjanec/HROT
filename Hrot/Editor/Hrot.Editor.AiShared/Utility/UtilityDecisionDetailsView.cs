using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Behavior;
using Hrot.Editor.AiShared.Shell;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Utility;

/// <summary>⭐ <c>CE-1325</c> — what the host gives the view (set by the editor; a host with none shows the settings only).</summary>
public static class UtilityDecisionHost
{
    public static Func<BehaviorRegistry?>? Registry { get; set; }
    public static Func<EntityRepository?>? World { get; set; }
    /// <summary>The unit a graph follows: its pinned unit, else the selected one (the graph overlay's rule).</summary>
    public static Func<Entity?>? FollowedUnit { get; set; }
    /// <summary>Arms a unit's traces (Debug this unit's switch).</summary>
    public static Action<Entity>? Trace { get; set; }
    public static Func<Entity, string>? UnitLabel { get; set; }
}

/// <summary>The view's model, for the rails and the snapshot.</summary>
public sealed record UtilityDecisionDetailsViewModel(string PanelId, string PanelKind, IReadOnlyList<string> Decisions, string? Unit) : IPanelViewModel
{
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md §1, §2 W1 W2 W3 W4 W6) — the <b>Decision</b> tab: a selected BTree node / HSM state
/// / HSM transition that binds a utility decision (<see cref="UtilityBindings"/>) shows that decision's settings, and the followed unit's
/// values when it runs this behaviour. Read-only. Applies only when the selection binds a decision.
/// </summary>
public sealed class UtilityDecisionDetailsView : IDetailsViewInstance
{
    private int _chosen;

    public static IReadOnlyList<BoundDecision> BoundBy(DetailsContext context)
        => context.Selection is { Count: 1 } one && context.Asset is { } asset
            ? UtilityBindings.Of(UtilityDecisionHost.Registry?.Invoke(), asset.Name, one[0])
            : Array.Empty<BoundDecision>();

    /// <summary>The sheet the view shows for <paramref name="bound"/>: the settings, and the followed unit's values when it runs
    /// <paramref name="behaviour"/>.</summary>
    public static UtilityDecisionSheet? SheetFor(BoundDecision bound, string behaviour)
    {
        if (UtilityDecisionSheet.Build(bound.DecisionId) is not { } sheet) return null;
        if (UtilityDecisionHost.World?.Invoke() is { } w && UtilityDecisionHost.FollowedUnit?.Invoke() is { } unit && w.IsAlive(unit)
            && w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.BehaviorState>()
            && w.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit)
            && w.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(unit).ActiveBehaviorHash == BehaviorHash.FromName(behaviour))
            return sheet.WithLive(w, unit);
        return sheet;
    }

    public void Draw(DetailsContext context, string idScope)
    {
        var bound = BoundBy(context);
        string behaviour = context.Asset?.Name ?? "";
        var sheets = bound.Select(b => (b, SheetFor(b, behaviour))).Where(x => x.Item2 != null).ToList();
        string panelId = $"{idScope}/{UtilityDecisionDetailsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);
        if (PanelSnapshot.CaptureEnabled)
            PanelSnapshot.Register(new UtilityDecisionDetailsViewModel(panelId, UtilityDecisionDetailsViewDescriptor.ViewId,
                sheets.Select(s => s.Item2!.Name).ToArray(), sheets.FirstOrDefault().Item2?.Unit is { } u ? $"#{u.Index}" : null));
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        if (sheets.Count == 0) { ImGui.TextDisabled("This node's decision is not registered here."); return; }

        // W6 — a node that holds more than one decision: pick
        if (sheets.Count > 1)
        {
            ImGui.TextUnformatted($"This node uses {sheets.Count} decisions:");
            for (int i = 0; i < sheets.Count; i++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{sheets[i].Item2!.Name}##d{i}", _chosen == i)) _chosen = i;
            }
        }
        _chosen = Math.Clamp(_chosen, 0, sheets.Count - 1);
        var (b, sheet) = sheets[_chosen];
        // W3 — an IsOption reads the winner: say which option this branch stands for
        if (b.Role == DecisionRole.Reads)
        {
            string option = sheet!.Options.FirstOrDefault(o => o.OptionId == b.Option)?.Name ?? $"option {b.Option}";
            ImGui.TextWrapped($"This branch runs while {sheet.Name} picks > {option} <");
            ImGui.TextDisabled($"(scored by ChooseOption in the parent, param {b.Via}; this only reads the winner)");
        }
        else ImGui.TextDisabled($"bound by {RoleText(b.Role)} (param {b.Via})");
        ImGui.Separator();
        UtilityDecisionWidget.Draw(sheet!, idScope, openOption: b.Role == DecisionRole.Reads ? b.Option : -1,
            unitLabel: UtilityDecisionHost.UnitLabel, trace: sheet!.Unit is { } u2 && UtilityDecisionHost.Trace is { } t ? () => t(u2) : null);
        if (sheet.Unit == null)
            ImGui.TextDisabled("(no unit: pin this graph to a unit, or select one that runs it, to see its values)");
    }

    private static string RoleText(DecisionRole r) => r switch
    {
        DecisionRole.Chooses => "ChooseOption",
        DecisionRole.Ranks => "RankCandidates",
        _ => "this node's params",
    };

    public void Dispose() { }
}

/// <summary>⭐ <c>CE-1325</c> — <c>details.utility.decision</c>, rank 19 (a tab beside Node, never the default).</summary>
public static class UtilityDecisionDetailsViewDescriptor
{
    public const string ViewId = "details.utility.decision";
    public const int Rank = 19;

    public static DetailsViewDescriptor For()
        => new(ViewId, "Decision", Rank, context => UtilityDecisionDetailsView.BoundBy(context).Count > 0, () => new UtilityDecisionDetailsView());
}
