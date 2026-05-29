using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Utility;

namespace Hrot.Diagnostics.Overlays
{
    /// <summary>
    /// Emits utility-AI decision overlay primitives for entities with
    /// <see cref="AiOverlayFlags.UtilityDecision"/> set in their <see cref="DebugState.Ai"/> field.
    /// Shows the most recently selected option from the utility trace ring buffer.
    /// </summary>
    internal sealed class UtilityDecisionOverlaySource : IGizmoSource
    {
        private readonly EntityRepository _repo;
        private readonly OverlayBudgetArbiter _budget;

        public UtilityDecisionOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
        {
            _repo   = repo;
            _budget = budget;
        }

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_budget.IsPermitted(AiOverlayFlags.UtilityDecision)) return;

            var q = _repo.Query().With<DebugState>().Build();
            foreach (var entity in q)
            {
                ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
                if ((ds.Ai & AiOverlayFlags.UtilityDecision) == 0) continue;

                EmitForEntity(entity, draw);
            }
        }

        private void EmitForEntity(Entity entity, IGizmoDrawBuilder draw)
        {
            // Emit nothing if entity has no UtilityTraceWorkingMemory1024 (do NOT throw).
            if (!_repo.HasComponent<UtilityTraceWorkingMemory1024>(entity)) return;

            ref readonly var mem = ref _repo.GetComponentRO<UtilityTraceWorkingMemory1024>(entity);
            if (mem.RecordCount == 0) return;

            // Copy to a mutable local to call LatestSelected() (non-readonly method).
            var memCopy = mem;
            var result  = memCopy.LatestSelected();

            draw.DrawTextLong(
                0f, 0f,
                $"UTIL option={result.OptionId} margin={result.RunnerUpMargin:F2}",
                new Rgba32(0x00, 0xFF, 0x80, 0xCC));
        }
    }
}
