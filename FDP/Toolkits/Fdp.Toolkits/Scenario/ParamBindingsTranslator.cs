using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ SR-57 / SR-163 — saves and loads <see cref="ParamBindings"/> (managed, so the auto-serializer cannot): <c>"ParamBindings":
    /// {"Bindings": {"Behavior.Params.Speed": "speed"}}</c>. So a macro file opened in the scenario editor and saved keeps its bindings,
    /// and a top-level entity bound to a scenario param keeps its binding across a save (SR-168).
    /// </summary>
    public sealed class ParamBindingsTranslator : IEntityScenarioTranslator
    {
        public bool IsExtractionSafe => true;   // value data only

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(ParamBindings));
            mask.SetBit(id >= 0 ? id : GlobalComponentIds.ParamBindings);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasManagedComponent<ParamBindings>(entity);

        public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var pb = ((ISimulationView)repo).GetManagedComponentRO<ParamBindings>(entity);
            var bindings = new JsonObject();
            if (pb?.Bindings != null)
                foreach (var kv in pb.Bindings) bindings[kv.Key] = kv.Value;
            return new Dictionary<string, object> { [ParamBindings.ComponentName] = new JsonObject { [nameof(ParamBindings.Bindings)] = bindings } };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(ParamBindings.ComponentName, out var raw) || raw is not JsonObject obj) return;
            var map = new Dictionary<string, string>();
            if (obj[nameof(ParamBindings.Bindings)] is JsonObject b)
                foreach (var (path, p) in b)
                    if (p is JsonValue v && v.TryGetValue<string>(out var s)) map[path] = s;
            repo.RegisterManagedComponent<ParamBindings>();   // idempotent — a staging repo may not have it yet
            repo.SetManagedComponent(entity, new ParamBindings { Bindings = map });
        }

        public IEnumerable<string> GetOutputDomKeys() => new[] { ParamBindings.ComponentName };
    }
}
