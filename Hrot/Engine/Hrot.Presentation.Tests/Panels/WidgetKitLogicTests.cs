using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Editing;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>The kit's non-ImGui logic (docs/DESIGN_Struct_Editor.md §8 / §10.1): the open flags that travel with their items, the op → state mapping, and the picker's filter. Pure — runs without a display.</summary>
public sealed class WidgetKitLogicTests
{
    private static OpenSet Flags(params int[] open)
    {
        var s = new OpenSet();
        foreach (var i in open) s.Set(i, true);
        return s;
    }

    private static string Show(OpenSet s, int n)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(s.Get(i) ? 'O' : '.');
        return sb.ToString();
    }

    [Fact]
    public void Move_TheOpenFlagTravelsWithItsItem_BothDirections()
    {
        var s = Flags(1);                      // . O . .
        s.Move(1, 2);                          // item 1 goes down one
        Assert.Equal("..O.", Show(s, 4));
        s.Move(2, 0);                          // and up two: the others shift
        Assert.Equal("O...", Show(s, 4));
        var t = Flags(0, 3);                   // O . . O
        t.Move(0, 3);                          // first to last: the old last is now third
        Assert.Equal("..OO", Show(t, 4));
    }

    [Fact]
    public void InsertAndRemove_ShiftTheFlagsAfterThePoint()
    {
        var s = Flags(0, 2);                   // O . O
        s.Insert(1);                           // a new closed item at 1
        Assert.Equal("O..O", Show(s, 4));
        s.RemoveAt(0);                         // the open first item is gone
        Assert.Equal("..O.", Show(s, 4));
        Assert.False(s.Get(99));
    }

    [Theory]
    [InlineData(ListOpKind.InsertAbove, 1, -1, "O..O")]
    [InlineData(ListOpKind.InsertBelow, 1, -1, "O..O")]   // the new closed slot is 2; the old item 2 shifts to 3
    [InlineData(ListOpKind.Duplicate, 0, -1, "O..O")]      // the copy of item 0 is a closed item at 1; the old item 2 shifts to 3
    [InlineData(ListOpKind.MoveDown, 0, 1, ".OO.")]
    [InlineData(ListOpKind.MoveUp, 2, 1, "OO..")]
    [InlineData(ListOpKind.Move, 0, 2, ".OO.")]
    [InlineData(ListOpKind.Delete, 0, -1, ".O..")]
    [InlineData(ListOpKind.Add, 3, 0, "O.O.")]
    public void ApplyToState_EveryOpKeepsEachFlagWithItsItem(ListOpKind kind, int index, int target, string expected)
    {
        var s = Flags(0, 2);                   // O . O .
        ListBlock.ApplyToState(s, new ListOp(kind, index, target));
        Assert.Equal(expected, Show(s, 4));
    }

    [Fact]
    public void EntityFieldFilter_IsACaseInsensitiveSubstringOfTheLabel_InCandidateOrder()
    {
        var c = new List<EntityCandidate>
        {
            new("Alpha 1-1", default, default, "Friend", "90 m", 1),
            new("Bravo 2-3", default, default, "Hostile", "210 m", 2),
            new("brava supply", default, default, "Friend", "1.2 km", 3),
        };
        var into = new int[8];
        Assert.Equal(3, EntityField.Filter(c, "", into));
        int n = EntityField.Filter(c, "BRA", into);
        Assert.Equal(2, n);
        Assert.Equal(1, into[0]); Assert.Equal(2, into[1]);
        Assert.Equal(0, EntityField.Filter(c, "zzz", into));
    }
}
