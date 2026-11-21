using System;
using System.Numerics;

namespace Fdp.Toolkit.Vis2D.Components
{
    /// <summary>
    /// ⭐ CE-1462 (W-B1) — THE fit of a ground rectangle into the visible map: the pure rule every "frame this" caller uses (the terrain on load,
    /// docs/DESIGN_Map_3D_Mode.md §6 "the 2-D map frames its terrain"). The rectangle's centre is the camera's target; the zoom is the largest that shows the whole
    /// rectangle plus <paramref name="margin"/> on every side in <paramref name="area"/> pixels, clamped to the camera's own limits.
    /// </summary>
    public static class MapFraming
    {
        /// <summary>The margin a terrain gets on each side (a fraction of its size).</summary>
        public const float TerrainMargin = 0.05f;

        /// <param name="rectMin">Ground rectangle, world metres (x, y).</param>
        /// <param name="area">The visible map area in pixels (the dockspace hole, else the window).</param>
        /// <param name="viewCentre">Where on screen the map's centre is (<see cref="MapCamera.ViewCentre"/>).</param>
        /// <returns>The view to apply, or null for an empty / non-finite rectangle or area.</returns>
        public static MapCameraView? Fit(Vector2 rectMin, Vector2 rectMax, Vector2 area, Vector2 viewCentre, float margin = TerrainMargin, float minZoom = 0.1f, float maxZoom = 100f)
        {
            var size = rectMax - rectMin;
            if (!(size.X > 0f && size.Y > 0f) || !float.IsFinite(size.X) || !float.IsFinite(size.Y) || !(area.X > 0f && area.Y > 0f)) return null;
            float grow = 1f + 2f * Math.Max(0f, margin);
            float zoom = Math.Min(area.X / (size.X * grow), area.Y / (size.Y * grow));
            zoom = Math.Clamp(zoom, minZoom, maxZoom);
            var centre = (rectMin + rectMax) / 2f;
            return new MapCameraView { Target = centre, Offset = viewCentre, Zoom = zoom, SmoothTarget = centre, SmoothZoom = zoom };
        }

        /// <summary>
        /// When to frame: the decision, pure. <paramref name="name"/> is the resident terrain's name (null = none — forgets, so the next terrain frames again), <paramref name="hasBounds"/> the terrain has a drawable extent,
        /// <paramref name="switching"/> the 2-D↔3-D switch is mid-flight (retry), <paramref name="leaveAlone"/> the camera is the user's or a station's right now (3-D on screen, followed, driven): that terrain is
        /// CONSUMED unframed — a view the user or a station owns is never taken, and it is not framed later behind their back.
        /// </summary>
        public sealed class Framer
        {
            private string? _framed;

            public bool Step(string? name, bool hasBounds, bool switching, bool leaveAlone)
            {
                if (name is null) { _framed = null; return false; }
                if (name == _framed) return false;
                if (!hasBounds || switching) return false;   // wait: the terrain is not drawable yet / the view is mid-switch
                _framed = name;
                return !leaveAlone;
            }
        }
    }
}
