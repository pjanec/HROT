namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Named constants for <see cref="MeasureTool"/>
/// (Â§CODE-STANDARDS Â§1 â€” no magic numbers in production code).
/// </summary>
public static class MeasureToolConstants
{
    /// <summary>Display name for the measurement gizmo.</summary>
    public const string ToolName = "Measure";

    /// <summary>
    /// Stroke thickness of the measurement line drawn between the start and end points
    /// (world units / pixels at zoom 1).
    /// </summary>
    public const float LineThickness = 2.0f;

    /// <summary>Font size of the distance label drawn at the line midpoint.</summary>
    public const int LabelFontSize = 14;

    /// <summary>
    /// Vertical pixel offset applied to the midpoint label so it does not sit
    /// exactly on the line.
    /// </summary>
    public const int LabelOffsetY = 4;
}
