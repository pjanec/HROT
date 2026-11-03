using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.UI.Common.Rules;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ CE-4216 (rules view R2, RV-7) — the <b>Objectives</b> tab of the Details panel: <i>"objectives about this unit"</i>, one line per rule whose subject the unit is (title · role icon · state words in the state's colour). A click opens that rule in the rules view
/// (<paramref name="openRule"/>: the host selects the row there). The rows are <see cref="ObjectivesAboutModel"/>'s — the rules view's own, never a second read.
/// </summary>
public sealed class EntityObjectivesView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private readonly Action<int>? _openRule;

    public EntityObjectivesView(Func<EntityRepository?> world, Action<int>? openRule)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _openRule = openRule;
    }

    /// <summary>The host's "open this rule in the rules view" (null = the tab lists without links) — a rail reads it to prove the host passed it.</summary>
    public Action<int>? OpenRule => _openRule;

    /// <summary>What the tab lists for <paramref name="unit"/> now.</summary>
    public IReadOnlyList<RuleRow> RowsFor(Entity unit)
        => _world() is { } w ? ObjectivesAboutModel.Of(w, unit, Fdp.Toolkit.Squad.Command.CommandPictureOps.Now(w)) : Array.Empty<RuleRow>();

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 }) return;
        var rows = RowsFor(context.Entities[0]);
        if (rows.Count == 0) { ImGui.TextDisabled("No objectives about this unit."); return; }
        foreach (var r in rows)
        {
            ImGui.PushID(idScope + ":" + r.NetworkId);
            ImGui.TextDisabled(r.Icon); ImGui.SameLine();
            if (ImGui.Selectable(r.Title) && _openRule != null) _openRule(r.NetworkId);
            ImGui.SameLine();
            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(r.StateColour), r.StateWords);
            ImGui.PopID();
        }
    }

    public void Dispose() { }
}

/// <summary>⭐ CE-4216 (RV-7) — the Objectives tab's descriptor: offered for ONE selected unit that some rule is about, and only then (<i>empty ⇒ not drawn</i>).</summary>
public static class EntityObjectivesViewDescriptor
{
    public const string ViewId = "details.objectives";

    /// <summary>Below the Summary (35), the Brain tab (34) and Components (30)... never the default view: the default is the highest rank.</summary>
    public const int Rank = 32;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Action<int>? openRule)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new EntityObjectivesView(world, openRule);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Objectives",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && instance.RowsFor(ctx.Entities[0]).Count > 0,
            Create:    () => instance);
    }
}
