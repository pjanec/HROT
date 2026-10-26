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
    /// <para>⭐ <c>CE-2269</c> TS-1/TS-3 (behaviors, cross-lane) — gains <c>"nerve"</c> and <c>"resolve"</c>; and an inject REPLACES only
    /// the authored layer, keeping the TYPE fields the TKB stamped (a type's insurgent obedience survives a scenario that authors
    /// only his nerve). The type fields are never written back.</para>
    /// </summary>
    public sealed class CombatTemperamentTranslator : IEntityScenarioTranslator
    {
        public const string Key = "Temperament";
        private const string Risk = "riskAcceptance", Obey = "obedience", NerveKey = "nerve", ResolveKey = "resolve";

        /// <summary>⭐ Floats and two flag bytes, no entity handle ⇒ the CGF staging extractor may carry it to the live entity.
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
            if ((t.Set & CombatTemperament.NerveSet) != 0) obj[NerveKey] = t.Nerve;
            if ((t.Set & CombatTemperament.ResolveSet) != 0) obj[ResolveKey] = t.Resolve;
            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonObject obj) return;
            if (!repo.IsComponentTypeRegistered<CombatTemperament>()) return;
            var t = repo.HasComponent<CombatTemperament>(entity) ? repo.GetComponentRO<CombatTemperament>(entity) : new CombatTemperament();
            t.RiskAcceptance = t.Obedience = t.Nerve = t.Resolve = 0f;   // the authored layer is replaced; the type's stays
            t.Set = 0;
            if (obj[Risk] is JsonValue r && r.TryGetValue<float>(out float risk)) { t.RiskAcceptance = risk; t.Set |= CombatTemperament.RiskAcceptanceSet; }
            if (obj[Obey] is JsonValue o && o.TryGetValue<float>(out float obey)) { t.Obedience = obey; t.Set |= CombatTemperament.ObedienceSet; }
            if (obj[NerveKey] is JsonValue n && n.TryGetValue<float>(out float nerve)) { t.Nerve = nerve; t.Set |= CombatTemperament.NerveSet; }
            if (obj[ResolveKey] is JsonValue v && v.TryGetValue<float>(out float resolve)) { t.Resolve = resolve; t.Set |= CombatTemperament.ResolveSet; }
            if (t.Set == 0) return;
            if (repo.HasComponent<CombatTemperament>(entity)) repo.SetComponent(entity, t);
            else repo.AddComponent(entity, t);
        }

        /// <summary>Its own DOM key — so the loader's unknown-key check does not reject it.</summary>
        public IEnumerable<string> GetOutputDomKeys() => new[] { Key };
    }
}
