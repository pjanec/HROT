using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Actions;
using Hrot.Common.Constants;
using Hrot.Common.Editing;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ CE-2423 (A6b, 📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the RULE target's actions are DECLARED ONCE (<see cref="RuleMenuActions"/>) and bound once
/// (<see cref="RuleActionBinding"/>): the Rules window's buttons and the rule track's timeline menu list the SAME words and the same validity.
/// <para>Capture: <c>MENU_GOLDEN_CAPTURE=1 dotnet test … --filter TheRuleActionsAreDeclaredOnceTests</c> rewrites <c>Goldens/rule.golden.txt</c>.</para>
/// </summary>
public sealed class TheRuleActionsAreDeclaredOnceTests
{
    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Goldens", "rule.golden.txt");

    private static readonly EditingMode[] Modes = { EditingMode.Edit, EditingMode.Preview, EditingMode.Live, EditingMode.Review };

    private static string Render()
    {
        var sb = new StringBuilder();
        foreach (var role in new[] { ObjectiveRole.Trigger, ObjectiveRole.Objective, ObjectiveRole.Measure })
            foreach (var mode in Modes)
            {
                sb.Append(role).Append(' ').Append(mode).Append(':').Append('\n');
                foreach (var row in MenuComposer.Compose(ActionContext.ForRule(mode, role)))
                    sb.Append("  ").Append(row.IsSeparator == true ? "---" : $"{row.Id} {row.Label}").Append('\n');
            }
        return sb.ToString();
    }

    /// <summary>The composed rows per role × mode (declarations only). 🔴 Red-proof: <i>Re-arm</i> declared for every role ⇒ the Objective / Measure rows move.</summary>
    [Fact]
    public void A6b_TheRuleRowsPerRoleAndMode_AreTheGolden()
    {
        var actual = Render();
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("MENU_GOLDEN_CAPTURE") == "1") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, actual); }
        Assert.True(File.Exists(path), $"no golden at {path} — capture it with MENU_GOLDEN_CAPTURE=1");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    private static RuleRow Row(ObjectiveStatus s, bool authored = false, int subject = 7)
        => new(1, Entity.Null, 0, false, s, "t", "", "", 0, subject, null, Array.Empty<RuleSpan>(), Array.Empty<double>(), null, Array.Empty<RuleLink>(), Array.Empty<RuleLink>(), authored);

    private static readonly ObjectiveStatus Armed = new() { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = TriggerState.Armed };

    /// <summary>⭐ THE SAME WORDS: the rule track's timeline menu lists exactly the binding's rows, in order, with the binding's reason — for every role, mode and block.
    /// 🔴 Red-proof: the track naming one action itself ("Fire!") ⇒ the labels differ from the declared ones.</summary>
    [Fact]
    public void A6b_TheTrackMenu_ListsTheDeclaredWords_WithTheBindingsReason()
    {
        foreach (var (row, blocked) in new[] { (Row(Armed), (string?)null), (Row(Armed), "a recording is read-only"), (Row(Armed, authored: true), null), (Row(new ObjectiveStatus { Role = ObjectiveRole.Measure }), null) })
        {
            var hooks = new RuleTrackHooks(_ => { }, (_, _) => { }, _ => { }, blocked);
            var menu = new RuleTrack(row, 1, false, hooks, 0).MenuItems(null).ToList();
            var bound = RuleActionBinding.Rows(row, RuleActionBinding.ModeOf(row, blocked), _ => { }, () => { }, blocked);
            Assert.Equal(bound.Select(b => b.Shown), menu.Select(m => m.Label));
            Assert.Equal(bound.Select(b => b.Why == null), menu.Select(m => m.Enabled));
        }
        // …and every word is one the catalogue declares for the Rule target
        var declared = ActionCatalogue.Shared.All.Where(d => (d.Targets & ActionTarget.Rule) != 0).Select(d => d.Label).ToHashSet();
        var shown = new RuleTrack(Row(Armed), 1, false, new RuleTrackHooks(_ => { }, (_, _) => { }, _ => { }, null), 0).MenuItems(null).Select(m => m.Label.Split(" — ")[0]);
        Assert.All(shown, w => Assert.Contains(w, declared));
    }

    /// <summary>⭐ An authored (not running) rule greys EVERY control with <see cref="RulesModel.NotRunning"/>; a blocked host (a recording) with its own reason; Show on map keeps its own validity.</summary>
    [Fact]
    public void A6b_AnAuthoredRule_GreysEveryControl_AndABlockedHostGreysThemWithItsReason()
    {
        var authored = RuleActionBinding.Rows(Row(Armed, authored: true, subject: 0), EditingMode.Edit, _ => { }, () => { }, null);
        Assert.Equal(new[] { "Edit", "Disable", "Show on map", "Fire now", "Disarm", "Re-arm", "Reset", "Delete" }, authored.Select(r => r.Label));
        Assert.All(authored.Where(r => RuleActionBinding.RequestOf(r.Id) != RuleRequest.NoRequest), r => Assert.Equal(RulesModel.NotRunning, r.Why));
        Assert.Equal(RuleActionBinding.NoSubject, authored.Single(r => r.Label == "Show on map").Why);
        Assert.Equal(RuleActionBinding.NoEditor, authored.Single(r => r.Label == "Edit").Why);   // no editor surface given ⇒ greyed, never dropped
        Assert.NotNull(authored.Single(r => r.Label == "Disable").Why);
        Assert.NotNull(authored.Single(r => r.Label == "Delete").Why);

        var rec = RuleActionBinding.Rows(Row(Armed), RuleActionBinding.ModeOf(Row(Armed), "no"), _ => { }, () => { }, "no");
        Assert.Equal(EditingMode.Review, RuleActionBinding.ModeOf(Row(Armed), "no"));
        Assert.All(rec.Where(r => RuleActionBinding.RequestOf(r.Id) != RuleRequest.NoRequest), r => Assert.Equal("no", r.Why));
    }

    /// <summary>⭐ A live row's controls are asked through the binding's <c>ask</c> with the declared request; a surface that has the editor / history makes Edit / Show history live.</summary>
    [Fact]
    public void A6b_TheControls_AskTheirRequest_AndTheSurfaceBindsEditAndHistory()
    {
        var asked = new System.Collections.Generic.List<RuleRequest>();
        int shown = 0, edited = 0, history = 0;
        var live = RuleActionBinding.Rows(Row(Armed), EditingMode.Live, asked.Add, () => shown++, null);
        Assert.Equal(new[] { "Fire now", "Disarm", "Re-arm", "Reset", "Show on map" }, live.Select(r => r.Label));
        foreach (var r in live) r.Run();
        Assert.Equal(new[] { RuleRequest.Fire, RuleRequest.Disarm, RuleRequest.Rearm, RuleRequest.Reset }, asked);
        Assert.Equal(1, shown);

        var surface = new RuleActionBinding.Surface(() => edited++, () => history++);
        var edit = RuleActionBinding.Rows(Row(Armed, authored: true), EditingMode.Edit, _ => { }, () => { }, null, surface);
        Assert.Null(edit.Single(r => r.Label == "Edit").Why);
        edit.Single(r => r.Label == "Edit").Run();
        var review = RuleActionBinding.Rows(Row(Armed), EditingMode.Review, _ => { }, () => { }, "ro", surface);
        Assert.Null(review.Single(r => r.Label == "Show history").Why);
        review.Single(r => r.Label == "Show history").Run();
        Assert.Equal((1, 1), (edited, history));
    }

    /// <summary>The ids are unique and every Rule declaration carries the Instructor level for a control, so an observer seat sees them greyed.</summary>
    [Fact]
    public void A6b_TheRuleControls_AreTheInstructors()
    {
        var ctx = ActionContext.ForRule(EditingMode.Live, ObjectiveRole.Trigger) with { Level = EditingLevel.Observer };
        var rows = MenuComposer.Compose(ctx);
        Assert.All(rows.Where(r => RuleActionBinding.RequestOf(r.Id) != RuleRequest.NoRequest), r => Assert.False(r.Enabled ?? true));
        Assert.Equal(ActionCatalogue.Shared.All.Count, ActionCatalogue.Shared.All.Select(d => (d.Id, d.Targets, d.Label)).Distinct().Count());
    }
}
