using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.WindowManager;

/// <summary>One member of a <see cref="ToolStripEntry"/> family: its glyph (own colour), the label of its popup row, tooltip and action.</summary>
public sealed record ToolStripMember(IconGlyph Glyph, string Label, string Tooltip, Action OnClick);

/// <summary>
/// ⭐ CE-5006 W1 — one icon of a <see cref="ToolStripWindow"/>: a coloured glyph (docs/UX/DESIGN_Colour_Icons.md — the glyph wears its colour; lit = framed and full strength), a tooltip, whether it is lit, what a click does.
/// With a <see cref="Family"/> the entry is a dropdown: it shows the chosen member's glyph, a click opens a popup of the members, picking one makes it the face and runs it.
/// </summary>
public sealed class ToolStripEntry
{
    /// <summary>A stable ImGui id for the button (the caller's constant, e.g. <c>"##ts_wall"</c>).</summary>
    public string Id { get; }
    /// <summary>The id of a dropdown entry's popup (built once).</summary>
    public string MenuId { get; }
    public IconGlyph Glyph { get; }
    public string Tooltip { get; }
    public Func<bool>? IsLit { get; init; }
    public Action? OnClick { get; init; }
    /// <summary>The members of a dropdown entry; null for a plain one.</summary>
    public IReadOnlyList<ToolStripMember>? Family { get; init; }
    /// <summary>The chosen member of <see cref="Family"/>.</summary>
    public int Selected { get; set; }

    /// <summary>A family row was picked: it becomes the face AND runs.</summary>
    public void Pick(int index)
    {
        if (Family is null || index < 0 || index >= Family.Count) return;
        Selected = index;
        Family[index].OnClick();
    }

    public ToolStripEntry(string id, IconGlyph glyph, string tooltip) { Id = id; MenuId = id + "_menu"; Glyph = glyph; Tooltip = tooltip; }
}

/// <summary>
/// ⭐ CE-5006 W1 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-19, R-5038) — the SHARED dockable tool strip: a list of <see cref="ToolStripEntry"/> laid out by its OWN client size each frame (<see cref="ToolStripLayout"/>: tall ⇒ a column,
/// wide ⇒ a row, wrapping). Nothing editor-specific: a host subclasses it and supplies <see cref="Entries"/>. The steady frame allocates nothing (the position buffer only grows).
/// </summary>
public abstract class ToolStripWindow : ManagedWindow
{
    private Vector2[] _positions = new Vector2[16];

    protected ToolStripWindow(string id, string title, string perspective, WindowScope scope) : base(id, title, perspective, scope) { }

    /// <summary>The icons, in order. Read every frame — return a stable list.</summary>
    protected abstract IReadOnlyList<ToolStripEntry> Entries { get; }

    /// <summary>The orientation chosen on the last drawn frame (for tests and hosts).</summary>
    public ToolStripOrientation LastOrientation { get; private set; }

    protected override void DrawClientArea()
    {
        var entries = Entries;
        if (entries.Count > _positions.Length) _positions = new Vector2[Math.Max(entries.Count, _positions.Length * 2)];
        float scale = Gui.GetFontSize() / 14f;   // ⭐ the tile is the mockup's 30 × 26 px at the default font size
        var tile = new Vector2(TileWidth * scale, TileHeight * scale);
        float spacing = Gui.GetStyle().ItemSpacing.X;
        LastOrientation = ToolStripLayout.Compute(entries.Count, tile, spacing, Gui.GetContentRegionAvail(), _positions, out _);
        var origin = Gui.GetCursorPos();
        for (int i = 0; i < entries.Count; i++)
        {
            Gui.SetCursorPos(origin + _positions[i]);
            DrawEntry(entries[i], tile, scale);
        }
    }

    /// <summary>The pin does not fit beside a narrow strip's title.</summary>
    protected override bool ShowPinButton => false;

    /// <summary>⭐ The tile of the mockup's <c>glyph_toggle</c> (docs/UX/terrain-editor.svg panel A, scripts/mockup_kit.py): 30 × 26 at the default font size.</summary>
    internal const float TileWidth = 30f, TileHeight = 26f, GlyphPx = 15f;

    private static readonly Vector4 Neutral = Rgb(0x1C, 0x1C, 0x22), NeutralLit = Rgb(0x34, 0x34, 0x3E), Border = Rgb(0x2E, 0x2E, 0x36), BorderLit = Rgb(0xC8, 0xC8, 0xD4);
    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    /// <summary>
    /// One framed tile like <c>glyph_toggle</c>: the neutral fill and a dim border, the glyph in its own colour at 65 % — LIT is the lighter fill, a 1.4 px light border and the glyph at full strength. A dropdown gets a tiny ▾ in its bottom-right corner.
    /// Returns true on a click. Allocates nothing.
    /// </summary>
    private static bool Tile(string id, in IconGlyph glyph, bool lit, bool hasMenu, string tooltip, Vector2 size, float scale)
        => Tile(id, glyph, lit, hasMenu, tooltip, size, scale, out _);

