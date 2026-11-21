using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>⭐ <c>CE-4318</c> — what kind of problem a <see cref="ScenarioProblem"/> is (📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md §2.7).</summary>
    public enum ScenarioProblemKind : byte
    {
        /// <summary>A rule / objective's subject or parent names an entity that is gone.</summary>
        RuleSubjectGone = 1,
        /// <summary>A rule it depends on / acts on is gone.</summary>
        RuleLinkGone = 2,
        /// <summary>A group's roster lists a member that is gone.</summary>
        RosterMemberGone = 3,
        /// <summary>A group's member is on another side than the group.</summary>
        MixedSides = 4,
        /// <summary>An objective shown to one side only — a side with no units.</summary>
        ObjectiveSideEmpty = 5,
    }

    /// <summary>⭐ <c>CE-4318</c> — one problem: its kind, the entity a click selects, and one number the text names (a network id, a side).</summary>
    public readonly record struct ScenarioProblem(ScenarioProblemKind Kind, Entity Culprit, int Other);

    /// <summary>
    /// ⭐⭐ <c>CE-4318</c> — scenario lifecycle LC-13 (📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md §2.7, R-4166): THE scenario problems, read from
    /// the world — a rule whose subject / linked rule is gone, a group whose roster names a dead entity or mixes sides, an objective shown only to a
    /// side with no units. ⛔ Never blocks Preview / Live: it only lists. The Problems window and the toolbar count (ui) read THIS. ⚠ Not a per-frame call: <see cref="Collect"/> walks the world (its queries allocate) — call it on a timer or after an edit; it fills
    /// the caller's reused list. <see cref="Text"/> allocates (a drawn row).
    /// </summary>
    public static class ScenarioProblems
    {
        /// <summary>Fills <paramref name="into"/> (cleared first) with every problem of <paramref name="world"/>; returns how many.</summary>
        public static int Collect(EntityRepository world, List<ScenarioProblem> into)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));
            if (into is null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            Rules(world, into);
            Groups(world, into);
            Objectives(world, into);
            return into.Count;
        }

        /// <summary>The one line a problem says (allocates — call it for a row that is drawn).</summary>
        public static string Text(EntityRepository world, in ScenarioProblem p)
        {
            string who = NameOf(world, p.Culprit);
            return p.Kind switch
            {
                ScenarioProblemKind.RuleSubjectGone => $"{who}: its subject (#{p.Other}) is gone",
                ScenarioProblemKind.RuleLinkGone => $"{who}: a rule it is linked to (#{p.Other}) is gone",
                ScenarioProblemKind.RosterMemberGone => $"{who}: a member of the group is gone",
                ScenarioProblemKind.MixedSides => $"{who} is on another side than its group ({(ForceId)p.Other})",
                ScenarioProblemKind.ObjectiveSideEmpty => $"{who} is shown only to {(ForceId)p.Other}, which has no units",
                _ => who,
            };
        }

        private static string NameOf(EntityRepository world, Entity e)
        {
            if (world.IsComponentTypeRegistered<ObjectiveStatus>() && world.IsAlive(e) && world.HasComponent<ObjectiveStatus>(e)
                && world.GetComponentRO<ObjectiveStatus>(e).Title.ToString() is { Length: > 0 } title)
                return title;
            if (world.IsComponentTypeRegistered<EntityInfo>() && world.IsAlive(e) && world.HasComponent<EntityInfo>(e)
                && world.GetComponentRO<EntityInfo>(e).Name.ToString() is { Length: > 0 } name)
                return name;
            return $"entity {e.Index}";
        }

        // ── the rules: their subject / parent, and the rules they depend on / act on ────────────────────────────────────────────────────

        private static void Rules(EntityRepository world, List<ScenarioProblem> into)
        {
            if (world.IsComponentTypeRegistered<ObjectiveLinks>())
                foreach (var e in world.Query().With<ObjectiveLinks>().Build())
                {
                    ref readonly var l = ref world.GetComponentRO<ObjectiveLinks>(e);
                    if (Gone(world, l.Subject.NetworkId)) into.Add(new(ScenarioProblemKind.RuleSubjectGone, e, l.Subject.NetworkId));
                    if (Gone(world, l.Parent.NetworkId)) into.Add(new(ScenarioProblemKind.RuleSubjectGone, e, l.Parent.NetworkId));
                }
            if (world.IsComponentTypeRegistered<ObjectiveStatus>())
                foreach (var e in world.Query().With<ObjectiveStatus>().Build())
                {
                    ref readonly var s = ref world.GetComponentRO<ObjectiveStatus>(e);
                    for (int i = 0; i < s.DependsOnCount && i < 8; i++)
                        if (Gone(world, s.DependsOn[i])) into.Add(new(ScenarioProblemKind.RuleLinkGone, e, s.DependsOn[i]));
                    for (int i = 0; i < s.ActsOnCount && i < 8; i++)
                        if (Gone(world, s.ActsOn[i])) into.Add(new(ScenarioProblemKind.RuleLinkGone, e, s.ActsOn[i]));
                }
        }

        /// <summary>A network id that is set (≠ 0) and names no live entity here.</summary>
        private static bool Gone(EntityRepository world, int networkId)
            => networkId != 0 && NetworkIdResolver.FindEntityByNetworkId(world, networkId) is var e && (e.IsNull || !world.IsAlive(e));

        // ── the ORBAT groups: a dead member, a member on another side ───────────────────────────────────────────────────────────────────

        private static void Groups(EntityRepository world, List<ScenarioProblem> into)
        {
            if (!world.IsComponentTypeRegistered<UnitRoster>()) return;
            bool sides = world.IsComponentTypeRegistered<EntityInfo>();
            foreach (var g in world.Query().With<UnitRoster>().Build())
            {
                ref readonly var roster = ref world.GetComponentRO<UnitRoster>(g);
                bool groupHasSide = sides && world.HasComponent<EntityInfo>(g);
                var side = groupHasSide ? world.GetComponentRO<EntityInfo>(g).ForceId : default;
                bool reportedDead = false;
                for (int i = 0; i < roster.Count && i < UnitRoster.Capacity; i++)
                {
                    var m = roster.SubordinateEntities[i];
                    if (m.IsNull || !world.IsAlive(m))
                    {
                        if (!reportedDead) { into.Add(new(ScenarioProblemKind.RosterMemberGone, g, 0)); reportedDead = true; }
                        continue;
                    }
                    if (groupHasSide && world.HasComponent<EntityInfo>(m) && world.GetComponentRO<EntityInfo>(m).ForceId is var ms && ms != side)
                        into.Add(new(ScenarioProblemKind.MixedSides, m, (int)side));
                }
            }
        }

        // ── the objectives: one shown only to a side with no units ──────────────────────────────────────────────────────────────────────

        private static void Objectives(EntityRepository world, List<ScenarioProblem> into)
        {
            if (!world.IsComponentTypeRegistered<ObjectiveStatus>() || !world.IsComponentTypeRegistered<EntityInfo>()) return;
            int sidesWithUnits = 0;   // bit per ForceId value
            foreach (var e in world.Query().With<EntityInfo>().Build())
            {
                if (world.HasComponent<ObjectiveStatus>(e)) continue;   // a rule's own info is not a unit
                int f = (int)world.GetComponentRO<EntityInfo>(e).ForceId;
                if (f < 8) sidesWithUnits |= 1 << f;
            }
            foreach (var e in world.Query().With<ObjectiveStatus>().Build())
            {
                ref readonly var s = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (s.Role != ObjectiveRole.Objective || s.VisibleSides is 0 or 0xFF) continue;   // everybody's
                for (int f = 0; f < 8; f++)
                    if ((s.VisibleSides & (1 << f)) != 0 && (sidesWithUnits & (1 << f)) == 0)
                        into.Add(new(ScenarioProblemKind.ObjectiveSideEmpty, e, f));
            }
        }
    }
}
