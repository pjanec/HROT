namespace Hrot.ScenarioEditor.Tools;

/// <summary>
/// Named constants for <see cref="CreationTool"/>
/// (Â§CODE-STANDARDS Â§1 â€” no magic numbers in production code).
/// </summary>
public static class CreationToolConstants
{
    /// <summary>Display name reported via <see cref="Fdp.Toolkit.Vis2D.Abstractions.IMapTool.Name"/>.</summary>
    public const string ToolName = "Creation";

    /// <summary>
    /// Default TKB type used when no type has been selected by the caller.
    /// Maps to a generic ground-unit placeholder in the TKB database.
    /// </summary>
    public const long DefaultTkbType = 101L;

    /// <summary>
    /// Alpha channel for the ghost preview circle drawn at the cursor while
    /// the tool is active.  Kept at half-opacity so it is clearly a preview.
    /// </summary>
    public const byte GhostAlpha = 128;

    /// <summary>Radius of the ghost preview circle drawn at the cursor (world units / pixels at zoom 1).</summary>
    public const int GhostRadiusPx = 15;

    /// <summary>Font size used for the TKB-type label drawn below the ghost circle.</summary>
    public const int GhostLabelFontSize = 10;

    /// <summary>Vertical offset below the ghost circle centre where the type label is drawn (world units).</summary>
    public const int GhostLabelOffsetY = 20;
}
