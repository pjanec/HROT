using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Resolves a menu item's icon key to a drawable atlas sprite.
    /// The key is a semantic name (e.g. <c>"save"</c>, <c>"delete"</c>) that the host maps to
    /// its icon atlas, falling back to a raw atlas coordinate when unmapped. Returns
    /// <c>false</c> when the key cannot be resolved.
    /// <para>
    /// The host supplies this (it owns the atlas + the semantic vocabulary), so GizmoMap stays
    /// free of any icon-atlas dependency.
    /// </para>
    /// </summary>
    public delegate bool MenuIconResolver(string key, out nint textureId, out Vector2 uv0, out Vector2 uv1);

    /// <summary>
    /// Shared helper for rendering a colored icon in a fixed left "gutter" of a menu row.
    /// The gutter is reserved by left-padding the label with spaces, so the native
    /// <c>MenuItem</c>/<c>BeginMenu</c> row (highlight, checkmark, shortcut, submenu arrow,
    /// keyboard nav) is preserved and every label lines up whether or not its row has an icon.
    /// </summary>
    public static class MenuIconRenderer
    {
        /// <summary>
        /// Left-pad <paramref name="label"/> with spaces sized to clear one icon cell, and report
        /// the reserved gutter width in pixels. Draw the row with the returned label, then call
        /// <see cref="DrawIcon"/> to overlay the icon into the gutter.
        /// </summary>
        public static string Pad(string label, out float gutterPx)
        {
            float line = ImGui.GetTextLineHeight();
            float spaceW = ImGui.CalcTextSize(" ").X;
            if (spaceW <= 0f) spaceW = 1f;
            float desired = line + line * 0.40f;               // icon square + breathing room
            int spaces = Math.Max(1, (int)MathF.Ceiling(desired / spaceW));
            gutterPx = spaces * spaceW;
            return new string(' ', spaces) + label;
        }

        /// <summary>
        /// Overlay the resolved icon into the gutter. <paramref name="rowStart"/> is the value of
        /// <c>ImGui.GetCursorScreenPos()</c> captured immediately BEFORE the row was drawn. No-op
        /// when the resolver is null, the key is empty, or the key does not resolve.
        /// </summary>
        public static void DrawIcon(MenuIconResolver? resolver, string? key, Vector2 rowStart, float gutterPx)
        {
            if (resolver == null || string.IsNullOrEmpty(key) || gutterPx <= 0f)
                return;
            if (!resolver(key!, out var tex, out var uv0, out var uv1) || tex == 0)
                return;

            float sz = ImGui.GetTextLineHeight();

            // Vertically center in the ACTUAL item rect (valid immediately after the item was
            // drawn). The main menu bar is intentionally taller than a text line with the label
            // centered, so pinning to rowStart.Y would float the icon above it; dropdown items
            // reduce to the normal item height and still center correctly.
            var rmin = ImGui.GetItemRectMin();
            var rmax = ImGui.GetItemRectMax();
            float cy = (rmin.Y + rmax.Y) * 0.5f - sz * 0.5f;

            var min = new Vector2(rowStart.X + (gutterPx - sz) * 0.5f, cy);
            ImGui.GetWindowDrawList().AddImage(tex, min, min + new Vector2(sz, sz), uv0, uv1);
        }

        /// <summary>True when any item at this menu level carries an icon — meaning the whole
        /// level should reserve the gutter so labels stay aligned.</summary>
        public static bool AnyHasIcon(IEnumerable<ContextMenuItemDto> items)
        {
            foreach (var i in items)
                if (!string.IsNullOrEmpty(i.Icon)) return true;
            return false;
        }
    }
}
