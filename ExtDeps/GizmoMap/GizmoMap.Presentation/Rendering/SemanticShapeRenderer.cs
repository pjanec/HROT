using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    // Profile descriptor for a semantic entity shape.
    public struct SemanticShapeProfile
    {
        public float LengthMeters;
        public float WidthMeters;
        public string DisplayName;
    }

    // Registry interface for resolving a profile ID to a shape profile.
    public interface ISemanticShapeProfileRegistry
    {
        bool TryGetProfile(ulong profileId, out SemanticShapeProfile profile);
    }

    /// <summary>
    /// Profile-based renderer for semantic entity shapes.
    /// Uses an optional <see cref="ISemanticShapeProfileRegistry"/> to look up profiles.
    /// Falls back to a magenta outline circle when no profile is found.
    /// </summary>
    public sealed class SemanticShapeRenderer
    {
        private readonly ISemanticShapeProfileRegistry? _registry;

        public SemanticShapeRenderer(ISemanticShapeProfileRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Draw(
            ulong profileId,
            float centerX,
            float centerY,
            float lengthMeters,
            float widthMeters,
            uint conditionMask,
            Camera2D camera,
            float zoom,
            Rgba32 color)
        {
            float geomScale = 1f / zoom;

            if (_registry != null && _registry.TryGetProfile(profileId, out var profile))
            {
                float len = profile.LengthMeters > 0f ? profile.LengthMeters : lengthMeters;
                float wid = profile.WidthMeters  > 0f ? profile.WidthMeters  : widthMeters;

                var rect = new Rectangle(
                    centerX - len * geomScale * 0.5f,
                    centerY - wid * geomScale * 0.5f,
                    len * geomScale,
                    wid * geomScale);

                var raylibColor = new Color(color.R, color.G, color.B, color.A);
                Raylib.DrawRectangleLinesEx(rect, 1f, raylibColor);

                // Bit 0 = Damaged: draw a red X overlay.
                if ((conditionMask & 1u) != 0)
                {
                    Raylib.DrawLineEx(
                        new Vector2(rect.X, rect.Y),
                        new Vector2(rect.X + rect.Width, rect.Y + rect.Height),
                        1.5f, Color.Red);
                    Raylib.DrawLineEx(
                        new Vector2(rect.X + rect.Width, rect.Y),
                        new Vector2(rect.X, rect.Y + rect.Height),
                        1.5f, Color.Red);
                }
            }
            else
            {
                // Fallback: magenta outline circle.
                float radius = lengthMeters > 0f ? lengthMeters * geomScale : 5f;
                Raylib.DrawCircleLines((int)centerX, (int)centerY, radius, Color.Magenta);
            }
        }
    }
}
