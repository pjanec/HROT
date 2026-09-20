using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;
using System.Collections.Generic;

namespace Hrot.SimHost.UI
{
    /// <summary>
    /// Bridges <see cref="SimHostSelectionManager"/> with the FDP toolkit's
    /// <see cref="IInspectorContext"/> and <see cref="ISelectionState"/> interfaces
    /// so that <c>StandardInteractionTool</c> and <c>EntityInspectorPanel</c>
    /// all share the same selection state.
    /// </summary>
    public class SimHostInspectorAdapter : IInspectorContext, ISelectionState
    {
        private readonly SimHostSelectionManager _sel;
        private readonly EntityRepository _repo;

        private int _version;

        public SimHostInspectorAdapter(SimHostSelectionManager sel, EntityRepository repo)
        {
            _sel  = sel;
            _repo = repo;
            // ⭐ UXI-11 S-1 -- the manager already announces every change, so the view's change token
            //   is exact rather than a count of the calls that happened to come through here.
            _sel.SelectionChanged += () => _version++;
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

        public bool IsMergedView { get; set; }

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

        // ── ISelectionState, UXI-11 S-1 ───────────────────────────────────────
        // ⭐ Every member forwards: SimHostSelectionManager already carried this exact vocabulary,
        //   which is why S-1 chose it for the interface rather than inventing one (the seam law).

        public int Version => _version;

        public void Add(Entity entity) => _sel.Add(entity);

        public void Remove(Entity entity) => _sel.Remove(entity);

        public void SetMultiple(IReadOnlyCollection<Entity> entities) => _sel.SetMultiple(entities);

        public void Clear() => _sel.Clear();
    }
}
