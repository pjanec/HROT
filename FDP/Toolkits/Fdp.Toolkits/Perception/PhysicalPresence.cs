using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core.CommandHierarchy;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐⭐ <c>CE-2125</c> (R-272) — the ONE answer to "is this entity a physical actor that can be seen, heard or found?". Hidden
    /// today: a body-less unit commander (<see cref="UnitEntity"/>). ⚠ R-271's I-2c adds the enclosed passenger HERE (its own
    /// <c>IsEmbarkedTag</c>), so the choke points never grow a second check. The doors that call it: the perception grid
    /// (<c>LocalGridBuilderSystem</c> — visual, thermal and acoustic sensing) and the EQS area search (<c>EntitiesInAreaGenerator</c>);
    /// hit, near-miss and <c>EntitiesInRadius</c> read the collider hash, which a body-less unit is never in.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.1 C-2.
    /// </summary>
    public static class PhysicalPresence
    {
        /// <summary>True when <paramref name="entity"/> must not be sensed or found.</summary>
        public static bool IsHidden(ISimulationView view, Entity entity)
            => view.HasComponent<UnitEntity>(entity);

        /// <summary>The same rule as a query filter, for a door that walks a query.</summary>
        public static QueryBuilder ExcludeHidden(this QueryBuilder query) => query.Without<UnitEntity>();
    }
}
