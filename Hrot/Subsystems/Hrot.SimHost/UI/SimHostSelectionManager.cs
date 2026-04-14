using Fdp.Core;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.SimHost.UI
{
    /// <summary>
    /// Tracks which entities are currently selected / hovered in the SimHost 2D window.
    /// Mirrors <c>Fdp.Examples.CarKinem.Core.SelectionManager</c> without the example
    /// project dependency.
    /// </summary>
    public class SimHostSelectionManager
    {
        private readonly HashSet<Entity> _selected = new();
        private Entity? _primary;

        public event System.Action? SelectionChanged;

        public IReadOnlyCollection<Entity> SelectedEntities => _selected;

        /// <summary>The most-recently selected entity (primary selection).</summary>
        public Entity? PrimarySelected => _primary;

        /// <summary>Convenience alias for <see cref="PrimarySelected"/>.</summary>
        public Entity? SelectedEntity => _primary;

        public int Count => _selected.Count;

        /// <summary>Entity currently under the mouse cursor (highlight only, no selection change).</summary>
        public Entity? HoveredEntity { get; set; }

        public bool Contains(Entity entity) => _selected.Contains(entity);

        public void Clear()
        {
            if (_selected.Count > 0)
            {
                _selected.Clear();
                _primary = null;
                SelectionChanged?.Invoke();
            }
        }

        public void Set(Entity entity)
        {
            if (_selected.Count == 1 && _selected.Contains(entity))
            {
                if (_primary != entity) { _primary = entity; SelectionChanged?.Invoke(); }
                return;
            }
            _selected.Clear();
            _selected.Add(entity);
            _primary = entity;
            SelectionChanged?.Invoke();
        }

        public void Add(Entity entity)
        {
            if (_selected.Add(entity))
            {
                _primary = entity;
                SelectionChanged?.Invoke();
            }
            else if (_primary != entity)
            {
                _primary = entity;
                SelectionChanged?.Invoke();
            }
        }

        public void SetMultiple(IEnumerable<Entity> entities)
        {
            _selected.Clear();
            foreach (var e in entities) _selected.Add(e);
            _primary = _selected.Count > 0 ? _selected.First() : null;
            SelectionChanged?.Invoke();
        }

        public void Remove(Entity entity)
        {
            if (_selected.Remove(entity))
            {
                if (_primary == entity)
                    _primary = _selected.Count > 0 ? _selected.First() : null;
                SelectionChanged?.Invoke();
            }
        }
    }
}
