using System.Numerics;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// For RAILS only (like <see cref="FormGrid.ColumnWidthOf"/>): the screen rectangle of the kit's inner clickables as last drawn — popup menu rows, picker rows, a list row's handle / body / ⋮ — so a frame rail can click
/// them (the popups' windows are not reachable through ImGui.NET). A fixed array of 64 slots written with two <c>Vector2</c> stores: nothing is allocated, nothing in the product reads it.
/// </summary>
public static class KitProbe
{
    /// <summary>ListBlock's ⋮ menu rows: slot = the row's index (0 … 8).</summary>
    public const int MenuRow = 0;
    /// <summary>ListBlock's <i>Add ▾</i> kinds popup: slot = 16 + kind index.</summary>
    public const int KindRow = 16;
    /// <summary>EntityField's candidate rows: slot = 32 + row (first 8).</summary>
    public const int CandidateRow = 32;
    /// <summary>EntityField's entries: 48 = Pick on the map · 49 = By key · 50 = Clear · 51 = the search box.</summary>
    public const int Entry = 48;
    /// <summary>The LAST list row drawn by <c>ListBlock.ItemBegin</c>: 56 = handle · 57 = body · 58 = ⋮ button.</summary>
    public const int ListHandle = 56, ListBody = 57, ListMenuButton = 58;
    /// <summary>ChipRow: slots 40 … 46 = the first seven chips · 47 = the ⊕.</summary>
    public const int Chip = 40, ChipAdd = 47;
    /// <summary>The long list's header: 59 = filter box · 60 = ‹ · 61 = › · 62 = the ▸/▾ fold button.</summary>
    public const int HeaderFilter = 59, HeaderPrev = 60, HeaderNext = 61, HeaderFold = 62;

    /// <summary>The struct table's cells: 64 = the last text cell · 65 … 72 = flag chips · 73 = the nullable's set box · 74 = the last ↺ · 75 = the last show-on-map glyph.</summary>
    public const int TextCell = 64, FlagChip = 65, SetBox = 73, ResetMark = 74, ShowOnMap = 75;

    /// <summary>76 = the last entity field's chip (closed).</summary>
    public const int EntityChip = 76;

    private static readonly Vector4[] Rects = new Vector4[80];
    private static readonly bool[] Has = new bool[80];

    public static void Set(int slot, Vector2 min, Vector2 max) { Rects[slot] = new Vector4(min.X, min.Y, max.X, max.Y); Has[slot] = true; }

    public static bool TryGet(int slot, out Vector2 min, out Vector2 max)
    {
        var r = Rects[slot];
        min = new Vector2(r.X, r.Y); max = new Vector2(r.Z, r.W);
        return Has[slot];
    }

    public static Vector2 Center(int slot) => TryGet(slot, out var a, out var b) ? (a + b) * 0.5f : new Vector2(float.NaN, float.NaN);
}
