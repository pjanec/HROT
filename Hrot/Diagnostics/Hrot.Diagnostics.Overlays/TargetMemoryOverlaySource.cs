using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;

namespace Hrot.Diagnostics.Overlays
{
    /// <summary>
    /// Emits target memory overlay primitives (one sphere per tracked target) for entities with
    /// <see cref="AiOverlayFlags.TargetMemory"/> set in their <see cref="DebugState.Ai"/> field.
    /// </summary>
    internal sealed class TargetMemoryOverlaySource : IGizmoSource
    {
        private readonly EntityRepository _repo;
        private readonly OverlayBudgetArbiter _budget;

        public TargetMemoryOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
        {
            _repo   = repo;
            _budget = budget;
        }

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_budget.IsPermitted(AiOverlayFlags.TargetMemory)) return;

            var q = _repo.Query().With<DebugState>().Build();
            foreach (var entity in q)
            {
                ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
                if ((ds.Ai & AiOverlayFlags.TargetMemory) == 0) continue;

                EmitForEntity(entity, draw);
            }
        }

        private unsafe void EmitForEntity(Entity entity, IGizmoDrawBuilder draw)
        {
            // Emit nothing if entity has no TargetMemory (do NOT throw).
            if (!_repo.HasComponent<TargetMemory>(entity)) return;

            ref readonly var tm = ref _repo.GetComponentRO<TargetMemory>(entity);

            // Draw one sphere per tracked target at its last-known world position.
            for (int i = 0; i < tm.Count; i++)
            {
                var center = new Vector3(tm.PositionsX[i], tm.PositionsY[i], tm.PositionsZ[i]);
                draw.DrawSphere(center, 0.5f, new Rgba32(0xFF, 0x00, 0x00, 0xCC));
            }
        }
    }
}
