namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Named constants for <see cref="EditTool"/> geometry and thresholds
/// (Â§CODE-STANDARDS Â§1 â€” no magic numbers in production code).
/// </summary>
public static class EditToolConstants
{
    /// <summary>Tool name returned by <see cref="EditTool.Name"/>.</summary>
    public const string ToolName = "Edit";

    /// <summary>
    /// Maximum world-space distance (metres) from the cursor to a vertex for
    /// the vertex to be considered a click target.
    /// </summary>
    public const float VertexPickRadiusWorldUnits = 15.0f;

    /// <summary>Visual radius (world units) of an unselected vertex handle circle.</summary>
    public const float VertexHandleRadiusWorldUnits = 5.0f;

    /// <summary>Visual radius (world units) of the selected vertex handle circle.</summary>
    public const float SelectedHandleRadiusWorldUnits = 7.0f;
}
