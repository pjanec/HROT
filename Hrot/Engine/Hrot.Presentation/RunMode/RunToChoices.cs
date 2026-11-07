using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;
using Hrot.UI.Common.Rules;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ W-4 (Scenario lifecycle LC-10, 📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md §2.8; mockup <c>run-to-visual.svg</c>) — what the mode menu's
/// <i>Run to …</i> offers: a time the author types, <i>when Phase N begins</i> per phase and <i>when a rule fires</i> per trigger. PURE: the presets
/// are read once when the popup opens (never per frame), each a compiled condition of the ONE condition language — a phase's is
/// <see cref="PlanConditionCache.PhaseBegins"/>, a rule's the same shape (its <c>FireCount &gt; 0</c>, the condition editor's <i>has fired</i>).
/// </summary>
public static class RunToChoices
{
    /// <summary>One preset: its words, its phase (0 = a rule that is not a phase) and the condition that ends the run.</summary>
    public sealed record Choice(string Label, int Phase, Func<EntityRepository, Entity, bool> Until);

    /// <summary>
    /// Reads a time as the clock shows it: <c>m:ss</c> (<i>10:00</i> = ten minutes), <c>h:mm:ss</c>, or a bare number of MINUTES (<i>10</i>).
    /// False for anything else, a negative time or seconds past 59.
    /// </summary>
    public static bool TryParseTime(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length == 1)
        {
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) || minutes < 0) return false;
            seconds = minutes * 60; return true;
        }
        if (parts.Length > 3) return false;
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int v)) return false;
            if (i > 0 && v > 59) return false;
            total = total * 60 + v;
        }
        seconds = total; return true;
    }

    /// <summary>The phases (ordered by number) and the rules that can fire (triggers, by title) of <paramref name="world"/>, into the two lists (cleared).</summary>
    public static void Collect(EntityRepository world, List<Choice> phases, List<Choice> rules)
    {
        phases.Clear(); rules.Clear();
        if (!world.IsComponentTypeRegistered<ObjectiveStatus>() || !world.IsComponentTypeRegistered<NetworkIdentity>()) return;
        var scope = AuthoredRules.ScopeOf(world);
        foreach (var e in world.Query().With<ObjectiveStatus>().With<NetworkIdentity>().Build())
        {
            ref readonly var s = ref world.GetComponentRO<ObjectiveStatus>(e);
            if (s.Kind.ToString() != RuleAdd.TriggerKind) continue;   // only a trigger fires
            var link = RuleGallery.LinkTo(world.GetComponentRO<NetworkIdentity>(e).Value, scope);
            if (link.Ref.IsNone && link.Spec.IsNone) continue;
            var until = ConditionCache.Compiler.CompileLevelPredicate(PlanConditionCache.PhaseBegins(link));   // FireCount > 0 — a phase's begin IS its trigger firing (compiled on the popup's open only)
            string title = s.Title.ToString();
            if (s.Phase > 0) phases.Add(new Choice(string.IsNullOrEmpty(title) ? $"Phase {s.Phase}" : $"Phase {s.Phase} · {title}", s.Phase, until));
            else rules.Add(new Choice(string.IsNullOrEmpty(title) ? "(untitled rule)" : title, 0, until));
        }
        phases.Sort((a, b) => a.Phase.CompareTo(b.Phase));
        rules.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
    }
}