    /// <summary>⭐ <c>CE-1965</c> — the share of a dropdown tile (from its right and its bottom edge) that is its ▾: a click there opens the menu; anywhere
    /// else on the tile runs the member chosen last (a SPLIT button). 🔒 User, 2026-11-17: "when I click the arrow part of the split icon it will open the
    /// menu, and whatever I click in the menu it will be remembered for the next click of the icon".</summary>
    public const float ArrowZone = 0.42f;

    /// <summary>⭐ CE-1965 — is <paramref name="local"/> (from the tile's top-left) on the ▾ of a tile of <paramref name="size"/>?</summary>
    public static bool OnArrow(Vector2 local, Vector2 size) => local.X >= size.X * (1f - ArrowZone) && local.Y >= size.Y * (1f - ArrowZone);

    private static bool Tile(string id, in IconGlyph glyph, bool lit, bool hasMenu, string tooltip, Vector2 size, float scale, out bool menu)
    {
        var pos = Gui.GetCursorScreenPos();
        bool clicked = Gui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = Gui.IsItemHovered();
        // ⭐ CE-1965 — a dropdown: the ▾ corner (or a right-click) opens the menu; the face runs the chosen member
        bool right = Gui.IsMouseReleased(ImGuiMouseButton.Right);
        menu = clicked && hasMenu && (right || OnArrow(Gui.GetIO().MousePos - pos, size));
        if (clicked && !hasMenu && right) clicked = false;   // a plain tile: the left button only, as before
        var dl = Gui.GetWindowDrawList();
        float rounding = 4f * scale;
        dl.AddRectFilled(pos, pos + size, Gui.GetColorU32(lit ? NeutralLit : Neutral), rounding);
        dl.AddRect(pos, pos + size, Gui.GetColorU32(lit ? BorderLit : Border), rounding, ImDrawFlags.None, lit ? 1.4f * scale : 1f);
        var c = glyph.Color; if (!lit) c.W *= 0.65f;
        float px = GlyphPx * scale;
        float gw = Gui.CalcTextSize(glyph.Glyph).X * (px / Gui.GetFontSize());
        dl.AddText(Gui.GetFont(), px, new Vector2(pos.X + (size.X - gw) * 0.5f, pos.Y + (size.Y - px) * 0.5f), Gui.GetColorU32(c), glyph.Glyph);
        if (hasMenu)
        {
            // ⭐ CE-1965 — the ▾ zone reads as a part of its own: a thin divider, lit while the mouse is over it
            var corner = pos + size * (1f - ArrowZone);
            bool overArrow = hovered && OnArrow(Gui.GetIO().MousePos - pos, size);
            if (overArrow) dl.AddRectFilled(corner, pos + size, Gui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), rounding);
            dl.AddLine(new Vector2(corner.X, corner.Y), new Vector2(corner.X, pos.Y + size.Y - 2f * scale), Gui.GetColorU32(lit ? BorderLit : Border), 1f);
            float m = 3.5f * scale; var br = pos + size - new Vector2(3f * scale, 3f * scale);
            dl.AddTriangleFilled(br + new Vector2(-2f * m, -m), br + new Vector2(0f, -m), br + new Vector2(-m, 0f), Gui.GetColorU32(new Vector4(0.78f, 0.78f, 0.83f, 0.9f)));
        }
        IconButtonChrome.DrawHoverFrame(dl, pos, size, hovered);
        if (hovered && tooltip.Length > 0) Gui.SetTooltip(tooltip);
        return clicked;
    }

    private static void DrawEntry(ToolStripEntry e, Vector2 tile, float scale)
    {
        bool lit = e.IsLit?.Invoke() ?? false;
        if (e.Family is { Count: > 0 } fam)
        {
            int sel = Math.Clamp(e.Selected, 0, fam.Count - 1);
            // ⭐ CE-1965 — a SPLIT button: the ▾ (or a right-click) opens the members; the face runs the one chosen last
            if (Tile(e.Id, fam[sel].Glyph, lit, true, fam[sel].Tooltip, tile, scale, out bool menu))
            {
                if (menu) Gui.OpenPopup(e.MenuId);
                else e.Pick(sel);
            }
            if (Gui.BeginPopup(e.MenuId))
            {
                // ⭐ a VERTICAL list: each row the member's glyph in its own colour, its label, a ✓ on the chosen one; each row has its own id scope (no clash with the face)
                for (int i = 0; i < fam.Count; i++)
                {
                    Gui.PushID(i);
                    Gui.TextColored(fam[i].Glyph.Color, fam[i].Glyph.Glyph);
                    Gui.SameLine();
                    bool picked = Gui.MenuItem(fam[i].Label, null, i == sel);
                    if (Gui.IsItemHovered() && fam[i].Tooltip.Length > 0) Gui.SetTooltip(fam[i].Tooltip);
                    Gui.PopID();
                    if (picked) e.Pick(i);
                }
                Gui.EndPopup();
            }
        }
        else if (Tile(e.Id, e.Glyph, lit, false, e.Tooltip, tile, scale)) e.OnClick?.Invoke();
    }
}
