using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;

namespace Hrot.Map.Definitions.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-2125</c> (R-272) — a TKB COMPOSITE type (a squad, a platoon: <see cref="TkbCompositionDef"/>) is a unit, not a body:
    /// stamp <see cref="UnitEntity"/> with the type's command-disruption time. Runs wherever the base translators run — on the
    /// creator and on every ghost promotion — so every node knows it. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.1.
    /// </summary>
    public sealed class CompositeUnitTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(TkbCompositionDef);
        }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(UnitEntity);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var composite = template.GetDescriptor<TkbCompositionDef>();
            if (composite == null || !repo.IsComponentTypeRegistered<UnitEntity>()) return;
            var unit = new UnitEntity { CommandDisruptionSeconds = composite.CommandDisruptionSeconds > 0f ? composite.CommandDisruptionSeconds : 6f };
            if (repo.HasComponent<UnitEntity>(entity)) repo.SetComponent(entity, unit);
            else repo.AddComponent(entity, unit);
        }
    }
}
