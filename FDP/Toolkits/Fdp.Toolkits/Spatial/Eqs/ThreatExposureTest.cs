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
    public sealed class ThreatExposureTest : IEqsTest, IEqsCountedWork
    {
        private readonly ILosService? _los;

        public ThreatExposureTest() { }

        /// <summary>A test over an explicit sight source (tests, host-specific sight).</summary>
        public ThreatExposureTest(ILosService los) => _los = los;

        /// <summary>⭐ <c>AQ88</c> C (R-265) — the weight the cover and retreat templates give hiding from ALL threats: above the sum of
        /// their other scores (distance ≤ 1, path cost ≤ 1), so a point hidden from everyone wins over a nearer one seen by someone.</summary>
        public const float HideFromAllWeight = 3f;

        /// <summary>The score a fully hidden candidate gains. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        // ⭐ CE-3279 (c) — a sight check per known threat per candidate: this thread's count for the last batch
        [ThreadStatic] private static int t_work;

        /// <inheritdoc/>
        public int TakeCountedWork() { int w = t_work; t_work = 0; return w; }

        public unsafe void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            t_work = candidates.Length * EqsCost.Cheap;   // ⭐ CE-3279 (c) — the pass itself, even when nothing is known
            var los = EqsTerrainSight.Sight(view, _los);
            if (los == null) return;
            var self = EqsContext.Self(view, observer, sensor);
            if (self.IsNull) return;

            Span<Vector3> eyes = stackalloc Vector3[PerceptionConstants.MaxTrackedTargets];
            Span<long> ids = stackalloc long[PerceptionConstants.MaxTrackedTargets];
            int threats = EqsKnownThreats.Collect(view, self, sensor.FactionFilter, ids);
            for (int t = 0; t < threats; t++)
            {
                var e = new Entity((ulong)ids[t]);
                eyes[t] = view.GetComponentRO<SimTransform>(e).Position + new Vector3(0, 0, EqsTerrainSight.Mount(view, e).Standing);
            }
            if (threats == 0) return;
            t_work = candidates.Length * threats * EqsCost.Sight;   // 📐 live: ~42 µs a candidate with ~4 threats ≈ 15 units

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
