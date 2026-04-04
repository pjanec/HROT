namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Named constants for <see cref="MeasureTool"/>
/// (Â§CODE-STANDARDS Â§1 â€” no magic numbers in production code).
/// </summary>
public static class MeasureToolConstants
{
    /// <summary>Display name reported via <see cref="FDP.Toolkit.Vis2D.Abstractions.IMapTool.Name"/>.</summary>
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
    /// <summary>
    /// Colour of the measurement line overlay (cyan â€” clearly distinct from entity tints).
    /// Defined as a named constant rather than <c>Color.Cyan</c> because Raylib-cs does
    /// not expose that preset (Â§CODE-STANDARDS Â§1).
    /// </summary>
    public static readonly Raylib_cs.Color LineColor = new Raylib_cs.Color(0, 255, 255, 255);}
