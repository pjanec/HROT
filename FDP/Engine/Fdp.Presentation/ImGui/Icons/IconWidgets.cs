using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

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

    // ─── MTB2-T1: Geometry helper ─────────────────────────────────────────────

    /// <summary>
    /// Default icon scale applied by the <see cref="IconHandle"/> overloads
    /// when <c>iconScale</c> is omitted. 0.9 means the icon image occupies
    /// ~72 % of the hit/spacing box, centered with clear breathing room (~14 % each side).
    /// </summary>
    public const float DefaultIconScale = 0.72f;

    /// <summary>
    /// Returns the centered sub-rect at <paramref name="scale"/> of the box.
    /// </summary>
    /// <param name="boxPos">Top-left corner of the layout/hit box.</param>
    /// <param name="boxSize">Full layout/hit box size.</param>
    /// <param name="scale">
    /// Fraction of the box the icon should occupy (1.0 = full box, 0.9 = 90 % centered).
    /// </param>
    /// <returns>A tuple (<c>Min</c>, <c>Max</c>) defining the centered inset rectangle.</returns>
    public static (Vector2 Min, Vector2 Max) ComputeIconRect(Vector2 boxPos, Vector2 boxSize, float scale)
    {
        Vector2 insetSize = boxSize * scale;
        Vector2 margin = (boxSize - insetSize) * 0.5f;
        Vector2 min = boxPos + margin;
        return (min, min + insetSize);
    }

    // ─── MTB-P1-T2: IconHandle-based overloads with explicit size ──────────────

    /// <summary>
    /// A stateless icon button that draws from an <see cref="IconHandle"/> at an explicit size.
    /// Returns <c>true</c> on the frame it is clicked.
    /// Delegates to <see cref="ToggleIcon(in IconHandle, string, Vector2, ref bool, bool, Vector4?, float)"/>
    /// with a discarded local toggle state, so no filled background is ever drawn.
    /// </summary>
    /// <param name="icon">Resolved icon handle from an <see cref="IIconProvider"/>.</param>
    /// <param name="id">Unique ImGui ID for this button.</param>
    /// <param name="size">Render size in pixels (typically 64×64 for toolbar icons).</param>
    /// <param name="enabled">
    /// When <c>false</c> the icon is drawn dimmed with no click hit-area
    /// and this method always returns <c>false</c>.
    /// </param>
    /// <param name="tint">
    /// Optional RGBA color tint applied to the icon texture.
    /// Pass <c>null</c> (default) for full-white / no tint.
    /// </param>
    /// <param name="iconScale">
    /// Fraction of the hit/spacing box the icon image occupies (default <see cref="DefaultIconScale"/> = 0.9).
    /// The hit/spacing <see cref="Gui.InvisibleButton"/> always uses the full <paramref name="size"/>.
    /// </param>
    public static bool IconButton(in IconHandle icon, string id, Vector2 size,
                                  bool enabled = true, Vector4? tint = null, float iconScale = DefaultIconScale)
    {
        bool dummy = false;
        return ToggleIcon(in icon, id, size, ref dummy, enabled, tint, iconScale);
    }

    /// <summary>
    /// A stateful icon button that draws from an <see cref="IconHandle"/> at an explicit size.
    /// Flips <paramref name="isToggled"/> on click and draws a filled background when
    /// toggled or hovered. Returns <c>true</c> on click.
    /// </summary>
    /// <param name="icon">Resolved icon handle from an <see cref="IIconProvider"/>.</param>
    /// <param name="id">Unique ImGui ID for this button.</param>
    /// <param name="size">Render size in pixels.</param>
    /// <param name="isToggled">Toggle state reference — flipped on click.</param>
    /// <param name="enabled">
    /// When <c>false</c> the icon is drawn dimmed via a passive <see cref="Gui.Dummy"/>
    /// (no click hit-area), the toggle state is NOT changed, and this method always
    /// returns <c>false</c>.
    /// </param>
    /// <param name="tint">
    /// Optional RGBA color tint applied to the icon texture.
    /// Pass <c>null</c> (default) for full-white / no tint.
    /// </param>
    /// <param name="iconScale">
    /// Fraction of the hit/spacing box the icon image occupies (default <see cref="DefaultIconScale"/> = 0.9).
    /// The hit/spacing <see cref="Gui.InvisibleButton"/> always uses the full <paramref name="size"/>.
    /// </param>
    public static bool ToggleIcon(in IconHandle icon, string id, Vector2 size,
                                  ref bool isToggled, bool enabled = true, Vector4? tint = null,
                                  float iconScale = DefaultIconScale)
    {
        var screenPos = Gui.GetCursorScreenPos();

        bool clicked;
        bool isHovered;
        bool isPressed;

        if (enabled)
        {
            clicked = Gui.InvisibleButton(id, size);
            isHovered = Gui.IsItemHovered();
            isPressed = Gui.IsItemActive();
        }
        else
        {
            // Passive placeholder — no hit area, no interaction
            Gui.Dummy(size);
            clicked = false;
            isHovered = false;
            isPressed = false;
        }

        var drawList = Gui.GetWindowDrawList();

        // Compute the inset icon rect (image drawn here; hit/spacing box unchanged).
        var (iconMin, iconMax) = ComputeIconRect(screenPos, size, iconScale);
        var iconSize = iconMax - iconMin;

        // ── Toggled-state fill (only when enabled) ───────────────────────────
        // A clearly-visible accent fill marks the "active"/toggled state. The hover
        // indicator is a SEPARATE white frame (drawn after the icon, below) so that a
        // toggled icon that is ALSO hovered shows BOTH cues.
        if (enabled && isToggled)
        {
            var toggleCol = Gui.GetStyle().Colors[(int)ImGuiCol.HeaderActive];
            toggleCol.W = 0.85f; // strong, unmistakable "active" fill
            drawList.AddRectFilled(screenPos, screenPos + size, Gui.GetColorU32(toggleCol), 2f);
        }

        // Icon image; shift 1px when pressed.
        // Enabled: normal tint (null → full-white). Disabled: dimmed alpha (~0.28f, mirroring TransportIconRenderer).
        var imagePos = isPressed ? iconMin + new Vector2(1f, 1f) : iconMin;
        uint imageTint;
        if (!enabled)
        {
            // Dimmed draw — reduced alpha on the tint
            Vector4 dim = tint ?? Vector4.One;
            dim.W *= 0.28f;
            imageTint = Gui.GetColorU32(dim);
        }
        else if (tint.HasValue)
        {
            imageTint = Gui.GetColorU32(tint.Value);
        }
        else
        {
            imageTint = 0xFFFFFFFF; // full-white
        }

        drawList.AddImage(icon.TextureId, imagePos, imagePos + iconSize, icon.Uv0, icon.Uv1, imageTint);

        // ── Hover frame (independent of toggle) ──────────────────────────────
        // A white border around the full box, drawn on top of everything — the clear,
        // toggle-independent hover indicator. Composes with the toggled fill above.
        if (enabled && isHovered)
        {
            drawList.AddRect(screenPos, screenPos + size,
                Gui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)), 2f);
        }

        // Flip state on click (only when enabled)
        if (clicked)
            isToggled = !isToggled;

        return clicked;
    }

    // ─── MTB-P1-T2: Tooltip helper ───────────────────────────────────────────

    /// <summary>
    /// Simple tooltip helper. If the most recently submitted item is hovered,
    /// calls <see cref="Gui.SetTooltip"/> with <paramref name="text"/>.
    /// Call immediately after an interactive widget (button, toggle, etc.).
    /// </summary>
    public static void Tooltip(string text)
    {
        if (Gui.IsItemHovered())
            Gui.SetTooltip(text);
    }
}
