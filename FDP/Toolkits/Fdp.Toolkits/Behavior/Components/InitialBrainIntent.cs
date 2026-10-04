using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>⭐ <c>CE-3042</c> — one saved slot: what runs, with which params, on whose authority (R-192: nothing else).</summary>
    public sealed class SavedBrainSlot
    {
        /// <summary>The registered behaviour name.</summary>
        public string Name { get; set; } = "";
        /// <summary>The params JSON it was started with (R-191 — never bytes).</summary>
        public string Params { get; set; } = "{}";
        /// <summary>Who ordered it — the reload carries the same rank.</summary>
        public BehaviorOrigin Origin { get; set; } = BehaviorOrigin.Superior;
    }

    /// <summary>⭐ <c>CE-3042</c> — the saved ROE (R-200), only when an order changed it from the template default.</summary>
    public sealed class SavedRoe
    {
        public RoeFire Fire { get; set; }
        public RoeReactions Reactions { get; set; }
        public BehaviorOrigin SetBy { get; set; } = BehaviorOrigin.Superior;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3042</c> (R-192) — a unit's AI as the SCENARIO saved it: the task slot, the SOP slot and the ROE, each present
    /// only when it differs from the TKB template's default. Written by the scenario translator's Inject; consumed by
    /// <c>InitialBrainMaterializationSystem</c>, which starts each one THROUGH THE INGRESS at its saved origin — so a reloaded
    /// unit starts exactly as if it had just been ordered. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.5.
    /// <para>⚠ <see cref="DataPolicy.Transient"/>: an intent, never itself saved.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.InitialBrainIntent)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class InitialBrainIntent
    {
        public SavedBrainSlot? Behavior { get; set; }
        public SavedBrainSlot? Sop { get; set; }
        public SavedRoe? Roe { get; set; }
    }
}
