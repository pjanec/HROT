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
    /// <summary>⭐ CE-4322 — the one-shot step that ends the exercise when the scenario has no <c>instructor-end</c> rule (the Game master's one path, <see cref="GmOneShot"/>).</summary>
    public static Fdp.Toolkit.ScenarioRules.Actions.ActionStep EndStep()
        => new() { Act = "EndExercise", Args = new System.Text.Json.Nodes.JsonObject { ["reason"] = EndedByInstructor } };

    public const string Key = "instructor-end";
    public const string NoTrigger = "this scenario has no instructor-end trigger";
    public const string Ended = "the exercise has already ended";

    // ⭐ U-5 (CE-4335, AQ108 §0) — why End is greyed OUTSIDE a Live run: ONE rule for every control that draws it (the Rules toolbar, the Mission summary), by the host's transport mode
    public const string NoExercise = "no exercise is loaded";
    public const string NotRunning = "no exercise is running - End applies to a Live run";
    public const string PreviewIsNotAnExercise = "a preview is not an exercise - stop the preview instead";
    public const string NotStarted = "the exercise has not started - press play first";
    public const string ARecording = "a recording is shown - End applies to the live run";

    /// <summary>⭐ U-5 (CE-4335) — why <i>End exercise</i> cannot be used in this transport state, or null in a running Live exercise (a Live exercise that has ENDED keeps <see cref="Ended"/>, which the offer itself says).
    /// Edit, Preview, the Live briefing (paused at 0:00, <see cref="Hrot.UI.Common.RunMode.RunModeChoice.IsBriefing"/> — the same fact ■ and ⏪ read), a review (DAR) and a replay (AAR) are refused with their reason; constant strings, 0 B per read.</summary>
    public static string? RunRefusal(Hrot.UI.Common.Facades.TransportMode mode, bool paused, double totalTime) => mode switch
    {
        Hrot.UI.Common.Facades.TransportMode.Idle => NoExercise,
        Hrot.UI.Common.Facades.TransportMode.Edit => NotRunning,
        Hrot.UI.Common.Facades.TransportMode.Preview => PreviewIsNotAnExercise,
        Hrot.UI.Common.Facades.TransportMode.Live => Hrot.UI.Common.RunMode.RunModeChoice.IsBriefing(mode, paused, totalTime) ? NotStarted : null,
        _ => ARecording,   // Review (DAR) and Replay (AAR)
    };

    /// <summary>The same, read off the host's transport (null transport = a host with none: no refusal of its own).</summary>
    public static string? RunRefusal(Hrot.UI.Common.Facades.ITimeTransportFacade? transport)
        => transport == null ? null : RunRefusal(transport.Mode, transport.IsPaused, transport.TotalTime);
    /// <summary>⭐ CE-4322 (L-3, Scenario Lifecycle LC-17) — the result an instructor's End writes when the scenario has no <c>instructor-end</c> rule.</summary>
    public const string EndedByInstructor = "Ended by the instructor";

    /// <summary>What the button does now: the trigger to fire (null = none), and why it cannot be (null = it can).</summary>
    public readonly record struct Offer(Entity Trigger, string? DisabledReason, string Title)
    {
        public bool Available => DisabledReason == null;
        /// <summary>⭐ CE-4322 — no <c>instructor-end</c> rule: the End writes <see cref="EndedByInstructor"/> through a one-shot <c>EndExercise</c> step instead.</summary>
        public bool WithoutRule => Trigger.IsNull;
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
    /// <param name="withoutRule">⭐ CE-4322 (L-3) — why this station cannot end a scenario that has NO <c>instructor-end</c> rule (no way to create the one-shot,
    /// a seat that may not edit rules), or null when it can; omitted = it cannot (the old behaviour: disabled, <see cref="NoTrigger"/>).</param>
    public static Offer Of(EntityRepository? world, string? blocked, string? withoutRule = NoTrigger)
    {
        if (world == null) return new Offer(Entity.Null, NoTrigger, "End");
        var e = Find(world);
        if (e.IsNull)
        {
            // ⭐ CE-4322 (LC-17) — End ALWAYS works for an Instructor / Author: with no rule of its own, the exercise ends with "Ended by the instructor"
            if (withoutRule != null) return new Offer(e, withoutRule, "End");
            if (blocked != null) return new Offer(e, blocked, "End");
            if (ExerciseResult.PhaseOf(world) != ExercisePhase.Running) return new Offer(e, Ended, "End");
            return new Offer(e, null, "End");
        }
        if (blocked != null) return new Offer(e, blocked, "End");
        if (ExerciseResult.PhaseOf(world) != ExercisePhase.Running) return new Offer(e, Ended, "End");
        if (!world.IsComponentTypeRegistered<ObjectiveStatus>() || !world.HasComponent<ObjectiveStatus>(e)) return new Offer(e, RulesModel.NotRunning, "End");
        return new Offer(e, RuleCommands.DisabledReason(world.GetComponentRO<ObjectiveStatus>(e), RuleRequest.Fire), "End");
    }
}
