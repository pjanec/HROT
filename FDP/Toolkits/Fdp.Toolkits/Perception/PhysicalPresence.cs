using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐⭐ <c>CE-2125</c> (R-272) / <c>CE-3110</c> I-2c (R-271) — the ONE answer to "is this entity a physical actor that can be seen,
    /// heard, found, targeted or hit?". Hidden: a body-less unit commander (<see cref="UnitEntity"/>) and a passenger inside an ENCLOSED
    /// vehicle (its own <see cref="IsEmbarkedTag"/> with <c>Exposed = 0</c> — the passenger answers from its own tag, never a passenger list).
    /// ⭐ Every door that gathers candidates calls it per entity: the perception grid (<c>LocalGridBuilderSystem</c> — visual, thermal,
    /// acoustic), the EQS area search (<c>EntitiesInAreaGenerator</c>), the collider hash (<c>SpatialHashSystem</c> — bullets, near misses,
    /// the radius search, network raycasts, avoidance), the blast (<c>AreaEffectSystem</c>) and the line-of-sight occluder lists.
    /// ⛔ The query filter <c>ExcludeHidden</c> was removed: a filter cannot read <c>Exposed</c>, and two halves of one rule rot apart.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.2a · docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.1 C-2.
    /// </summary>
    public static class PhysicalPresence
    {
        /// <summary>True when <paramref name="entity"/> must not be sensed, found, targeted or hit.</summary>
        public static bool IsHidden(ISimulationView view, Entity entity)
            => view.HasComponent<UnitEntity>(entity)
               || (view.HasComponent<IsEmbarkedTag>(entity) && view.GetComponentRO<IsEmbarkedTag>(entity).Exposed == 0);
    }
}
