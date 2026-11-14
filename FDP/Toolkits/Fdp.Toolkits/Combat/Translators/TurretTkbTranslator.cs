using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Translators
{
    /// <summary>
    /// ⭐ WL-1 (docs/DESIGN_Weapon_Layers.md W-9) — one turret part child per <see cref="TurretDto"/>, on EVERY node that spawns the
    /// unit (unlike weapon-mount children, which only the Brain builds): a viewer needs the turret to draw it, and the replicated pose
    /// lands on it. Built by <see cref="TurretParts.Ensure"/>, so a second Inject makes no second child.
    /// </summary>
    public sealed class TurretTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(TurretSuiteDto);
        }

        /// <summary>⚠ Both land on the CHILD, not on the unit.</summary>
        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(PartMetadata);
            yield return typeof(TurretPose);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var suite = template.GetDescriptor<TurretSuiteDto>();
            if (suite == null || suite.Turrets.Count == 0) return;
            int count = Math.Min(suite.Turrets.Count, TurretParts.MaxTurrets);
            for (int i = 0; i < count; i++)
                TurretParts.Ensure(repo, entity, i);
        }
    }
}
