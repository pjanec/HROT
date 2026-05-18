using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Layers;

/// <summary>
/// <see cref="IMapLayer"/> that renders an adaptive coordinate grid over the 2-D map canvas.
///
/// <para>Visibility is controlled by the <c>isVisible</c> delegate supplied at construction
/// time, allowing the host application to toggle the grid without recomposing the layer stack
/// (e.g. bind it directly to <c>MapViewConfig.ShowGrid</c>).</para>
///
/// <para>The grid spacing auto-scales so that at most <see cref="MaxGridLines"/> lines are
/// drawn in each axis at the current zoom level.</para>
/// </summary>
public sealed class GridMapLayer : IMapLayer
{
    private const float BaseSpacingMeters = 1000f;
    private const int   MaxGridLines      = 80;
    private static readonly Color LineColor = new(200, 200, 200, 60);

    private readonly Func<bool> _isVisible;

    /// <summary>Layer name (displayed in layer-control UI).</summary>
    public string Name => "Grid";

    /// <summary>Always-on layer (never masked by the visibility bitmask).</summary>
    public int LayerBitIndex => -1;

    /// <summary>
    /// Initialises a new <see cref="GridMapLayer"/>.
    /// </summary>
    /// <param name="isVisible">
    /// Delegate evaluated each frame to determine whether the grid should be drawn.
    /// </param>
    public GridMapLayer(Func<bool> isVisible)
    {
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
    }

    /// <inheritdoc/>
    public void Update(float dt) { }

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        if (!_isVisible()) return;

        float zoom    = ctx.Zoom > 0f ? ctx.Zoom : 1f;
        float screenW = Raylib.GetScreenWidth();
        float screenH = Raylib.GetScreenHeight();

        // Estimate visible world bounds from zoom and screen dimensions.
        // Use a multiplier of 2 to conservatively cover the full screen
        // regardless of camera offset.
        float halfW = screenW / zoom;
        float halfH = screenH / zoom;
        var   center = ctx.MouseWorldPos;

        float worldLeft   = center.X - halfW;
        float worldRight  = center.X + halfW;
        float worldTop    = center.Y - halfH;
        float worldBottom = center.Y + halfH;

        float visW = worldRight  - worldLeft;
        float visH = worldBottom - worldTop;

        // Auto-scale spacing so we never exceed MaxGridLines in either axis.
        float spacing = BaseSpacingMeters;
        while (visW / spacing > MaxGridLines || visH / spacing > MaxGridLines)
            spacing *= 10f;
        while (spacing > BaseSpacingMeters
            && visW / (spacing / 10f) <= MaxGridLines
            && visH / (spacing / 10f) <= MaxGridLines)
            spacing /= 10f;

        float startX = MathF.Floor(worldLeft  / spacing) * spacing;
        float startY = MathF.Floor(worldTop   / spacing) * spacing;

        // Lines are drawn inside the currently active camera mode (started by MapCanvas.Draw).
        for (float x = startX; x <= worldRight  + spacing; x += spacing)
            Raylib.DrawLineV(new Vector2(x, worldTop    - spacing),
                             new Vector2(x, worldBottom + spacing), LineColor);

        for (float y = startY; y <= worldBottom + spacing; y += spacing)
            Raylib.DrawLineV(new Vector2(worldLeft  - spacing, y),
                             new Vector2(worldRight + spacing, y), LineColor);
    }

    /// <inheritdoc/>
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    public Entity? PickEntity(Vector2 worldPos) => null;
}
