using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 — a test that reads the TKB. The solver runs on a snapshot that carries none, so the host hands the LIVE
    /// world's TKB to every such test once, when it builds the EQS module (<c>EqsModule.ForTerrainHost</c> →
    /// <see cref="InjectTkb"/>): a reference read at call time, nothing copied (docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §3).
    /// </summary>
    public interface ITkbAwareTest
    {
        /// <summary>Where the test reads the TKB from; null ⇒ the view's own singleton (a live world), else none.</summary>
        Func<ITkbDatabase?>? Tkb { get; set; }
    }

    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 (DESIGN_Vehicle_Exposure_Cycle §3) — prefer points OUTSIDE the weapon reach of the threats the self knows of:
    /// per threat that can hurt the self at all (<see cref="ThreatDanger.ReachAgainst"/> — a rifle cannot hurt a tank, so it does not
    /// push a tank back), the candidate's ground distance ÷ his reach, capped at 1; the score gains <see cref="Weight"/> × the MEAN
    /// over those threats. Outside every reach ⇒ the full weight; on top of him ⇒ 0. No threat that can hurt the self, or no TKB ⇒
    /// nothing is scored (the template's other tests decide).
    /// </summary>
    public sealed class BeyondThreatRangeTest : IEqsTest, ITkbAwareTest
    {
        /// <summary>The score a point outside every reach gains. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public Func<ITkbDatabase?>? Tkb { get; set; }

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var db = Tkb != null ? Tkb() : view is EntityRepository r && r.HasSingletonManaged<ITkbDatabase>() ? r.GetSingletonManaged<ITkbDatabase>() : null;
            if (db == null) return;
            var self = EqsContext.Self(view, observer, sensor);
            if (self.IsNull) return;

            Span<long> ids = stackalloc long[PerceptionConstants.MaxTrackedTargets];
            Span<Vector2> at = stackalloc Vector2[PerceptionConstants.MaxTrackedTargets];
            Span<float> reach = stackalloc float[PerceptionConstants.MaxTrackedTargets];
            int known = EqsKnownThreats.Collect(view, self, sensor.FactionFilter, ids), threats = 0;
            for (int t = 0; t < known; t++)
            {
                var e = new Entity((ulong)ids[t]);
                float hisReach = ThreatDanger.ReachAgainst(view, db, self, e);
                if (hisReach <= 0f) continue;
                var p = view.GetComponentRO<SimTransform>(e).Position;
                at[threats] = new Vector2(p.X, p.Y);
                reach[threats++] = hisReach;
            }
            if (threats == 0) return;

            for (int c = 0; c < candidates.Length; c++)
            {
                ref var cand = ref candidates[c];
                if (cand.EntityId == -1L) continue;
                var point = new Vector2(cand.PositionX, cand.PositionY);
                float sum = 0f;
                for (int t = 0; t < threats; t++)
                    sum += Math.Min(1f, Vector2.Distance(point, at[t]) / reach[t]);
                cand.Score += Weight * sum / threats;
            }
        }

        /// <summary>Hands <paramref name="tkb"/> to every <see cref="ITkbAwareTest"/> of every template in <paramref name="registry"/> —
        /// once, when the host builds the module (each world's registry builds its own template instances).</summary>
        public static void InjectTkb(EqsTemplateRegistry registry, Func<ITkbDatabase?> tkb)
        {
            foreach (var entry in registry.Entries)
            {
                if (!registry.TryGetTemplate(entry.BlueprintId, out var template)) continue;
                Inject(template.FilterCheap, tkb);
                Inject(template.FilterExpensive, tkb);
                Inject(template.ScoreCheap, tkb);
                Inject(template.ScoreExpensive, tkb);
            }
        }

        private static void Inject(IEqsTest[]? tests, Func<ITkbDatabase?> tkb)
        {
            if (tests == null) return;
            foreach (var test in tests)
                if (test is ITkbAwareTest aware) aware.Tkb = tkb;
        }
    }
}
