using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects entity candidates that are out of the fight: wrecks and bodies (<see cref="Health"/> <c>Current &lt;= 0</c>) and,
    /// ⭐ CE-2155 (R-2011 I4), the incapacitated. Entities without <see cref="Health"/> pass. Runs in FilterCheap.
    /// </summary>
    /// <remarks>
    /// ⭐ The engine's own rule, asked through <c>ThreatDanger.IsOutOfTheFight</c>: combat death is the STATE <c>Health.Current &lt;= 0</c>
    /// and the body stays in the world (<c>HealthApplicationSystem</c>); a man down (<c>Injury</c>) is alive but no target.
    /// </remarks>
    public sealed class AliveFilterTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];
                if (candidate.EntityId == -1L || candidate.EntityId == 0L) continue;

                var target = new Entity((ulong)candidate.EntityId);
                if (!view.IsAlive(target)) { candidate.EntityId = -1L; continue; }

                // ⭐ CE-2155 (R-2011 I4) — the dead AND the incapacitated: a man down is no longer a target.
                if (Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(view, target))
                    candidate.EntityId = -1L;
            }
        }
    }
}
