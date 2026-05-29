using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Overlays
{
    /// <summary>
    /// Emits squad-assignment overlay primitives for entities with
    /// <see cref="AiOverlayFlags.SquadAssignment"/> set in their <see cref="DebugState.Ai"/> field.
    /// Checks for <see cref="UnitRoster"/> as the proxy component indicating squad composition.
    /// </summary>
    internal sealed class SquadAssignmentOverlaySource : IGizmoSource
    {
        private readonly EntityRepository _repo;
        private readonly OverlayBudgetArbiter _budget;

        public SquadAssignmentOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
        {
            _repo   = repo;
            _budget = budget;
        }

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_budget.IsPermitted(AiOverlayFlags.SquadAssignment)) return;

            var q = _repo.Query().With<DebugState>().Build();
            foreach (var entity in q)
            {
                ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
                if ((ds.Ai & AiOverlayFlags.SquadAssignment) == 0) continue;

                EmitForEntity(entity, draw);
            }
        }

        private void EmitForEntity(Entity entity, IGizmoDrawBuilder draw)
        {
            // Emit nothing if entity has no UnitRoster (do NOT throw).
            if (!_repo.HasComponent<UnitRoster>(entity)) return;

            ref readonly var ur = ref _repo.GetComponentRO<UnitRoster>(entity);
            draw.DrawText(0f, 0f, new FixedString32($"SQUAD:{ur.Count}"), new Rgba32(0x80, 0xFF, 0x00, 0xCC));
        }
    }
}
