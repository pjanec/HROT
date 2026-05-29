using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Spatial.Eqs;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Overlays
{
    /// <summary>
    /// Emits EQS overlay primitives for entities with
    /// <see cref="AiOverlayFlags.Eqs"/> set in their <see cref="DebugState.Ai"/> field.
    /// </summary>
    internal sealed class EqsOverlaySource : IGizmoSource
    {
        private readonly EntityRepository _repo;
        private readonly OverlayBudgetArbiter _budget;

        public EqsOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
        {
            _repo   = repo;
            _budget = budget;
        }

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_budget.IsPermitted(AiOverlayFlags.Eqs)) return;

            var q = _repo.Query().With<DebugState>().Build();
            foreach (var entity in q)
            {
                ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
                if ((ds.Ai & AiOverlayFlags.Eqs) == 0) continue;

                EmitForEntity(entity, draw);
            }
        }

        private void EmitForEntity(Entity entity, IGizmoDrawBuilder draw)
        {
            // Emit nothing if entity has no EqsCognitiveBuffer (do NOT throw).
            if (!_repo.HasComponent<EqsCognitiveBuffer>(entity)) return;

            ref readonly var buf = ref _repo.GetComponentRO<EqsCognitiveBuffer>(entity);
            draw.DrawText(0f, 0f, new FixedString32($"EQS:{buf.Count}"), new Rgba32(0x00, 0xCC, 0xFF, 0xCC));
        }
    }
}
