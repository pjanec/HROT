using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;
using System.Collections.Generic;

namespace Hrot.SimHost.UI
{
    /// <summary>
    /// Bridges <see cref="SimHostSelectionManager"/> with the FDP toolkit's
    /// <see cref="IInspectorContext"/> and <see cref="ISelectionState"/> interfaces
    /// so that <c>EntityRenderLayer</c>, <c>StandardInteractionTool</c>, and
    /// <c>EntityInspectorPanel</c> all share the same selection state.
    /// </summary>
    public class SimHostInspectorAdapter : IInspectorContext, ISelectionState
    {
        private readonly SimHostSelectionManager _sel;
        private readonly EntityRepository _repo;

        public SimHostInspectorAdapter(SimHostSelectionManager sel, EntityRepository repo)
        {
            _sel  = sel;
            _repo = repo;
        }

        // ── IInspectorContext ─────────────────────────────────────────────────

        public Entity? SelectedEntity
        {
            get => _sel.PrimarySelected;
            set
            {
                if (value.HasValue) _sel.Set(value.Value);
                else                _sel.Clear();
            }
        }

        public Entity? HoveredEntity
        {
            get => _sel.HoveredEntity;
            set => _sel.HoveredEntity = value;
        }

        // ── ISelectionState ───────────────────────────────────────────────────

        public bool IsSelected(Entity entity) => _sel.Contains(entity);

        public IReadOnlyCollection<Entity> SelectedEntities => _sel.SelectedEntities;

        public Entity? PrimarySelected
        {
            get => _sel.PrimarySelected;
            set
            {
                if (value.HasValue) _sel.Set(value.Value);
                else                _sel.Clear();
            }
        }
    }
}
