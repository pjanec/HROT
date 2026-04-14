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
        
        public CarKinemInspectorAdapter(SelectionManager selectionManager, EntityRepository repository)
        {
            _selectionManager = selectionManager;
            _repository = repository;
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
    }
}
