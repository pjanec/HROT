using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ How exposed each candidate is to the threats the self KNOWS of (docs/designs/eqs-2/EQS_Design_v1.3_final.md §5.4,
    /// §19.5). Known threats = the <see cref="SensorContactList"/> entries in state <see cref="SensorContactState.Acquired"/> whose
    /// force is in <see cref="EqsSensor.FactionFilter"/> (0 = every contact), from the self's PERCEPTION SENSOR CHILDREN — ⭐
    /// <c>CE-3136</c> P-4 (peek-and-fire D6): <c>CE-3038</c> moved the lists there (one per sensor, written by the memory stage),
    /// so the unit-level list this read was never filled and the test was inert. The children's lists live only on the node that
    /// SOLVES them; the Brain keeps a unit's sensors on one solver (R-239), so they are here. A legacy unit-level list still
    /// counts. One threat seen by two sensors counts once.
    /// <list type="bullet">
    ///   <item>Score += <see cref="Weight"/> × (1 − exposed / threats): a point no threat can see scores the full weight.</item>
    ///   <item>Flag bit 4 <c>IsInCover</c> = hidden from every known threat; bit 5 <c>IsExposedFromKnownThreat</c> = seen by at
    ///     least one (§4.2). ⭐ This is also §5.4's <c>CoverQuality</c> — one measurement, one test.</item>
    ///   <item>Sight: each threat's standing eye → the self's crouched eye at the candidate (as the hidden-side LOS filter).</item>
    ///   <item>No known threats, or no sight source ⇒ nothing is scored or flagged.</item>
    /// </list>
    /// </summary>
    public sealed class ThreatExposureTest : IEqsTest
    {
        private readonly ILosService? _los;

        public ThreatExposureTest() { }

        /// <summary>A test over an explicit sight source (tests, host-specific sight).</summary>
        public ThreatExposureTest(ILosService los) => _los = los;

        /// <summary>The score a fully hidden candidate gains. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public unsafe void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var los = EqsTerrainSight.Sight(view, _los);
            if (los == null) return;
            var self = EqsContext.Self(view, observer, sensor);
            if (self.IsNull) return;

            Span<Vector3> eyes = stackalloc Vector3[PerceptionConstants.MaxTrackedTargets];
            Span<long> ids = stackalloc long[PerceptionConstants.MaxTrackedTargets];
            int threats = 0;
            if (view.HasComponent<SensorContactList>(self))
                Collect(view, in view.GetComponentRO<SensorContactList>(self), sensor.FactionFilter, eyes, ids, ref threats);
            bool parts = view is not EntityRepository repo
                         || (repo.IsComponentTypeRegistered<PartMetadata>() && repo.IsComponentTypeRegistered<SensorContactList>());
            if (parts)
                foreach (var child in view.Query().With<PartMetadata>().With<SensorContactList>().Build())
                    if (view.GetComponentRO<PartMetadata>(child).ParentEntity.Equals(self))
                        Collect(view, in view.GetComponentRO<SensorContactList>(child), sensor.FactionFilter, eyes, ids, ref threats);
            if (threats == 0) return;

            float crouched = EqsTerrainSight.Mount(view, self).Crouched;
            for (int c = 0; c < candidates.Length; c++)
            {
                ref var cand = ref candidates[c];
                if (cand.EntityId == -1L) continue;
                var aim = new Vector3(cand.PositionX, cand.PositionY, cand.PositionZ + crouched);
                int exposed = 0;
                for (int t = 0; t < threats; t++)
                    if (los.HasLineOfSight(eyes[t], aim)) exposed++;

                cand.Score += Weight * (1f - (float)exposed / threats);
                cand.FlagsMeaningful |= (short)((1 << 4) | (1 << 5));
                if (exposed == 0) cand.Flags |= (short)(1 << 4);
                else              cand.Flags |= (short)(1 << 5);
            }
        }

        /// <summary>Adds the acquired, live, faction-matching contacts of one list that are not yet counted.</summary>
        private static unsafe void Collect(ISimulationView view, in SensorContactList contacts, uint factionFilter,
            Span<Vector3> eyes, Span<long> ids, ref int threats)
        {
            for (int i = 0; i < contacts.Count && threats < eyes.Length; i++)
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
                ids[threats] = id;
                eyes[threats++] = view.GetComponentRO<SimTransform>(t).Position
                                + new Vector3(0, 0, EqsTerrainSight.Mount(view, t).Standing);
            }
        }
    }
}
