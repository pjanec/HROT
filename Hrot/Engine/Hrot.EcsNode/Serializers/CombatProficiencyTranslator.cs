using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// ⭐ <c>CE-2273</c> DA-A (DA-1) — a man's or a unit's AUTHORED <see cref="CombatProficiency"/> in the scenario as
    /// <c>"Proficiency": 0.8</c> or <c>"Proficiency": "Veteran"</c> (a tier name, <see cref="ProficiencyTier"/>). On a unit entity it fills
    /// every man under it that has none of his own (<c>ProficiencyFillSystem</c>). Only the authored value is written and read back:
    /// the unit's and the type's are derived, so a save never freezes them into the file. 📄 Defend &amp; Attack §8a.
    /// </summary>
    public sealed class CombatProficiencyTranslator : IEntityScenarioTranslator
    {
        public const string Key = "Proficiency";

        /// <summary>Three floats and a flag byte, no entity handle ⇒ the CGF staging extractor may carry it to the live entity
        /// (the <see cref="CombatTemperamentTranslator"/> precedent).</summary>
        public bool IsExtractionSafe => true;

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(CombatProficiency));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.IsComponentTypeRegistered<CombatProficiency>() && repo.HasComponent<CombatProficiency>(entity)
               && (repo.GetComponentRO<CombatProficiency>(entity).Set & CombatProficiency.AuthoredSet) != 0;

        public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver resolver)
            => new() { [Key] = JsonValue.Create(repo.GetComponentRO<CombatProficiency>(entity).Authored)! };

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || !repo.IsComponentTypeRegistered<CombatProficiency>()) return;
            if (!TryRead(raw, out float value)) return;
            var p = repo.HasComponent<CombatProficiency>(entity) ? repo.GetComponentRO<CombatProficiency>(entity) : new CombatProficiency();
            p.Authored = value;
            p.Set |= CombatProficiency.AuthoredSet;
            if (repo.HasComponent<CombatProficiency>(entity)) repo.SetComponent(entity, p);
            else repo.AddComponent(entity, p);
        }

        /// <summary>A number 0–1, or a tier name (any case).</summary>
        public static bool TryRead(object? raw, out float value)
        {
            value = 0f;
            if (raw is not JsonValue v) return false;
            if (v.TryGetValue<float>(out float f)) { value = f < 0f ? 0f : f > 1f ? 1f : f; return true; }
            if (v.TryGetValue<string>(out var s) && Enum.TryParse<ProficiencyTier>(s, ignoreCase: true, out var tier)
                && Enum.IsDefined(tier)) { value = ProficiencyOf.ValueOf(tier); return true; }
            return false;
        }

        /// <summary>Its own DOM key — so the loader's unknown-key check does not reject it.</summary>
        public IEnumerable<string> GetOutputDomKeys() => new[] { Key };
    }
}
