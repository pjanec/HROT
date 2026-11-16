using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.Windows;

/// <summary>
/// ⭐ CE-5006 W2 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-19) — the main toolbar's two entries for the editor's tool strips: <i>Toolbars</i> (sort order 95: a split icon — the face toggles the strip toggled last, lit while ANY strip is open;
/// the ▾ lists every strip with a ✓ on the open ones) and <i>Tactical Map Layers</i> (96: a toggle that opens / closes the layer editor, lit while it is shown).
/// </summary>
internal sealed class EditorToolbarsSection
{
    internal const string ToolbarsEntryId = "EditorToolbars", LayersEntryId = "EditorMapLayers";
    internal const int ToolbarsSortOrder = 95, LayersSortOrder = 96;

    private static readonly IconGlyph ToolbarsGlyph = new("", IconPalette.Teal);   // ListCheck: the tool strips
    private static readonly IconGlyph LayersGlyph = new("", IconPalette.Blue);     // LayerGroup

    private readonly IReadOnlyList<ToolStripWindow> _strips;
    private readonly Func<bool> _layersShown;
    private readonly Action _toggleLayers;
    private int _last;

    public EditorToolbarsSection(IReadOnlyList<ToolStripWindow> strips, Func<bool> layersShown, Action toggleLayers)
    { _strips = strips; _layersShown = layersShown; _toggleLayers = toggleLayers; }

    /// <summary>Any strip is open — the face is lit.</summary>
    public bool AnyOpen { get { foreach (var w in _strips) if (w.IsOpen) return true; return false; } }

    /// <summary>The strip the face toggles (the one toggled last; the first at the start).</summary>
    public ToolStripWindow Last => _strips[_last];

    /// <summary>The ▾ list: one line per strip, ✓ = open.</summary>
    public IReadOnlyList<SplitItem> Items()
    {
        var l = new List<SplitItem>(_strips.Count);
        foreach (var w in _strips) l.Add(new SplitItem(w.Title, w.IsOpen));
        return l;
    }

    /// <summary>Toggles strip <paramref name="index"/> and remembers it as the face's.</summary>
    public void Toggle(int index)
    {
        if (index < 0 || index >= _strips.Count) return;
        _last = index;
        _strips[index].IsOpen = !_strips[index].IsOpen;
    }

    /// <summary>The face was clicked: toggles the strip toggled last.</summary>
    public void ToggleLast() => Toggle(_last);

    /// <summary>Whether the layer editor is shown (the layers icon is lit).</summary>
    public bool LayersShown => _layersShown();

    /// <summary>The layers icon was clicked.</summary>
    public void ToggleLayers() => _toggleLayers();

    /// <summary>Draws the Toolbars split icon (face + ▾).</summary>
    public void DrawToolbars()
    {
        if (GlyphWidgets.GlyphToggle("##tb_toolbars", ToolbarsGlyph, AnyOpen, "Toolbars — show / hide " + Last.Title)) ToggleLast();
        if (!SplitArrow.Begin("##tb_toolbars_arrow", "##tb_toolbars_menu", "Choose a toolbar")) return;
        for (int i = 0; i < _strips.Count; i++)
            if (SplitArrow.Item(_strips[i].Title, _strips[i].IsOpen)) Toggle(i);
        SplitArrow.End();
    }

    /// <summary>Draws the Tactical Map Layers toggle.</summary>
    public void DrawLayers()
    {
        if (GlyphWidgets.GlyphToggle("##tb_layers", LayersGlyph, LayersShown, "Tactical map layers")) ToggleLayers();
    }
}
