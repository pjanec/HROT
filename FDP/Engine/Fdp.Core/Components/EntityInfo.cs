namespace Fdp.Core
{
    [ComponentId(GlobalComponentIds.EntityInfo)]
    // ⭐⭐ Faction and name are authored PER SPAWN — ScenarioSpawnAdapter.cs:189 and the child-entity path in
    //   CreateEntityRequestSystem both set them per instance — so the value BehaviorTkbTranslator.cs:35
    //   stamps from the template's BehaviorProfileDto is a placeholder that must lose to the instance's own.
    //   ⇒ a ghost must not be promoted, and have the template default applied, before the real one arrives.
    //   📄 PerInstanceValueAttribute; its egress guarantees a baseline via SmartEgressUtil's first-publish.
    // ⛔ NOT [BirthCritical] — an entity can correctly exist for a frame with a default affiliation; it is
    //   a position that cannot start empty.
    [PerInstanceValue]
    public struct EntityInfo
    {
        public FixedString64 Name;
        public ForceId ForceId;
    }
}
