using System;
using System.Numerics;
using Fdp.Presentation.Icons;
using GizmoMap.Presentation;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// Builds a <see cref="MenuIconResolver"/> that maps a menu item's semantic icon key
/// (e.g. <c>"shell/save"</c>, <c>"asset/btree"</c>) to a colored silk-atlas sprite, via
/// <see cref="SilkIconProvider"/>. Falls back to treating the key as a raw atlas coordinate
/// (e.g. <c>"v11"</c>) so gizmo authors may use either form.
///
/// <para>Lives in the editor layer because the semantic icon vocabulary is editor-owned;
/// the shared menu renderers only know the delegate.</para>
/// </summary>
public static class SilkMenuIconResolver
{
    /// <summary>
    /// Create a resolver bound to <paramref name="atlas"/> (must be the same atlas texture the
    /// menus render against, i.e. <c>WindowManager.Atlas</c>).
    /// </summary>
    public static MenuIconResolver Create(IconAtlas atlas)
    {
        if (atlas is null) throw new ArgumentNullException(nameof(atlas));
        var provider = new SilkIconProvider(atlas);

        return (string key, out nint textureId, out Vector2 uv0, out Vector2 uv1) =>
        {
            // 1. Semantic key via the provider's name->cell map.
            if (provider.TryGet(key, out var handle))
            {
                textureId = handle.TextureId;
                uv0 = handle.Uv0;
                uv1 = handle.Uv1;
                return true;
            }

            // 2. Raw atlas coordinate fallback (e.g. "v11"). GetUvCoordinates returns the
            //    whole-texture rect (0,0)-(1,1) for malformed input, which we treat as "unknown".
            if (!string.IsNullOrEmpty(key))
            {
                var (a, b) = atlas.GetUvCoordinates(key);
                if (!(a == Vector2.Zero && b == Vector2.One))
                {
                    textureId = atlas.TextureId;
                    uv0 = a;
                    uv1 = b;
                    return true;
                }
            }

            textureId = 0;
            uv0 = default;
            uv1 = default;
            return false;
        };
    }
}
