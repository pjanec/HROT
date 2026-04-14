using Fdp.Core;
using System.Collections.Generic;
using System.Linq;

namespace Fdp.Examples.CarKinem.Core
{
    public class SelectionManager
    {
        private readonly HashSet<Entity> _selectedEntities = new();
        // Track the last selected entity as "Primary"
        private Entity? _primarySelected;

        public event System.Action? SelectionChanged;

        public IReadOnlyCollection<Entity> SelectedEntities => _selectedEntities;
        
        /// <summary>
        /// The primary (most recently selected) entity in the selection set.
        /// </summary>
        public Entity? PrimarySelected => _primarySelected;
        
        // Legacy/Compatibility helper if needed, but per instructions we want PrimarySelected.
        // If single selection is active, this returns it.
        public Entity? SelectedEntity => _primarySelected;

        public int Count => _selectedEntities.Count;
        public Entity? HoveredEntity { get; set; }

        public void Clear()
        {
            if (_selectedEntities.Count > 0)
            {
                _selectedEntities.Clear();
                _primarySelected = null;
                SelectionChanged?.Invoke();
            }
        }

        public void Set(Entity entity)
        {
            // If it's already the only selected item, do nothing
            if (_selectedEntities.Count == 1 && _selectedEntities.Contains(entity)) 
            {
                // Ensure primary is correct even if it was already selected (e.g. via additive before)
                if (_primarySelected != entity)
                {
                    _primarySelected = entity;
                    SelectionChanged?.Invoke();
                }
                return;
            }

            _selectedEntities.Clear();
            _selectedEntities.Add(entity);
            _primarySelected = entity;
            SelectionChanged?.Invoke();
        }

        public void Add(Entity entity)
        {
            if (_selectedEntities.Add(entity))
            {
                _primarySelected = entity;
                SelectionChanged?.Invoke();
            }
            else if (_primarySelected != entity)
            {
                // If already selected but not primary, make it primary on re-click
                _primarySelected = entity;
                SelectionChanged?.Invoke();
            }
        }

        public void Remove(Entity entity)
        {
             if (_selectedEntities.Remove(entity))
             {
                 if (_primarySelected == entity)
                 {
                     _primarySelected = _selectedEntities.Count > 0 ? _selectedEntities.First() : null; // Arbitrary fallback or null
                 }
                 SelectionChanged?.Invoke();
             }
        }

        public void SetMultiple(IEnumerable<Entity> entities)
        {
            _selectedEntities.Clear();
            _primarySelected = null;
            bool any = false;
            foreach (var e in entities) 
            {
                _selectedEntities.Add(e);
                _primarySelected = e; // Last one becomes primary
                any = true;
            }
            if (any || _selectedEntities.Count > 0) // Should trigger if it was previously populated
            {
                SelectionChanged?.Invoke();
            }
        }

        public bool Contains(Entity entity) => _selectedEntities.Contains(entity);
    }
}
