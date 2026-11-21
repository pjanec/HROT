using System;
using System.Numerics;

namespace Fdp.Presentation.WindowManager;

/// <summary>How a tool strip lays its icons out.</summary>
public enum ToolStripOrientation { Row, Column }

/// <summary>
/// ⭐ CE-5006 W1 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-19, R-5038) — the PURE layout of a tool strip: taller than wide ⇒ a COLUMN, otherwise a ROW; it wraps to the next row / column when the next icon would pass the available size.
/// No ImGui, no allocation — the caller owns the position buffer.
/// </summary>
public static class ToolStripLayout
{
    /// <summary>
    /// Fills <paramref name="positions"/>[0..count) with each cell's top-left offset from the strip's origin and returns the orientation. <paramref name="cell"/> is one icon's box, <paramref name="spacing"/> the gap between boxes,
    /// <paramref name="available"/> the client size. At least one icon sits on every line, however narrow. <paramref name="extent"/> is the size the placed cells cover.
    /// </summary>
    public static ToolStripOrientation Compute(int count, Vector2 cell, float spacing, Vector2 available, Span<Vector2> positions, out Vector2 extent)
        => Compute(count, cell, spacing, available, positions, out extent, default);

    /// <summary>
    /// ⭐ <c>CE-1965</c> — the same, each cell with its OWN width (<paramref name="widths"/>[i]; empty ⇒ all <paramref name="cell"/>.X): a split button (a square face
    /// and its ▾ bar) is wider than a plain icon. A ROW steps by each cell's width; a COLUMN is as wide as its widest cell (the others sit at its left edge).
    /// </summary>
    public static ToolStripOrientation Compute(int count, Vector2 cell, float spacing, Vector2 available, Span<Vector2> positions, out Vector2 extent, ReadOnlySpan<float> widths)
    {
        var orientation = available.Y > available.X ? ToolStripOrientation.Column : ToolStripOrientation.Row;
        bool column = orientation == ToolStripOrientation.Column;
        float along = 0f, across = 0f, maxAlong = 0f, lineAcross = 0f;   // lineAcross: this line's thickness across (a column's widest cell; a row's height)
        float room = column ? available.Y : available.X;
        for (int i = 0; i < count; i++)
        {
            float w = i < widths.Length && widths[i] > 0f ? widths[i] : cell.X;
            float cellAlong = column ? cell.Y : w, cellAcross = column ? w : cell.Y;
            if (along > 0f && along + cellAlong > room) { along = 0f; across += lineAcross + spacing; lineAcross = 0f; }
            positions[i] = column ? new Vector2(across, along) : new Vector2(along, across);
            along += cellAlong + spacing;
            if (along - spacing > maxAlong) maxAlong = along - spacing;
            if (cellAcross > lineAcross) lineAcross = cellAcross;
        }
        float acrossUsed = count == 0 ? 0f : across + lineAcross;
        extent = count == 0 ? Vector2.Zero : column ? new Vector2(acrossUsed, maxAlong) : new Vector2(maxAlong, acrossUsed);
        return orientation;
    }
}
