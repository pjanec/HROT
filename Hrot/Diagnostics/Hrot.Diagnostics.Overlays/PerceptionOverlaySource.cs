using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Overlays
{
    /// <summary>
    /// Emits perception overlay primitives for entities with
    /// <see cref="AiOverlayFlags.Perception"/> set in their <see cref="DebugState.Ai"/> field.
    /// Checks for <see cref="SensorContactList"/> as the proxy component indicating that
    /// autonomous perception is active for this entity.
    /// </summary>
    internal sealed class PerceptionOverlaySource : IGizmoSource
    {
        private readonly EntityRepository _repo;
        private readonly OverlayBudgetArbiter _budget;

        public PerceptionOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
        {
            _repo   = repo;
            _budget = budget;
        }

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_budget.IsPermitted(AiOverlayFlags.Perception)) return;

            var q = _repo.Query().With<DebugState>().Build();
            foreach (var entity in q)
            {
                ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
                if ((ds.Ai & AiOverlayFlags.Perception) == 0) continue;

                EmitForEntity(entity, draw);
            }
        }

        private void EmitForEntity(Entity entity, IGizmoDrawBuilder draw)
        {
            // Emit nothing if entity has no SensorContactList (do NOT throw).
            if (!_repo.HasComponent<SensorContactList>(entity)) return;

            // Emit one label per entity indicating perception is active.
            // Full FOV cone geometry is a Phase 5 stretch goal.
            draw.DrawText(0f, 0f, new FixedString32("PERCEPT"), new Rgba32(0xFF, 0xA5, 0x00, 0xCC));
        }
    }
}
