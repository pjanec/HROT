using System;
using System.Collections.Generic;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Actions;
using Hrot.Common.Constants;
using Hrot.Common.Editing;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ CE-2423 (A6b, 📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the RULE target's actions as a surface offers them: the catalogue's rows for a rule row / track
/// (<see cref="RuleMenuActions"/> — the declaration and the words), each bound here to what runs and why it is greyed. The Rules window's buttons (and its probe) and
/// the rule track's timeline menu both take their rows from <see cref="Rows"/> — one declaration, one validity rule (<see cref="RuleCommands.DisabledReason"/>).
/// ⭐ Built when a row is selected or a menu opens, never per frame.
/// </summary>
public static class RuleActionBinding
{
    /// <summary>What one surface can do with a rule beyond asking it: open its editor, open its history. A null member = the host has none (the row is greyed, never dropped).</summary>
    public readonly record struct Surface(Action? Edit = null, Action? ShowHistory = null);

    /// <summary>A composed, bound row: the declared id and words, what runs, why greyed (null = it can run).</summary>
    public readonly record struct Row(int Id, string Label, Action Run, string? Why)
    {
        /// <summary>The words with a greyed row's reason, as the timeline menu shows it (<i>"label — reason"</i>).</summary>
        public string Shown => Why is null ? Label : $"{Label} — {Why}";
    }

    public const string NoSubject = "this rule has no subject";
    public const string NoEditor = "this host has no rule editor";
    public const string NoHistory = "this host has no rule details pane to show a history in";
    public const string CannotDisable = "this window cannot switch a rule off: it has no disable command for a rule";
    public const string CannotDelete = "this window cannot delete a rule: it has no remove command for a rule";

    /// <summary>The mode a row is offered in: an authored one (no run yet) is Edit; where asking is blocked (a recording) it is Review; else Live.</summary>
    public static EditingMode ModeOf(RuleRow row, string? blocked) => row.Authored ? EditingMode.Edit : blocked != null ? EditingMode.Review : EditingMode.Live;

    /// <summary>The request a control row asks (<see cref="RuleRequest.NoRequest"/> = the row is not a control).</summary>
    public static RuleRequest RequestOf(int id) => id switch
    {
        GlobalActionIds.RuleFire => RuleRequest.Fire,
        GlobalActionIds.RuleDisarm => RuleRequest.Disarm,
        GlobalActionIds.RuleRearm => RuleRequest.Rearm,
        GlobalActionIds.RuleReset => RuleRequest.Reset,
        GlobalActionIds.RuleActivate => RuleRequest.Activate,
        GlobalActionIds.RuleDeactivate => RuleRequest.Deactivate,
        _ => RuleRequest.NoRequest,
    };

    /// <summary>⭐ The ONE validity of a control on a row: blocked where the host cannot ask (a recording), <see cref="RulesModel.NotRunning"/> for an authored row, else the rule's own <see cref="RuleCommands.DisabledReason"/>.</summary>
    public static string? ControlReason(RuleRow row, RuleRequest request, string? blocked)
        => blocked ?? (row.Authored ? RulesModel.NotRunning : RuleCommands.DisabledReason(row.Status, request));

    /// <summary>
    /// The rows the catalogue offers <paramref name="row"/> in <paramref name="mode"/>, each bound: a control through <paramref name="ask"/>, Show on map through
    /// <paramref name="showOnMap"/>, Edit / Show history through <paramref name="surface"/>; Disable and Delete are declared and greyed (no host has the command).
    /// </summary>
    public static List<Row> Rows(RuleRow row, EditingMode mode, Action<RuleRequest> ask, Action showOnMap, string? blocked, Surface surface = default, ActionCatalogue? catalogue = null)
    {
        var rows = new List<Row>();
        foreach (var dto in MenuComposer.Compose(ActionContext.ForRule(mode, row.Status.Role), catalogue))
        {
            if (dto.IsSeparator == true || dto.Id == 0) continue;
            string label = dto.Label ?? "";
            var request = RequestOf(dto.Id);
            if (request != RuleRequest.NoRequest) { rows.Add(new Row(dto.Id, label, () => ask(request), dto.Enabled == false ? dto.Tooltip : ControlReason(row, request, blocked))); continue; }
            rows.Add(dto.Id switch
            {
                GlobalActionIds.RuleShowOnMap => new Row(dto.Id, label, showOnMap, row.SubjectNetworkId != 0 ? null : NoSubject),
                GlobalActionIds.RuleEdit => new Row(dto.Id, label, surface.Edit ?? Nothing, surface.Edit != null ? null : NoEditor),
                GlobalActionIds.RuleShowHistory => new Row(dto.Id, label, surface.ShowHistory ?? Nothing, surface.ShowHistory != null ? null : NoHistory),
                GlobalActionIds.RuleDisable => new Row(dto.Id, label, Nothing, CannotDisable),
                GlobalActionIds.RuleDelete => new Row(dto.Id, label, Nothing, CannotDelete),
                _ => new Row(dto.Id, label, Nothing, "not available here"),
            });
        }
        return rows;
    }

    private static void Nothing() { }
}
