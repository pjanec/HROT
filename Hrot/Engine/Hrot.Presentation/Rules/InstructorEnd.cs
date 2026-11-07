using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐⭐ CE-4240 (SR-116; docs/DESIGN_Rules_View_UI.md §7.10) — the instructor's <b>End</b>: the scenario's trigger carrying the <see cref="RefKey"/> <c>instructor-end</c> (backend's content, found by that KEY, never by title),
/// asked <see cref="RuleRequest.Fire"/> through the ONE write (<see cref="RuleCommands.Control"/>), so the round ends by the very path an automatic end takes. Pure — both windows (Rules toolbar, Mission summary) read the same
/// <see cref="Offer"/>; the button is never hidden: with no such trigger it is disabled and says so.
/// </summary>
public static class InstructorEnd
{
    public const string Key = "instructor-end";
    public const string NoTrigger = "this scenario has no instructor-end trigger";
    public const string Ended = "the exercise has already ended";

    /// <summary>What the button does now: the trigger to fire (null = none), and why it cannot be (null = it can).</summary>
    public readonly record struct Offer(Entity Trigger, string? DisabledReason, string Title)
    {
        public bool Available => DisabledReason == null;
    }

    /// <summary>The entity carrying the <c>instructor-end</c> key (a rule row, running or only authored), or <see cref="Entity.Null"/>.</summary>
    public static Entity Find(EntityRepository world)
    {
        if (!world.IsComponentTypeRegistered<RefKey>()) return Entity.Null;
        foreach (var e in world.Query().With<RefKey>().Build())
            if (world.IsAlive(e) && world.GetComponentRO<RefKey>(e).Value.ToString() == Key) return e;
        return Entity.Null;
    }

    /// <param name="blocked">Why this host cannot ask a rule now (a recording), or null.</param>
    public static Offer Of(EntityRepository? world, string? blocked)
    {
        if (world == null) return new Offer(Entity.Null, NoTrigger, "End");
        var e = Find(world);
        if (e.IsNull) return new Offer(e, NoTrigger, "End");
        if (blocked != null) return new Offer(e, blocked, "End");
        if (ExerciseResult.PhaseOf(world) != ExercisePhase.Running) return new Offer(e, Ended, "End");
        if (!world.IsComponentTypeRegistered<ObjectiveStatus>() || !world.HasComponent<ObjectiveStatus>(e)) return new Offer(e, RulesModel.NotRunning, "End");
        return new Offer(e, RuleCommands.DisabledReason(world.GetComponentRO<ObjectiveStatus>(e), RuleRequest.Fire), "End");
    }
}
