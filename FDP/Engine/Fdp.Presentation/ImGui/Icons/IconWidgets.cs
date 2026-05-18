using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace Fdp.Presentation.Icons;

/// <summary>
/// Static, stateless collection of immediate-mode icon-rendering widgets.
/// All methods accept an <see cref="IconAtlas"/> that provides the texture handle and UV lookup.
/// </summary>
/// <remarks>
/// Uses <c>global using Gui = ImGuiNET.ImGui</c> declared in <c>GlobalUsings.cs</c>.
/// Interactive widgets follow the <c>InvisibleButton + ImDrawList</c> pattern for
/// zero-GC, pixel-perfect rendering.
/// </remarks>
public static class IconWidgets
{
    // ─── WM-S102: Stateless rendering ────────────────────────────────────────

    /// <summary>
    /// Draws the icon at the current layout cursor position and calls
    /// <see cref="Gui.SameLine"/> so the next widget renders inline to the right.
    /// </summary>
    public static void InlineIcon(IconAtlas atlas, string coordinate)
    {
        var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
        Gui.Image(atlas.TextureId, atlas.IconSizeVec, uv0, uv1);
        Gui.SameLine();
    }

    /// <summary>
    /// Draws the icon via the window draw-list at an absolute screen position.
    /// Does <b>not</b> modify the layout cursor.
    /// </summary>
    public static void AbsoluteIcon(IconAtlas atlas, string coordinate, Vector2 screenPos)
    {
        var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
        var drawList = Gui.GetWindowDrawList();
        drawList.AddImage(atlas.TextureId, screenPos, screenPos + atlas.IconSizeVec, uv0, uv1);
    }

    // ─── WM-S103: Interactive widgets — IconButton and ToggleIcon ─────────────

    /// <summary>
    /// A stateless icon button. Returns <c>true</c> on the frame it is clicked.
    /// Delegates to <see cref="ToggleIcon"/> with a discarded local toggle state,
    /// so no filled background is ever drawn.
    /// </summary>
    /// <param name="tint">
    /// Optional RGBA color tint applied to the icon texture.
    /// Pass <c>null</c> (default) for full-white / no tint.
    /// </param>
    public static bool IconButton(IconAtlas atlas, string id, string coordinate, Vector4? tint = null)
    {
        bool dummy = false;
        return ToggleIcon(atlas, id, coordinate, ref dummy, tint);
    }

