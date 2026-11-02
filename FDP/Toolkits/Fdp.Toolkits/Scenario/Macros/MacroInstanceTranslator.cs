using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ SR-170 — saves and loads <see cref="MacroInstance"/> (managed, so the auto-serializer cannot):
    /// <c>"MacroInstance": {"Macro": "test/fire-team", "Args": { "side": "Hostile" }}</c> — Args as a real JSON object, not a string.
    /// </summary>
    public sealed class MacroInstanceTranslator : IEntityScenarioTranslator
    {
        public bool IsExtractionSafe => true;   // value data only

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(MacroInstance));
            mask.SetBit(id >= 0 ? id : GlobalComponentIds.MacroInstance);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasManagedComponent<MacroInstance>(entity);

        public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var mi = ((ISimulationView)repo).GetManagedComponentRO<MacroInstance>(entity);
            JsonNode args;
            try { args = JsonNode.Parse(string.IsNullOrWhiteSpace(mi.ArgsJson) ? "{}" : mi.ArgsJson) ?? new JsonObject(); }
            catch (System.Text.Json.JsonException) { args = new JsonObject(); }
            return new Dictionary<string, object>
            {
                [MacroInstance.ComponentName] = new JsonObject { ["Macro"] = mi.Macro, ["Args"] = args },
            };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(MacroInstance.ComponentName, out var raw) || raw is not JsonObject obj) return;
            string macro = obj["Macro"] is JsonValue m && m.TryGetValue<string>(out var s) ? s : string.Empty;
            string args = obj["Args"] is JsonObject a ? a.ToJsonString() : "{}";
            repo.RegisterManagedComponent<MacroInstance>();   // idempotent — a staging repo may not have it yet
            repo.SetManagedComponent(entity, new MacroInstance { Macro = macro, ArgsJson = args });
        }

        public IEnumerable<string> GetOutputDomKeys() => new[] { MacroInstance.ComponentName };
    }
}
