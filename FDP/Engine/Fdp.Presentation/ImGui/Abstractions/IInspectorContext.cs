using Fdp.Core;

namespace Fdp.Presentation.Abstractions;

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

    /// <summary>
    /// True when the active view is Merged View. Used by field flagging helpers.
    /// </summary>
    bool IsMergedView { get; set; }
}

/// <summary>
/// A simple default implementation of IInspectorContext.
/// </summary>
public class InspectorState : IInspectorContext
{
    public Entity? SelectedEntity { get; set; }
    public Entity? HoveredEntity { get; set; }
    public bool IsMergedView { get; set; }
}

/// <summary>
/// Returns true when an Entity-typed inspector field should be flagged
/// as a potential paradox. DESIGN §8.3.
/// </summary>
public static class EntityFieldParadoxHelper
{
    public static bool ShouldFlag(Entity value, bool isMergedView)
        => isMergedView && value.IsNull;

    public static string ParadoxTooltip =>
        "Referenced entity not present in federated snapshot. This may be due to " +
        "a manual time offset, or a recorded cluster desync in the original live run.";
}
