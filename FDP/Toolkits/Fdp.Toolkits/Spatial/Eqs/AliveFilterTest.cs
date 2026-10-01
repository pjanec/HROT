using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects entity candidates that are wrecks: an entity carrying <see cref="Health"/> with
    /// <c>Current &lt;= 0</c>. Entities without <see cref="Health"/> pass. Runs in FilterCheap.
    /// </summary>
    /// <remarks>
    /// ⭐ The engine's own death rule: combat death is the STATE <c>Health.Current &lt;= 0</c> and the body
    /// stays in the world (<c>HealthApplicationSystem</c>). It is the same rule the area query applies
    /// (<c>AreaQuerySolverSystem</c>, "Ignore wrecks"), so the EQS area template matches it.
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

                if (view.HasComponent<Health>(target) && view.GetComponentRO<Health>(target).Current <= 0f)
                    candidate.EntityId = -1L;
            }
        }
    }
}
