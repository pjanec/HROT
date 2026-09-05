using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using System.Collections.Generic;

namespace Fdp.Toolkit.Vis2D.Defaults
{
    public class DefaultSelectionState : ISelectionState
    {
        private readonly HashSet<Entity> _selectedEntities = new HashSet<Entity>();
        private Entity? _primarySelected;

        public bool IsSelected(Entity entity)
        {
            return _selectedEntities.Contains(entity);
        }

        public IReadOnlyCollection<Entity> SelectedEntities => _selectedEntities;

        /// <summary>
        /// Monotonically-increasing counter. Bumped every time <see cref="PrimarySelected"/> changes
        /// or <see cref="ClearSelection"/> is called. Used by BATCH-S2-R selection sync.
        /// </summary>
        public int Version { get; private set; }

        public Entity? PrimarySelected
        {
            get => _primarySelected;
            set
            {
                // Simple implementation: Setting primary resets selection to just that one.
                // This mimics "Click to Select" behavior without shift/ctrl modifiers logic which sits usually in Input handling.
                if (_primarySelected != value)
                {
                    _primarySelected = value;
                    _selectedEntities.Clear();
                    if (value.HasValue && value.Value != Entity.Null)
                    {
                        _selectedEntities.Add(value.Value);
                    }
                    Version++;
                }
            }
        }

        public Entity? HoveredEntity { get; set; }

        // Additional methods for multi-selection can be added to the implementation
        // and used if casted, but the interface contract is what matters for decoupling.
        public void AddSelection(Entity entity)
        {
             _selectedEntities.Add(entity);
             // Logic for primary? Maybe last added?
             _primarySelected = entity;
             Version++;
        }

        public void ClearSelection()
        {
            _selectedEntities.Clear();
            _primarySelected = null;
            Version++;
        }
    }
}
