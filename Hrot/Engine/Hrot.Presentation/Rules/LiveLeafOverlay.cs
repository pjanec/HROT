using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>One leaf the owner sampled (<see cref="RuleLeaf"/>): its label, whether it holds now, and the number behind it.</summary>
public sealed record LiveLeaf(string Label, bool Holds, float Measured);

/// <summary>One row of the Live strip: a leaf of the form's <c>when</c> (<see cref="InForm"/>) with the owner's sample matched by label (<see cref="Sampled"/>), or a sampled leaf the form does not have (the owner still runs the applied one).</summary>
public sealed record LiveLeafRow(string Label, bool Sampled, bool Holds, float Measured, bool InForm);

/// <summary>
/// ⭐⭐ CE-4220 (rules view R5b, RE-3 live) — the trigger's <c>when</c> leaves with their LIVE values, PURE: <see cref="Read"/> takes the owner's <see cref="RuleLeaves"/> (431), <see cref="Match"/> lays them over the form's own leaves BY LABEL
/// (<see cref="RuleLeafPlan.LabelOf(SearchPredicateDto)"/> cut to a <c>FixedString32</c>, as the owner stamps it — from the same flattening, <see cref="RuleLeafPlan.LeavesOf"/>).
/// <para>⚠ <b>Deviation from "beside each leaf":</b> the <c>when</c> form is the polymorphic container, so a value cannot be drawn on the leaf's own row without a drawer for every predicate kind; the values come as a strip UNDER the When form.</para>
/// </summary>
public static class LiveLeafOverlay
{
    /// <summary>The leaves the owner published for <paramref name="rule"/>; empty when it publishes none (the feature <c>ui.rules-live-values</c> is off, or the owner has not sampled yet).</summary>
    public static IReadOnlyList<LiveLeaf> Read(EntityRepository world, Entity rule)
    {
        if (!world.IsAlive(rule) || !world.IsComponentTypeRegistered<RuleLeaves>() || !world.HasComponent<RuleLeaves>(rule)) return Array.Empty<LiveLeaf>();
        ref readonly var leaves = ref world.GetComponentRO<RuleLeaves>(rule);
        int n = Math.Min((int)leaves.Count, RuleLeafPlan.MaxLeaves);
        var list = new List<LiveLeaf>(n);
        for (int i = 0; i < n; i++)
        {
            var l = leaves.Leaves[i];
            list.Add(new LiveLeaf(l.Label.ToString(), l.Value != 0, l.Measured));
        }
        return list;
    }

    /// <summary>The strip's rows: every leaf of <paramref name="when"/> in order (its sample if one carries its label), then the sampled leaves nothing in the form matches.</summary>
    public static IReadOnlyList<LiveLeafRow> Match(IReadOnlyList<LiveLeaf> live, SearchPredicateDto? when)
    {
        var rows = new List<LiveLeafRow>();
        var free = live.ToList();
        foreach (var leaf in RuleLeafPlan.LeavesOf(when, out _))
        {
            string label = RuleLeafPlan.LabelOf(leaf);
            string stamped = new FixedString32(label).ToString();   // the owner's label is a FixedString32: a long one (an OnEntity's authored ref + its leaf) arrives cut — found live; leaves with one cut label pair in order
            int at = free.FindIndex(l => l.Label == stamped);
            if (at < 0) rows.Add(new LiveLeafRow(label, false, false, 0f, true));
            else { rows.Add(new LiveLeafRow(label, true, free[at].Holds, free[at].Measured, true)); free.RemoveAt(at); }
        }
        foreach (var l in free) rows.Add(new LiveLeafRow(l.Label, true, l.Holds, l.Measured, false));
        return rows;
    }
}
