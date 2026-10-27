using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Layers;

/// <summary>
/// <see cref="IMapLayer"/> that draws the 2-D map's helper grid: thin grey MINOR lines and thicker grey MAJOR lines, the step chosen
/// by the zoom so the lines stay a readable distance apart whatever the scale.
///
/// <para>⭐ <c>CE-1288</c> (docs/DESIGN_Map_3D_Mode.md §6ac). 🔒 User, 2026-10-23: <i>"the 2d map is missing any aux grid
/// system with thicker gray lines for larger step grid and thinner gray lines for fines step grid, with auto selection of the grid
/// stepping based on zoom"</i>. This class was the grid all along — a single weight, a fixed 1 000 m base, bounds taken around the
/// MOUSE rather than the view, off by default and drawn above the entities — so it is rebuilt in place rather than replaced.</para>
///
/// <para>Visibility is the <c>isVisible</c> delegate; the shared map attach binds it to the "Grid" row of the layer panel
/// (<c>DebugTraceLayers.Grid</c>). The steps and the line set are the pure <see cref="GridSteps"/>.</para>
/// </summary>
public sealed class GridMapLayer : ISceneLayer   // ⭐ CE-1380 — filmed when the film's layer set turns its row on (off by default: a helper)
{
    /// <summary>Minor line width on screen, pixels.</summary>
    public const float MinorWidthPixels = 1f;

    /// <summary>Major line width on screen, pixels — the thicker one.</summary>
    public const float MajorWidthPixels = 2f;

    // Grey, translucent: a helper under the entities, never competing with them.
    private static readonly Color MinorColor = new(150, 150, 150, 40);
    private static readonly Color MajorColor = new(150, 150, 150, 95);

    private readonly Func<bool> _isVisible;
    private readonly Func<MapCamera?> _camera;

    /// <summary>Layer name (displayed in layer-control UI).</summary>
    public string Name => "Grid";

    /// <summary>Always-on layer (never masked by the visibility bitmask) — it gates itself by <c>isVisible</c>.</summary>
    public int LayerBitIndex => -1;

    /// <summary>Lines (minor + major, both axes) drawn in the last frame — the rails and the stats read it.</summary>
    public int LinesDrawn { get; private set; }

    /// <param name="isVisible">Evaluated each frame: whether the grid is drawn.</param>
    /// <param name="camera">The map camera — where the view is and how large a metre is on screen.</param>
    public GridMapLayer(Func<bool> isVisible, Func<MapCamera?> camera)
    {
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    /// <inheritdoc/>
    public void Update(float dt) { }

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        LinesDrawn = 0;
        if (!_isVisible()) return;
        var camera = _camera();
        if (camera == null || !Raylib.IsWindowReady()) return;

        float zoom = camera.Zoom;
        var (min, max) = ViewBounds(camera, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        var steps = GridSteps.For(zoom);
        if (!steps.IsValid) return;

        // Lines are drawn inside the camera mode MapCanvas.Draw started (world units; widths are pixels / zoom).
        int n = 0;
        foreach (var line in GridSteps.Lines(min.X, max.X, steps))
        {
            Raylib.DrawLineEx(new Vector2((float)line.Position, min.Y), new Vector2((float)line.Position, max.Y),
                (line.Major ? MajorWidthPixels : MinorWidthPixels) / zoom, line.Major ? MajorColor : MinorColor);
            n++;
        }
        foreach (var line in GridSteps.Lines(min.Y, max.Y, steps))
        {
            Raylib.DrawLineEx(new Vector2(min.X, (float)line.Position), new Vector2(max.X, (float)line.Position),
                (line.Major ? MajorWidthPixels : MinorWidthPixels) / zoom, line.Major ? MajorColor : MinorColor);
            n++;
        }
        LinesDrawn = n;
    }

    /// <summary>
    /// The world rectangle (min, max) the camera shows on a <paramref name="width"/> × <paramref name="height"/> screen, from the
    /// screen's corners — the 2-D map is north-up, so screen-down is world-south and both corners are folded into min/max.
    /// ⛔ NOT the mouse position: the first version of this layer took its bounds around the cursor.
    /// </summary>
    public static (Vector2 Min, Vector2 Max) ViewBounds(MapCamera camera, float width, float height)
    {
        var a = camera.ScreenToWorld(Vector2.Zero);
        var b = camera.ScreenToWorld(new Vector2(width, height));
        return (Vector2.Min(a, b), Vector2.Max(a, b));
    }

    /// <inheritdoc/>
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    public Entity? PickEntity(Vector2 worldPos) => null;
}

/// <summary>One grid line: where it lies (world metres on its axis) and whether it is a major one.</summary>
public readonly record struct GridLine(double Position, bool Major);

/// <summary>
/// ⭐ <c>CE-1288</c> — the grid's step choice and line set, pure (no window, no camera) so a rail drives them.
///
/// <para>The steps are the 1-2-5 ladder × 10ⁿ metres (…, 0.5, 1, 2, 5, 10, 20, 50, 100, …). The MINOR step is the first rung whose
/// spacing on screen is at least <see cref="MinMinorPixels"/>; the MAJOR step is the rung two above it (×5, ×5, ×4 — always a whole
/// multiple of the minor step, so every major line lies on a minor one). Lines lie on multiples of the step from the local origin.</para>
/// </summary>
public static class GridSteps
{
    /// <summary>The closest the minor lines are drawn to each other, in screen pixels.</summary>
    public const float MinMinorPixels = 18f;

    private static readonly double[] Mantissas = { 1d, 2d, 5d };

    /// <summary>Hard cap per axis: a view that would need more lines draws none rather than stall (a degenerate camera).</summary>
    public const int MaxLinesPerAxis = 2000;

    /// <summary>The ladder's rung <paramref name="k"/> (0 → 1 m, 1 → 2 m, 2 → 5 m, 3 → 10 m, … −3 → 0.1 m).</summary>
    public static double Rung(int k)
    {
        int e = (int)Math.Floor(k / 3d);
        return Mantissas[k - e * 3] * Math.Pow(10d, e);
    }

    /// <summary>The steps for a camera <paramref name="zoom"/> (screen pixels per metre).</summary>
    public static Steps For(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0f) return default;
        double target = MinMinorPixels / (double)zoom;                 // metres the minor step must be at least
        int e = (int)Math.Floor(Math.Log10(target));
        int k = 3 * e;
        while (Rung(k) < target * (1d - 1e-6)) k++;                    // the first rung that is far enough apart
        double minor = Rung(k), major = Rung(k + 2);
        return new Steps(minor, major, (int)Math.Round(major / minor));
    }

    /// <summary>The lines of one axis between <paramref name="min"/> and <paramref name="max"/>, in ascending order.</summary>
    public static IEnumerable<GridLine> Lines(double min, double max, Steps steps)
    {
        if (!steps.IsValid || !double.IsFinite(min) || !double.IsFinite(max) || max < min) yield break;
        long first = (long)Math.Ceiling(min / steps.Minor);
        long last = (long)Math.Floor(max / steps.Minor);
        if (last - first + 1 > MaxLinesPerAxis) yield break;
        for (long i = first; i <= last; i++)
            yield return new GridLine(i * steps.Minor, i % steps.MajorEvery == 0);
    }

    /// <summary>The minor step, the major step, and how many minor steps make a major one.</summary>
    public readonly record struct Steps(double Minor, double Major, int MajorEvery)
    {
        public bool IsValid => Minor > 0d && MajorEvery > 0;
    }
}
