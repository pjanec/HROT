using Fdp.Kernel;
using System.Collections.Generic;

namespace FDP.Toolkit.Vis2D.Abstractions
{
    public interface ISelectionState
    {
        bool IsSelected(Entity entity);
        IReadOnlyCollection<Entity> SelectedEntities { get; }
        Entity? PrimarySelected { get; set; }
        Entity? HoveredEntity { get; set; }
    }
}
