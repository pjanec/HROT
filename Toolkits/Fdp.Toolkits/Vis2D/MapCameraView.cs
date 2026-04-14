using System.Numerics;

namespace Fdp.Toolkit.Vis2D.Components
{
    /// <summary>
    /// Plain-data snapshot of a 2-D map camera, used as the exchange type for
    /// <see cref="Fdp.Engine.Runner.IMapCameraProvider"/> so that the engine layer
    /// can transfer camera state without depending on the Raylib rendering layer.
    /// </summary>
    public readonly struct MapCameraView
    {
        public Vector2 Target       { get; init; }
        public Vector2 Offset       { get; init; }
        public float   Zoom         { get; init; }
        // Smooth-follow targets (used by MapCamera animation logic)
        public Vector2 SmoothTarget { get; init; }
        public float   SmoothZoom   { get; init; }
    }
}
