namespace Hrot.ScenarioEditor.Adapters;

/// <summary>
/// Named constants for the stub entity visualizer (Â§CODE-STANDARDS Â§1).
/// </summary>
internal static class StubVisualizerConstants
{
    /// <summary>Radius of the placeholder circle drawn for each entity, in pixels.</summary>
    public const int CircleRadiusPx = 10;

    /// <summary>Pixel offset below the circle centre where the entity label is drawn.</summary>
    public const int LabelOffsetPx = 15;

    /// <summary>Font size used for the entity ID label overlay.</summary>
    public const int LabelFontSize = 10;

    /// <summary>
    /// Hit-test radius in world units.
    /// Equals <see cref="CircleRadiusPx"/> pixels at the default initial zoom (0.5 px/m â†’ 2 m/px).
    /// Adjusting for the initial zoom keeps the hit area visually consistent on first load.
    /// </summary>
    public const float HitRadiusWorldUnits = CircleRadiusPx / 0.5f;
}
