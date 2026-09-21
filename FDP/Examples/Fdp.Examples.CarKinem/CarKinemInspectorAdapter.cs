using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Examples.CarKinem.Core;
using System.Collections.Generic;

namespace Fdp.Examples.CarKinem
{
    public class CarKinemInspectorAdapter : IInspectorContext, ISelectionState
    {
        private readonly SelectionManager _selectionManager;
        private readonly EntityRepository _repository; 
        
        private int _version;

        public CarKinemInspectorAdapter(SelectionManager selectionManager, EntityRepository repository)
        {
            _selectionManager = selectionManager;
            _repository = repository;
            _selectionManager.SelectionChanged += () => _version++;
        }

        // ISelectionState Implementation
        public bool IsSelected(Entity entity) 
        {
             return _selectionManager.Contains(entity);
        }

        public IReadOnlyCollection<Entity> SelectedEntities => _selectionManager.SelectedEntities;

        public Entity? PrimarySelected 
        {
            get => _selectionManager.PrimarySelected;
            set 
            {
                if (value.HasValue)
                    _selectionManager.Set(value.Value);
                else
                    _selectionManager.Clear();
            }
        }

        public Entity? SelectedEntity 
        { 
            get => _selectionManager.PrimarySelected;
            set 
            {
                if (value.HasValue)
                    _selectionManager.Set(value.Value);
                else
                    _selectionManager.Clear();
            }
        }
        
        public Entity? HoveredEntity 
        { 
            get => _selectionManager.HoveredEntity;
            set => _selectionManager.HoveredEntity = value;
        }

        public bool IsMergedView { get; set; }

        // ── ISelectionState, UXI-11 S-1 ───────────────────────────────────────
        // ⭐ SelectionManager already carried this vocabulary; the interface adopted its shape.

        public int Version => _version;

        public void Add(Entity entity) => _selectionManager.Add(entity);

        public void Remove(Entity entity) => _selectionManager.Remove(entity);

        public void SetMultiple(IReadOnlyCollection<Entity> entities) => _selectionManager.SetMultiple(entities);

        public void Clear() => _selectionManager.Clear();
    }
}
