using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    // Icon UV descriptor returned by an atlas.
    public interface IIconAtlas
    {
        bool TryGetUv(FixedString32 atlasCoord, out Vector4 uv);
    }

    /// <summary>
    /// Minimal adapter that resolves icon atlas coordinates to UV rects
    /// and draws the icon at the specified world position.
    /// Falls back to a yellow dot when no atlas is configured or coord not found.
    /// </summary>
    public sealed class IconAtlasAdapter
    {
        private readonly IIconAtlas? _atlas;

        public IconAtlasAdapter(IIconAtlas? atlas = null)
        {
            _atlas = atlas;
        }

        public void Draw(
            FixedString32 atlasCoord,
            float worldX,
            float worldY,
            Camera2D camera,
            float zoom,
            Rgba32 color)
        {
            var pos = new Vector2(worldX, worldY);

            if (_atlas != null && _atlas.TryGetUv(atlasCoord, out _))
            {
                // In a full implementation, draw the atlas sub-texture here.
                // For this stub, draw a colored dot to indicate a resolved icon.
                Raylib.DrawCircleV(pos, 6f / zoom, new Color(color.R, color.G, color.B, color.A));
            }
            else
            {
                // Fallback: yellow dot.
                Raylib.DrawCircleV(pos, 4f / zoom, Color.Yellow);
            }
        }
    }
}
