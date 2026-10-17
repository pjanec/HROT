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
    /// ⭐ AQ89 B (R-269) — a unit's <see cref="CombatTemperament"/> in the scenario as <c>"Temperament": { "riskAcceptance": 0.8,
    /// "obedience": 0.9 }</c>, either key optional. Only an AUTHORED field is written and read back: inject sets its
    /// <see cref="CombatTemperament.Set"/> bit, extract writes only set fields — so an omitted field keeps reading the default
    /// (<see cref="TemperamentOf"/>) and a save never freezes today's default into the file.
    /// </summary>
    public sealed class CombatTemperamentTranslator : IEntityScenarioTranslator
    {
        public const string Key = "Temperament";
        private const string Risk = "riskAcceptance", Obey = "obedience";

        /// <summary>⭐ Two floats and a flag byte, no entity handle ⇒ the CGF staging extractor may carry it to the live entity.
        /// 📌 Measured on the cluster: without this the extractor dropped it (a translator-consumed component is excluded by
        /// default) and the Grenadier ran on the defaults.</summary>
        public bool IsExtractionSafe => true;

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(CombatTemperament));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.IsComponentTypeRegistered<CombatTemperament>() && repo.HasComponent<CombatTemperament>(entity)
               && repo.GetComponentRO<CombatTemperament>(entity).Set != 0;

        public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var t = repo.GetComponentRO<CombatTemperament>(entity);
            var obj = new JsonObject();
            if ((t.Set & CombatTemperament.RiskAcceptanceSet) != 0) obj[Risk] = t.RiskAcceptance;
            if ((t.Set & CombatTemperament.ObedienceSet) != 0) obj[Obey] = t.Obedience;
            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonObject obj) return;
            if (!repo.IsComponentTypeRegistered<CombatTemperament>()) return;
            var t = new CombatTemperament();
            if (obj[Risk] is JsonValue r && r.TryGetValue<float>(out float risk)) { t.RiskAcceptance = risk; t.Set |= CombatTemperament.RiskAcceptanceSet; }
            if (obj[Obey] is JsonValue o && o.TryGetValue<float>(out float obey)) { t.Obedience = obey; t.Set |= CombatTemperament.ObedienceSet; }
            if (t.Set == 0) return;
            if (repo.HasComponent<CombatTemperament>(entity)) repo.SetComponent(entity, t);
            else repo.AddComponent(entity, t);
        }

        /// <summary>Its own DOM key — so the loader's unknown-key check does not reject it.</summary>
        public IEnumerable<string> GetOutputDomKeys() => new[] { Key };
    }
}
