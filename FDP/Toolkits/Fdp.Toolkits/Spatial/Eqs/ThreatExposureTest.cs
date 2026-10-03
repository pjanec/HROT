using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ How exposed each candidate is to the threats the self KNOWS of (docs/designs/eqs-2/EQS_Design_v1.3_final.md §5.4,
    /// §19.5). Known threats = the self's <see cref="SensorContactList"/> entries in state <see cref="SensorContactState.Acquired"/>
    /// whose force is in <see cref="EqsSensor.FactionFilter"/> (0 = every contact) — perception's tracks, which live on the
    /// Muscle where this runs.
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
            if (self.IsNull || !view.HasComponent<SensorContactList>(self)) return;

            Span<Vector3> eyes = stackalloc Vector3[PerceptionConstants.MaxTrackedTargets];
            int threats = 0;
            ref readonly var contacts = ref view.GetComponentRO<SensorContactList>(self);
            for (int i = 0; i < contacts.Count && threats < eyes.Length; i++)
            {
                if (contacts.State[i] != (byte)SensorContactState.Acquired) continue;
                var t = new Entity((ulong)contacts.EntityIds[i]);
                if (!view.IsAlive(t) || !view.HasComponent<SimTransform>(t)) continue;
                if (sensor.FactionFilter != 0)
                {
                    if (!view.HasComponent<EntityInfo>(t)) continue;
                    if ((sensor.FactionFilter & (1u << (int)view.GetComponentRO<EntityInfo>(t).ForceId)) == 0) continue;
                }
                eyes[threats++] = view.GetComponentRO<SimTransform>(t).Position
                                + new Vector3(0, 0, EqsTerrainSight.Mount(view, t).Standing);
            }
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
    }
}
