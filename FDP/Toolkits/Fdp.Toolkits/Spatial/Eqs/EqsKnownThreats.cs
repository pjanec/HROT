using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ The threats the self KNOWS of, as every EQS test that reads them sees them (docs/designs/eqs-2/EQS_Design_v1.3_final.md §5.4,
    /// §19.5): the <see cref="SensorContactList"/> entries in state <see cref="SensorContactState.Acquired"/>, live, placed, of a force in
    /// the sensor's <see cref="EqsSensor.FactionFilter"/> (0 = every contact), from the self's PERCEPTION SENSOR CHILDREN (⭐ <c>CE-3136</c>
    /// P-4: the memory stage writes one list per sensor) and a legacy unit-level list. One threat seen by two sensors counts once.
    /// <para>Extracted from <see cref="ThreatExposureTest"/> (<c>CE-2165</c> V3) so <see cref="BeyondThreatRangeTest"/> reads the SAME set.</para>
    /// </summary>
    public static class EqsKnownThreats
    {
        /// <summary>Fills <paramref name="ids"/> with the known threats of <paramref name="self"/>; returns how many.</summary>
        public static unsafe int Collect(ISimulationView view, Entity self, uint factionFilter, Span<long> ids)
        {
            int threats = 0;
            if (self.IsNull) return 0;
            if (view.HasComponent<SensorContactList>(self))
                Add(view, in view.GetComponentRO<SensorContactList>(self), factionFilter, ids, ref threats);
            bool parts = view is not EntityRepository repo
                         || (repo.IsComponentTypeRegistered<PartMetadata>() && repo.IsComponentTypeRegistered<SensorContactList>());
            if (parts)
                foreach (var child in view.Query().With<PartMetadata>().With<SensorContactList>().Build())
                    if (view.GetComponentRO<PartMetadata>(child).ParentEntity.Equals(self))
                        Add(view, in view.GetComponentRO<SensorContactList>(child), factionFilter, ids, ref threats);
            return threats;
        }

        /// <summary>Adds the acquired, live, placed, faction-matching contacts of one list that are not yet counted.</summary>
        private static unsafe void Add(ISimulationView view, in SensorContactList contacts, uint factionFilter, Span<long> ids, ref int threats)
        {
            for (int i = 0; i < contacts.Count && threats < ids.Length; i++)
            {
                if (contacts.State[i] != (byte)SensorContactState.Acquired) continue;
                long id = contacts.EntityIds[i];
                if (ids.Slice(0, threats).Contains(id)) continue;
                var t = new Entity((ulong)id);
                if (!view.IsAlive(t) || !view.HasComponent<SimTransform>(t)) continue;
                if (factionFilter != 0)
                {
                    if (!view.HasComponent<EntityInfo>(t)) continue;
                    if ((factionFilter & (1u << (int)view.GetComponentRO<EntityInfo>(t).ForceId)) == 0) continue;
                }
                ids[threats++] = id;
            }
        }
    }
}
