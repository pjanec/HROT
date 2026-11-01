using System;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> (docs/DESIGN_Scenario_Entity_Keys.md S1) — the entity's STABLE identity in a scenario file: the file's entity KEY.
    /// <list type="bullet">
    ///   <item>Read from the key on load, written back as the key on save — ⛔ never regenerated per save.</item>
    ///   <item>OPTIONAL: bullets, effects and sensor parts never carry one. An entity that came from a scenario ALWAYS has one;
    ///     an entity authored afterwards gets one at creation (<c>CreateEntityRequestSystem</c>).</item>
    ///   <item><c>NoScenario</c>: the generic serializer never writes it as a component — it is the key, not a field.</item>
    ///   <item>Replicated (<c>dtEntityGuid</c>, no ownership group — the creator keeps it) so whichever node owns the entity at save time knows it.</item>
    /// </list>
    /// </summary>
    [ComponentId(GlobalComponentIds.EntityGuid)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct EntityGuid
    {
        public Guid Value;

        public EntityGuid(Guid value) { Value = value; }

        public override string ToString() => Value.ToString();
    }
}
