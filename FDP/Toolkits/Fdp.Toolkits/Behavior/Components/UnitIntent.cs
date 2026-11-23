using Fdp.Core;
using Fdp.Core.Serialization.Converters;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>⭐ <c>CE-3042</c> — one saved slot: what runs, with which params, on whose authority (R-192: nothing else).</summary>
    public sealed record IntentSlot
    {
        /// <summary>The registered behaviour name.</summary>
        public string Name { get; init; } = "";
        /// <summary>The params JSON it was started with (R-191 — never bytes). ⭐ CE-4295: written to a file as the object it holds.</summary>
        [JsonText] public string Params { get; init; } = "{}";
        /// <summary>Who ordered it — the reload carries the same rank.</summary>
        public BehaviorOrigin Origin { get; init; } = BehaviorOrigin.Superior;
        /// <summary>⭐ <c>CE-3494</c> §10 P3 (R-3119) — a person's RELEASE: the slot ends what runs instead of starting something (live only; a
        /// pending Clear saves no task). Not written when false, so a file saved after loading is unchanged.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool Clear { get; init; }
        /// <summary>⭐ <c>CE-3494</c> §10 P4 — the station's request id (edit framework F5), so the brain's owner answers the station that asked.
        /// ⛔ Never saved (the save strips it); not written when empty.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public System.Guid RequestId { get; init; }
    }

    /// <summary>⭐ <c>CE-3042</c> — the saved ROE (R-200), only when an order changed it from the template default.</summary>
    public sealed record SavedRoe
    {
        public RoeFire Fire { get; init; }
        public RoeReactions Reactions { get; init; }
        public BehaviorOrigin SetBy { get; init; } = BehaviorOrigin.Superior;
        /// <summary>⭐ <c>CE-2095</c> — the ReturnFire window; <c>0</c> = unset (the default). JSON-carried, so the saved scenario
        /// and the replicated intent both take it with no wire change.</summary>
        public SimDuration ReturnFireWindowSeconds { get; init; }
        /// <summary>⭐ <c>CE-2239</c> part 1 — the force level; <c>ForceUnset</c> = the default. JSON-carried, as the window.</summary>
        public RoeForceLevel ForceLevel { get; init; }
        /// <summary>⭐ <c>CE-3486</c> (F-9) — the incendiary rule; <c>IncendiaryUnset</c> = the default. JSON-carried, as the force level.</summary>
        public RoeIncendiary Incendiary { get; init; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3494</c> (R-3114; <c>docs/DESIGN_Unit_Intent.md</c> §1 R2–R3, R9) — what a unit does NEXT, as AUTHORED: the task slot,
    /// the SOP slot and the ROE in the scenario's own shape (<c>CE-3042</c>, R-192). Written by the scenario load
    /// (<c>BrainSnapshotTranslator.Inject</c>), the editor in edit mode and the type's defaults; applied by <c>BehaviorIngressSystem</c>
    /// on the next frame where time ADVANCES — ⛔ never on a paused frame — and then removed (pending-only).
    /// <para>⭐ RECORDED (a checkpoint at a paused frame keeps an authored edit) and an immutable record — change it by building a new
    /// one (CLAUDE.md "NEW COMPONENTS"); <see cref="DataPolicy.NoScenario"/> only because the scenario keys are the translator's.</para>
    /// <para>⛔ SUPERSEDED: <c>InitialBrainIntent</c> — Transient, consumed one frame after a load by <c>InitialBrainMaterializationSystem</c>
    /// (deleted) on a PAUSED frame.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.InitialBrainIntent)]   // id 316 — the catalogue name predates CE-3494
    [DataPolicy(DataPolicy.NoScenario)]
    public sealed record UnitIntent
    {
        public IntentSlot? Behavior { get; init; }
        public IntentSlot? Sop { get; init; }
        public SavedRoe? Roe { get; init; }
    }
}
