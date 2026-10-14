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
    /// ⭐ <c>CE-2163</c> — and a ROUND IN FLIGHT (<see cref="Fdp.Toolkit.Combat.Components.BallisticProjectile"/>): it has a collider so
    /// the ballistics can carry it, but it is the thing that searches, never a thing found. 📌 In the collider hash a fresh round 1 m in
    /// front of the muzzle sat inside the shooter's avoidance shell: every shot shoved him back (−6 m/s for a frame), and a man who
    /// only suppressed crept backwards ~5 m a minute — fire and movement lost a fifth of its gain.
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
            => view.HasComponent<UnitEntity>(entity) || IsEnclosedPassenger(view, entity)
               || view.HasComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>(entity);

        /// <summary>⭐ <c>CE-1058</c> — the passenger half of <see cref="IsHidden"/>: aboard a vehicle whose cabin is ENCLOSED (its own
        /// <see cref="IsEmbarkedTag"/> with <c>Exposed = 0</c>). An exposed rider (a motorcycle, a bike, a helicopter with its side door
        /// off — the TKB's <c>PassengerExposure = Open</c>) is not. ⭐ The maps (2-D symbol and card, 3-D body, pick and selection box)
        /// draw nothing for it, by this one rule, while a body-less unit keeps its 2-D unit symbol.</summary>
        public static bool IsEnclosedPassenger(ISimulationView view, Entity entity)
            => view.HasComponent<IsEmbarkedTag>(entity) && view.GetComponentRO<IsEmbarkedTag>(entity).Exposed == 0;
    }
}