    /// <summary>
    /// A stateful icon button. Flips <paramref name="isToggled"/> on click and
    /// draws a gray filled background when toggled. Returns <c>true</c> on click.
    /// </summary>
    /// <param name="tint">
    /// Optional RGBA color tint applied to the icon texture.
    /// Pass <c>null</c> (default) for full-white / no tint.
    /// </param>
    public static bool ToggleIcon(IconAtlas atlas, string id, string coordinate, ref bool isToggled, Vector4? tint = null)
    {
        var screenPos = Gui.GetCursorScreenPos();
        var clicked = Gui.InvisibleButton(id, atlas.IconSizeVec);
        var isHovered = Gui.IsItemHovered();
        var isPressed = Gui.IsItemActive();

        var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
        var drawList = Gui.GetWindowDrawList();

        // Filled background only when toggled
        if (isToggled)
            drawList.AddRectFilled(
                screenPos,
                screenPos + atlas.IconSizeVec,
                Gui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)));

        // Icon image; shift 1px when pressed.
        // Resolve tint: null => full-white (no tint), otherwise convert to U32.
        var imagePos = isPressed ? screenPos + new Vector2(1f, 1f) : screenPos;
        uint imageTint = tint.HasValue ? Gui.GetColorU32(tint.Value) : 0xFFFFFFFF;
        drawList.AddImage(atlas.TextureId, imagePos, imagePos + atlas.IconSizeVec, uv0, uv1, imageTint);

        // Hover border
        if (isHovered)
            drawList.AddRect(
                screenPos,
                screenPos + atlas.IconSizeVec,
                Gui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));

        // Flip state on click
        if (clicked)
            isToggled = !isToggled;

        return clicked;
    }

    // ─── WM-S104: AlternatingFaceToggleIcon ───────────────────────────────────

    /// <summary>
    /// Like <see cref="ToggleIcon"/> but swaps the icon face based on state instead of drawing
    /// a filled background. The coordinate is evaluated <b>after</b> the click flip so the
    /// displayed face immediately reflects the new state.
    /// </summary>
    public static bool AlternatingFaceToggleIcon(
        IconAtlas atlas, string id,
        string trueCoordinate, string falseCoordinate,
        ref bool isToggled)
    {
        var screenPos = Gui.GetCursorScreenPos();
        var clicked = Gui.InvisibleButton(id, atlas.IconSizeVec);
        var isHovered = Gui.IsItemHovered();
        var isPressed = Gui.IsItemActive();

        var drawList = Gui.GetWindowDrawList();

        // Flip first so the coordinate selection reflects the new state
        if (clicked)
            isToggled = !isToggled;

        // Select coordinate after the flip
        var coordinate = isToggled ? trueCoordinate : falseCoordinate;
        var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);

        // No filled background (face change expresses state instead)
        var imagePos = isPressed ? screenPos + new Vector2(1f, 1f) : screenPos;
        drawList.AddImage(atlas.TextureId, imagePos, imagePos + atlas.IconSizeVec, uv0, uv1);

        if (isHovered)
            drawList.AddRect(
                screenPos,
                screenPos + atlas.IconSizeVec,
                Gui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));

        return clicked;
    }

    // ─── WM-S105: DropdownFaceIcon ────────────────────────────────────────────

    /// <summary>
    /// Renders the currently selected icon and opens a popup grid of all available icons
    /// when clicked. Sets <paramref name="selectedIndex"/> when the user picks a new icon
    /// and returns <c>true</c>. Out-of-range indices are clamped to 0.
    /// </summary>
    public static bool DropdownFaceIcon(
        IconAtlas atlas, string id,
        IReadOnlyList<string> availableCoordinates,
        ref int selectedIndex)
    {
        if (availableCoordinates.Count == 0)
            return false;

        // Safety clamp
        if (selectedIndex < 0 || selectedIndex >= availableCoordinates.Count)
            selectedIndex = 0;

        var screenPos = Gui.GetCursorScreenPos();
        var clicked = Gui.InvisibleButton(id, atlas.IconSizeVec);
        var isHovered = Gui.IsItemHovered();
        var isPressed = Gui.IsItemActive();

        var (uv0, uv1) = atlas.GetUvCoordinates(availableCoordinates[selectedIndex]);
        var drawList = Gui.GetWindowDrawList();

        var imagePos = isPressed ? screenPos + new Vector2(1f, 1f) : screenPos;
        drawList.AddImage(atlas.TextureId, imagePos, imagePos + atlas.IconSizeVec, uv0, uv1);

        if (isHovered)
            drawList.AddRect(
                screenPos,
                screenPos + atlas.IconSizeVec,
                Gui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));

        var popupId = $"{id}_popup";
        if (clicked)
            Gui.OpenPopup(popupId);

        bool result = false;
        if (Gui.BeginPopup(popupId))
        {
            const int iconsPerRow = 4;
            for (int i = 0; i < availableCoordinates.Count; i++)
            {
                if (i % iconsPerRow != 0)
                    Gui.SameLine();

                var (puv0, puv1) = atlas.GetUvCoordinates(availableCoordinates[i]);
                Gui.PushID(i);
                if (Gui.ImageButton("##icon", atlas.TextureId, atlas.IconSizeVec, puv0, puv1))
                {
                    selectedIndex = i;
                    Gui.CloseCurrentPopup();
                    result = true;
                }
                Gui.PopID();
            }

            Gui.EndPopup();
        }

        return result;
    }
}
