using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ CE-3374 (📄 docs/DESIGN_Scenario_Macros.md §17.1) — the AUTHORED variant forms of one entity, kept so an EDIT load followed by a save does not lose
    /// them: <see cref="ScenarioVariants.Apply"/> strips <c>Presence</c>, turns <c>Positions</c> into a place and a one-of-N <c>MacroInstance.Macro</c> into a
    /// name — and, before it does, copies them here (ONE writer). <see cref="VariantFormsTranslator"/> writes them back as authored on a save.
    /// <para>⭐ <see cref="Variant"/> records which variant built the world: a world built with a variant ≠ 0 is NOT saveable (an entity whose Presence roll
    /// failed was never created) — <c>ScenarioSaveGate</c> reads it (the presets design's <c>CanSave</c>).</para>
    /// ⭐ Managed, IMMUTABLE (init-only), <c>NoReplay</c> (SR-165's rule — an authoring form: never recorded, never in a checkpoint).
    /// </summary>
    [ComponentId(GlobalComponentIds.VariantForms)]
    [DataPolicy(DataPolicy.NoReplay)]
    public sealed record VariantForms
    {
        /// <summary>The key <see cref="ScenarioVariants.Apply"/> puts on the entity JSON (consumed by the translator on load, never saved).</summary>
        public const string ComponentName = "VariantForms";

        /// <summary>The variant that built the world (0 = as authored).</summary>
        public int Variant { get; init; }
        /// <summary>The authored <c>Presence</c>, null when the entity had none.</summary>
        public double? Presence { get; init; }
        /// <summary>The authored <c>Positions</c> list as JSON text, null when none.</summary>
        public string? PositionsJson { get; init; }
        /// <summary>Which entry of <see cref="PositionsJson"/> the variant chose (variant 0 ⇒ 0).</summary>
        public int PositionChosen { get; init; }
        /// <summary>The authored one-of-N <c>MacroInstance.Macro</c> list as JSON text, null when the macro was a plain name.</summary>
        public string? MacroChoicesJson { get; init; }
        /// <summary>Which entry of <see cref="MacroChoicesJson"/> the variant chose.</summary>
        public int MacroChosen { get; init; }

        /// <summary>The holder as the entity JSON carries it between <c>Apply</c> and the translator.</summary>
        public JsonObject ToJson()
        {
            var o = new JsonObject { [nameof(Variant)] = Variant };
            if (Presence is double p) o[nameof(Presence)] = p;
            if (PositionsJson != null) { o[nameof(PositionsJson)] = PositionsJson; o[nameof(PositionChosen)] = PositionChosen; }
            if (MacroChoicesJson != null) { o[nameof(MacroChoicesJson)] = MacroChoicesJson; o[nameof(MacroChosen)] = MacroChosen; }
            return o;
        }

        /// <summary>Reads <see cref="ToJson"/>'s form.</summary>
        public static VariantForms FromJson(JsonObject o) => new()
        {
            Variant = Int(o, nameof(Variant)),
            Presence = o[nameof(Presence)] is JsonValue pv && pv.TryGetValue<double>(out var p) ? p : null,
            PositionsJson = Str(o, nameof(PositionsJson)),
            PositionChosen = Int(o, nameof(PositionChosen)),
            MacroChoicesJson = Str(o, nameof(MacroChoicesJson)),
            MacroChosen = Int(o, nameof(MacroChosen)),
        };

        private static int Int(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
        private static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        /// <summary>
        /// The authored one-of-N macro list with its CHOSEN entry replaced by <paramref name="currentMacro"/> (a <i>Change…</i> changes the chosen one);
        /// null when this entity had no list. Used by <c>MacroInstanceTranslator</c> — the ONE writer of the <c>MacroInstance</c> key.
        /// </summary>
        public JsonArray? MacroListWith(string currentMacro)
        {
            if (MacroChoicesJson == null || JsonNode.Parse(MacroChoicesJson) is not JsonArray list || list.Count == 0) return null;
            int i = MacroChosen >= 0 && MacroChosen < list.Count ? MacroChosen : 0;
            if (list[i] is JsonObject entry)
            {
                string key = "macro";
                foreach (var (k, _) in entry) if (string.Equals(k, "macro", System.StringComparison.OrdinalIgnoreCase)) key = k;
                entry[key] = currentMacro;
            }
            else list[i] = currentMacro;
            return list;
        }
    }

    /// <summary>
    /// ⭐ CE-3374 — loads <see cref="VariantForms"/> from the key <c>Apply</c> wrote, and SAVES the authored forms back: <c>Presence</c> as is; <c>Positions</c>
    /// with the CHOSEN entry's place replaced by the entity's current place (a move in Edit moves the chosen place — at variant 0, the first). The macro list
    /// is written by <see cref="MacroInstanceTranslator"/> (one writer of that key), which reads this component. ⛔ <c>VariantForms</c> itself is never saved.
    /// </summary>
    public sealed class VariantFormsTranslator : IEntityScenarioTranslator
    {
        public bool IsExtractionSafe => true;   // value data only

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(VariantForms));
            mask.SetBit(id >= 0 ? id : GlobalComponentIds.VariantForms);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity) => repo.HasManagedComponent<VariantForms>(entity);

        public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var vf = ((ISimulationView)repo).GetManagedComponentRO<VariantForms>(entity);
            var output = new Dictionary<string, object>();
            if (vf.Presence is double p) output[ScenarioVariants.PresenceKey] = JsonValue.Create(p)!;
            if (vf.PositionsJson != null && JsonNode.Parse(vf.PositionsJson) is JsonArray places && places.Count > 0)
            {
                if (repo.IsComponentTypeRegistered<SimTransform>() && repo.HasComponent<SimTransform>(entity))
                {
                    var at = repo.GetComponent<SimTransform>(entity).Position;
                    var here = new JsonArray(at.X, at.Y, at.Z);
                    int i = vf.PositionChosen >= 0 && vf.PositionChosen < places.Count ? vf.PositionChosen : 0;
                    if (places[i] is JsonObject entry)
                    {
                        string key = "at";
                        foreach (var (k, _) in entry) if (string.Equals(k, "at", System.StringComparison.OrdinalIgnoreCase)) key = k;
                        entry[key] = here;
                    }
                    else places[i] = here;
                }
                output[ScenarioVariants.PositionsKey] = places;
            }
            return output;
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(VariantForms.ComponentName, out var raw) || raw is not JsonObject obj) return;
            repo.RegisterManagedComponent<VariantForms>();   // idempotent — a staging repo may not have it yet
            repo.SetManagedComponent(entity, VariantForms.FromJson(obj));
        }

        public IEnumerable<string> GetOutputDomKeys() => new[] { VariantForms.ComponentName, ScenarioVariants.PresenceKey, ScenarioVariants.PositionsKey };
    }
}
