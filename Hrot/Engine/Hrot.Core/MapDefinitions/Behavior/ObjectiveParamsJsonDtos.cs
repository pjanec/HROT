using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.Map.Definitions.Behavior
{
    // ⭐ rules S1 (📄 docs/DESIGN_Scenario_Rules.md §10.2) — the authored contracts of the objective KINDS. Every one begins with the same
    //   nested "common" block (title, subject, parent, required, scored, weight, points, deadlineSeconds, startActive, visibleSides).
    //   Zero is the default everywhere (0 = the kind's own default), like every params DTO here.

    /// <summary><c>ReachArea</c>: at least <see cref="MinMen"/> of the subject's men inside <see cref="Area"/>.</summary>
    [ParamsDto]
    public struct ReachAreaParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ReachArea;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("area")] public AreaLink Area;
        /// <summary>0 = 1.</summary>
        [JsonPropertyName("minMen")] public int MinMen;
    }

    /// <summary><c>HoldArea</c>: held (≥ MinMen of the subject's men inside, ≤ MaxEnemy enemies) for HoldSeconds; <see cref="Capture"/> never ends.</summary>
    [ParamsDto]
    public struct HoldAreaParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.HoldArea;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("area")] public AreaLink Area;
        /// <summary>0 = 4.</summary>
        [JsonPropertyName("minMen")] public int MinMen;
        [JsonPropertyName("maxEnemy")] public int MaxEnemy;
        /// <summary>0 = 600.</summary>
        [JsonPropertyName("holdSeconds")] public float HoldSeconds;
        /// <summary>Repeatable: never ends, the Owner flips.</summary>
        [JsonPropertyName("capture")] public bool Capture;
        /// <summary>⭐ S6 G-1 — with <see cref="Capture"/>: &gt; 0 = the point is taken BY SIDE — the side with more men inside (every man of the side, not the
        /// subject's roster) than the other, and at least <see cref="MinMen"/>, for this many seconds becomes the Owner; the moment the other side outnumbers
        /// the owner the point is Neutral. 0 = the S1 rule (instant, the subject's roster).</summary>
        [JsonPropertyName("captureSeconds")] public float CaptureSeconds;
    }

    /// <summary><c>ClearArea</c>: no more than MaxEnemy enemies inside for ForSeconds.</summary>
    [ParamsDto]
    public struct ClearAreaObjectiveParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ClearAreaObjective;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("area")] public AreaLink Area;
        /// <summary>Neutral (0) = any side hostile to the subject.</summary>
        [JsonPropertyName("enemySide"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId EnemySide;
        [JsonPropertyName("maxEnemy")] public int MaxEnemy;
        /// <summary>0 = 30.</summary>
        [JsonPropertyName("forSeconds")] public float ForSeconds;
    }

    /// <summary><c>DestroyTargets</c>: <see cref="Count"/> of <see cref="Targets"/> (at most 8) destroyed.</summary>
    [ParamsDto]
    public struct DestroyTargetsParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DestroyTargets;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("targets")] public EntityLink[]? Targets;
        /// <summary>0 = all of them.</summary>
        [JsonPropertyName("count")] public int Count;
    }

    /// <summary><c>Protect</c>: at least <see cref="MinSurvivors"/> of <see cref="Targets"/> (at most 8) stay alive; judged at the end.</summary>
    [ParamsDto]
    public struct ProtectParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Protect;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("targets")] public EntityLink[]? Targets;
        /// <summary>0 = all of them.</summary>
        [JsonPropertyName("minSurvivors")] public int MinSurvivors;
    }

    /// <summary><c>NoHarm</c>: no more than MaxCount distinct <see cref="Protected"/> victims harmed by <see cref="BySide"/>; judged at the end.</summary>
    [ParamsDto]
    public struct NoHarmParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.NoHarm;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>The affiliation that must not be harmed (default Neutral).</summary>
        [JsonPropertyName("protected"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId Protected;
        /// <summary>Harm by this side; Neutral (0) = the subject's side.</summary>
        [JsonPropertyName("bySide"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId BySide;
        /// <summary>Harm by anybody (BySide ignored).</summary>
        [JsonPropertyName("anySide")] public bool AnySide;
        [JsonPropertyName("maxCount")] public int MaxCount;
        /// <summary>⭐ CE-4155 (SR-82) — whose side is judged for a RETHROWN round's harm: <c>last</c> (default; <c>DamageAssessedEvent.Source</c>), <c>original</c>
        /// (the first thrower, <c>OriginalSource</c>, else the source), <c>both</c> (either side counts).</summary>
        [JsonPropertyName("attribute"), JsonConverter(typeof(JsonStringEnumConverter))] public HarmAttribute Attribute;
    }

    /// <summary>⭐ CE-4155 (SR-82) — which source of a harm a <c>NoHarm</c> judges. <c>Last</c> = 0 so an absent param keeps the old behaviour.</summary>
    public enum HarmAttribute : byte { Last = 0, Original = 1, Both = 2 }

    /// <summary>⭐ S6 G-2 — one death rule of a <see cref="CounterParamsJsonDto"/>: each man of <see cref="Side"/> newly OUT of the fight adds <see cref="Per"/> to the value.</summary>
    public struct CounterDeathsJsonDto
    {
        [JsonPropertyName("side"), JsonConverter(typeof(JsonStringEnumConverter))] public ForceId Side;
        /// <summary>Added per man down (tickets: -1).</summary>
        [JsonPropertyName("per")] public float Per;
    }

    /// <summary>
    /// ⭐ S6 G-2 (📄 docs/DESIGN_Scenario_Rules.md §00.11) — <c>Counter</c>, a MEASURE (role Measure): <c>Value</c> starts at <see cref="Start"/> (also its
    /// <c>Target</c>, so the score is value / start); <c>AddToCounter</c> moves it; <see cref="Deaths"/> (at most two sides) moves it by <c>per</c> for every man of the side
    /// newly out of the fight (counted once; a revived man refunds nothing). Clamped to [<see cref="Floor"/>, <see cref="Ceiling"/>].
    /// </summary>
    [ParamsDto]
    public struct CounterParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Counter;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        [JsonPropertyName("start")] public float Start;
        /// <summary>At most two entries (Friend, Hostile); a Neutral side is ignored.</summary>
        [JsonPropertyName("deaths")] public CounterDeathsJsonDto[]? Deaths;
        /// <summary>The value never goes below this; null = 0 (a lead counter sets a negative one).</summary>
        [JsonPropertyName("floor")] public float? Floor;
        /// <summary>The value never goes above this; null = no cap.</summary>
        [JsonPropertyName("ceiling")] public float? Ceiling;
    }

    /// <summary><c>WeightedMean</c>: a composite scoring its children (<c>ObjectiveLinks.Parent</c> = it) by weight.</summary>
    [ParamsDto]
    public struct WeightedMeanParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.WeightedMean;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>0 = 0.6.</summary>
        [JsonPropertyName("passScore")] public float PassScore;
    }

    /// <summary><c>AllMustPass</c>: a composite achieved when every child is.</summary>
    [ParamsDto]
    public struct AllMustPassParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.AllMustPass;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
    }
    /// <summary>
    /// ⭐ rules S2-c (📄 docs/DESIGN_Scenario_Rules.md §10.11, §00.9) — <c>Trigger</c>: when <see cref="When"/> RISES (false → true) wait out
    /// <see cref="HoldFor"/>, then <see cref="Delay"/>, then run the <see cref="Do"/> list in order; once, or <see cref="Repeat"/> (a new edge after
    /// <see cref="Cooldown"/>) up to <see cref="MaxFires"/>. <c>common.scored</c> defaults to FALSE for a trigger (T8); <c>common.startActive</c> false = Disarmed
    /// until a <c>SetObjectiveActive</c> arms it.
    /// <para>⭐ <c>when</c> and <c>do</c> are kept as the caller's JSON (in <c>ActionArgs</c>, items 0 and 1) and refused at ASSIGNMENT when they do not
    /// compile / validate (CE-4163).</para>
    /// </summary>
    [ParamsDto]
    public struct TriggerParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Trigger;
        [JsonPropertyName("common")] public ObjectiveCommon Common;
        /// <summary>The condition — a polymorphic search predicate (<c>{"$type":"OnEntity",...}</c>), evaluated as a LEVEL each tick.</summary>
        [JsonPropertyName("when")] public SearchPredicateDto? When;
        /// <summary>The condition must hold this long (sim s) before it counts; 0 = no debounce.</summary>
        [JsonPropertyName("holdFor")] public float HoldFor;
        /// <summary>Fire this long (sim s) after the edge (or the hold), even if the condition drops meanwhile; 0 = at once.</summary>
        [JsonPropertyName("delay")] public float Delay;
        /// <summary>A repeating trigger re-arms no sooner than this (sim s) after it fired.</summary>
        [JsonPropertyName("cooldown")] public float Cooldown;
        /// <summary>Re-arm after firing (needs the condition to go false again); default once.</summary>
        [JsonPropertyName("repeat")] public bool Repeat;
        /// <summary>A repeating trigger stops after this many fires; 0 = unlimited.</summary>
        [JsonPropertyName("maxFires")] public int MaxFires;
        /// <summary>CE-4166 — a condition already TRUE when the trigger arms counts as an edge (default: it does not).</summary>
        [JsonPropertyName("fireIfTrueWhenArmed")] public bool FireIfTrueWhenArmed;
        /// <summary>⭐ S6 G-4 — with <see cref="Repeat"/>: when the cooldown ends and the condition is STILL true, fire again (the periodic rule: "every 10 s while held");
        /// without it a repeat needs the condition to go false and rise again (T2). <see cref="MaxFires"/> still caps it.</summary>
        [JsonPropertyName("whileTrue")] public bool WhileTrue;
        /// <summary>The actions: <c>[{"act":"Message","args":{...}}, ...]</c>, run in order (SR-93).</summary>
        [JsonPropertyName("do")] public JsonNode? Do;
    }
}
