using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ CE-4216 (rules view R2, RV-7; docs/DESIGN_Rules_View_UI.md) — <i>"objectives about this unit"</i> (SR-56) as DATA: every rule row whose <c>ObjectiveLinks.Subject</c> is the unit, with its state words and colour — the SAME rows the rules view draws
/// (<see cref="RulesModel.Build"/> over <see cref="RulesReader"/>), so the inspector section and the window can never disagree. Empty ⇒ the section is not offered.
/// </summary>
public static class ObjectivesAboutModel
{
    public static IReadOnlyList<RuleRow> Of(EntityRepository world, Entity unit, double now)
    {
        if (unit.IsNull || !world.IsAlive(unit)) return Array.Empty<RuleRow>();
        int net = NetworkIdResolver.RuntimeNetworkIdOf(world, unit);
        if (net == 0) return Array.Empty<RuleRow>();
        return RulesModel.Build(world, new RulesOptions(RulesGroupBy.Subject), null, now).Rows.Where(r => r.SubjectNetworkId == net).ToList();
    }
}
