using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ U-8 (docs/UX/DESIGN_Condition_Editor.md §8.2 ④) — what KIND of thing an entity is, for a person, so an entity link reads
/// <i>Objective: Hold the bridge</i> and never <i>Key #10</i>. The editor's one reference scope (<see cref="AuthoredRules.ScopeOf"/>) asks it once per
/// entity per rebuild. ⭐ A rule entity (TKB 8860 / 8861) is an <i>Objective</i> / <i>Trigger</i>; anything else is its most specific entity class
/// (<see cref="EntityClassTable"/> — THE one answer to "what kind of entity is this", CE-2138); no class ⇒ no kind (the chip shows the name alone).
/// </summary>
public static class EntityKinds
{
    public static EntityKind? Describe(EntityRepository world, Entity e)
    {
        if (world.IsComponentTypeRegistered<TkbIdentity>() && world.HasComponent<TkbIdentity>(e))
        {
            long tkb = world.GetComponentRO<TkbIdentity>(e).TkbType;
            if (tkb == AuthoredRules.TkbObjective) return new EntityKind("Objective", "thing/objective");
            if (tkb == AuthoredRules.TkbTrigger) return new EntityKind("Trigger", "thing/trigger");
        }
        var chain = EntityClassTable.Default.Chain(world.GetDisType(e));
        return chain.Count == 0 ? null : new EntityKind(chain[chain.Count - 1].DisplayName, "thing/entity");
    }
}
