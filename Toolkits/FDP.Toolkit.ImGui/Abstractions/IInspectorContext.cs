using Fdp.Kernel;

namespace FDP.Toolkit.ImGui.Abstractions;

/// <summary>
/// Provides selection state for inspectors, decoupled from game logic.
/// This allows the debug UI to highlight things without affecting game logic.
/// </summary>
public interface IInspectorContext
{
    /// <summary>
    /// The currently selected entity in the inspector.
    /// </summary>
    Entity? SelectedEntity { get; set; }
    
    /// <summary>
    /// The entity currently being hovered in the inspector or map.
    /// Useful for highlighting an entity in the 2D map when hovered in the inspector list.
    /// </summary>
    Entity? HoveredEntity { get; set; }
}

/// <summary>
/// A simple default implementation of IInspectorContext.
/// </summary>
public class InspectorState : IInspectorContext
{
    public Entity? SelectedEntity { get; set; }
    public Entity? HoveredEntity { get; set; }
}
