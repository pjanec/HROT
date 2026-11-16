using System;
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.WindowManager;

/// <summary>One member of a <see cref="ToolStripEntry"/> family: its glyph (own colour), tooltip and action.</summary>
public sealed record ToolStripMember(IconGlyph Glyph, string Tooltip, Action OnClick);

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
        float h = Gui.GetFrameHeight(), w = h;
        for (int i = 0; i < entries.Count; i++) { float ew = GlyphWidgets.WidthOf(entries[i].Glyph); if (ew > w) w = ew; }
        float spacing = Gui.GetStyle().ItemSpacing.X;
        LastOrientation = ToolStripLayout.Compute(entries.Count, new Vector2(w, h), spacing, Gui.GetContentRegionAvail(), _positions, out _);
        var origin = Gui.GetCursorPos();
        for (int i = 0; i < entries.Count; i++)
        {
            Gui.SetCursorPos(origin + _positions[i]);
            DrawEntry(entries[i]);
        }
    }

    private static void DrawEntry(ToolStripEntry e)
    {
        bool lit = e.IsLit?.Invoke() ?? false;
        if (e.Family is { Count: > 0 } fam)
        {
            int sel = Math.Clamp(e.Selected, 0, fam.Count - 1);
            if (GlyphWidgets.GlyphToggle(e.Id, fam[sel].Glyph, lit, fam[sel].Tooltip)) Gui.OpenPopup(e.MenuId);
            if (Gui.BeginPopup(e.MenuId))
            {
                for (int i = 0; i < fam.Count; i++)
                {
                    if (GlyphWidgets.GlyphToggle(e.Id, fam[i].Glyph, i == sel, fam[i].Tooltip)) { e.Selected = i; fam[i].OnClick(); Gui.CloseCurrentPopup(); }
                    if (i + 1 < fam.Count) Gui.SameLine();
                }
                Gui.EndPopup();
            }
        }
        else if (GlyphWidgets.GlyphToggle(e.Id, e.Glyph, lit, e.Tooltip)) e.OnClick?.Invoke();
    }
}
