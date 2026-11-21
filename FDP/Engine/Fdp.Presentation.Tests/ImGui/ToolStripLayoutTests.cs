using System;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Xunit;

namespace Fdp.Presentation.Tests;

/// <summary>⭐ CE-5006 W1 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-19) — the pure layout of the shared tool strip: a row when wide, a column when tall, wrapping, no allocation.</summary>
public class ToolStripLayoutTests
{
    private static readonly Vector2 Cell = new(20, 20);

    /// <summary>Wide ⇒ a ROW: icons run along x at cell + spacing. 🔴 Mutation: flip the <c>available.Y &gt; available.X</c> test ⇒ a column.</summary>
    [Fact]
    public void Wide_IsARow()
    {
        var pos = new Vector2[5];
        var o = ToolStripLayout.Compute(5, Cell, 4f, new Vector2(200, 30), pos, out var extent);
        Assert.Equal(ToolStripOrientation.Row, o);
        Assert.Equal(new Vector2(0, 0), pos[0]); Assert.Equal(new Vector2(24, 0), pos[1]); Assert.Equal(new Vector2(96, 0), pos[4]);
        Assert.Equal(new Vector2(116, 20), extent);
    }

    /// <summary>Tall ⇒ a COLUMN: icons run along y. 🔴 Mutation: swap x and y when placing a column cell ⇒ red.</summary>
    [Fact]
    public void Tall_IsAColumn()
    {
        var pos = new Vector2[5];
        var o = ToolStripLayout.Compute(5, Cell, 4f, new Vector2(30, 200), pos, out var extent);
        Assert.Equal(ToolStripOrientation.Column, o);
        Assert.Equal(new Vector2(0, 24), pos[1]); Assert.Equal(new Vector2(0, 96), pos[4]);
        Assert.Equal(new Vector2(20, 116), extent);
    }

    /// <summary>⭐ CE-1965 — each cell its OWN width (a split button: face + ▾ bar, 35 wide here): a ROW steps by each width; a COLUMN is as wide as its widest
    /// cell and the next column starts after it. 🔴 Mutation: step by <c>cell.X</c> again ⇒ the third icon overlaps the split.</summary>
    [Fact]
    public void CellsOfTheirOwnWidth_StackInRowsAndColumns()
    {
        var pos = new Vector2[3];
        ReadOnlySpan<float> widths = stackalloc float[] { 20f, 35f, 20f };
        ToolStripLayout.Compute(3, Cell, 4f, new Vector2(200, 30), pos, out var row, widths);
        Assert.Equal(new Vector2(24, 0), pos[1]); Assert.Equal(new Vector2(63, 0), pos[2]);     // 24 + 35 + 4
        Assert.Equal(new Vector2(83, 20), row);
        ToolStripLayout.Compute(3, Cell, 4f, new Vector2(40, 50), pos, out var col, widths);    // tall: two fit in a column of 50
        Assert.Equal(new Vector2(0, 24), pos[1]); Assert.Equal(new Vector2(39, 0), pos[2]);     // the next column after the widest (35 + 4)
        Assert.Equal(new Vector2(59, 44), col);
    }

    /// <summary>Out of room ⇒ it WRAPS: a row of width 70 holds three icons (0, 24, 48) and the fourth starts the next row; a column wraps to the next column the same way. 🔴 Mutation: never wrap ⇒ red.</summary>
    [Fact]
    public void RunningOutOfRoom_Wraps_InBothOrientations()
    {
        var pos = new Vector2[5];
        ToolStripLayout.Compute(5, Cell, 4f, new Vector2(70, 40), pos, out var extent);   // wider than tall ⇒ row
        Assert.Equal(new Vector2(48, 0), pos[2]); Assert.Equal(new Vector2(0, 24), pos[3]); Assert.Equal(new Vector2(24, 24), pos[4]);
        Assert.Equal(new Vector2(68, 44), extent);
        ToolStripLayout.Compute(5, Cell, 4f, new Vector2(40, 70), pos, out extent);       // taller than wide ⇒ column
        Assert.Equal(new Vector2(0, 48), pos[2]); Assert.Equal(new Vector2(24, 0), pos[3]); Assert.Equal(new Vector2(24, 24), pos[4]);
    }

    /// <summary>However narrow, one icon sits on every line (no endless wrapping, no empty lines); no entries is an empty extent.</summary>
    [Fact]
    public void AtLeastOneIconPerLine_AndNothingIsNothing()
    {
        var pos = new Vector2[3];
        ToolStripLayout.Compute(3, Cell, 4f, new Vector2(5, 3), pos, out _);   // wider than tall, but narrower than one cell
        Assert.Equal(new Vector2(0, 0), pos[0]); Assert.Equal(new Vector2(0, 24), pos[1]); Assert.Equal(new Vector2(0, 48), pos[2]);
        ToolStripLayout.Compute(0, Cell, 4f, new Vector2(100, 100), pos, out var extent);
        Assert.Equal(Vector2.Zero, extent);
    }

    /// <summary>A steady layout call allocates nothing. 🔴 Mutation: allocate a <c>new Vector2[count]</c> inside <c>Compute</c> ⇒ red.</summary>
    [Fact]
    public void ASteadyLayout_AllocatesNothing()
    {
        var pos = new Vector2[64];
        ToolStripLayout.Compute(40, Cell, 4f, new Vector2(300, 80), pos, out _);   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) ToolStripLayout.Compute(40, Cell, 4f, new Vector2(300, 80), pos, out _);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
