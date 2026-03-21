namespace Bagira.IG.Tools;

/// <summary>
/// Named constants for <see cref="RouteEditTool"/> geometry and thresholds.
/// </summary>
public static class RouteEditToolConstants
{
    /// <summary>Tool name returned by <see cref="RouteEditTool.Name"/>.</summary>
    public const string ToolName = "RouteEdit";

    /// <summary>
    /// Maximum world-space distance (metres) from the cursor to a vertex or segment
    /// midpoint for it to be considered a pick target.
    /// </summary>
    public const float VertexPickRadius = 15.0f;

    /// <summary>Visual radius (world units) of an unselected vertex handle circle.</summary>
    public const float HandleRadius = 5.0f;

    /// <summary>Visual radius (world units) of the selected vertex handle circle.</summary>
    public const float SelectedHandleRadius = 7.0f;
}
