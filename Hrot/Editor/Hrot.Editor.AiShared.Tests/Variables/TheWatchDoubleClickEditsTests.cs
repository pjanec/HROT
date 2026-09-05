using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐ <b>A double-click raises the gesture the HOST OFFERS.</b>
///
/// <para>🔴 <i>"double click on Watch row opens Properties which was removed from context menu; users do
/// not want any properties on Watch. double click should open the edit variable."</i>
/// *(user, <c>2026-08-20</c>)*. 📐 Batch 100's <c>100f</c> made the gesture set host-declared and gated
/// the MENU — ⛔ <b>the double-click was a SECOND entry point and was not gated.</b></para>
///
/// <para>⚠ <b>Same shape as <c>BP-360</c></b> *(the outline's Watch entry: one command, two entry
/// points, one of them wired)* ⇒ ⭐ this rail exists so the class does not recur a third time.</para>
/// </summary>
public sealed class TheWatchDoubleClickEditsTests
{
    private static VariableRow Row() => new(
        Origin:    new VariableRowOrigin(System.Guid.NewGuid(), default, "Variables", "Count", "Bp"),
        ShortName: "Count", TypeText: "int", ClrType: typeof(int),
        ReadValue: () => System.Array.Empty<byte>());

    private static VariableTableControl Control(VariableTableGestures gestures)
        => new(new VariableValueFormatter(RawValueDecoder.Instance)) { Gestures = gestures };

    /// <summary>⭐⭐ The Watch offers no Properties ⇒ a double-click EDITS. ⛔ Never nothing.</summary>
    [Fact]
    public void OnTheWatchADoubleClickOpensTheValueEditor()
        => Assert.Equal(VariableEditAction.EditValue,
                        Control(VariableTableGestures.Watch).RaiseNameCellDoubleClick(Row()));

    /// <summary>⭐ Everywhere else the NAME cell keeps its documented meaning.</summary>
    [Fact]
    public void ElsewhereTheNameCellStillOpensProperties()
        => Assert.Equal(VariableEditAction.Properties,
                        Control(VariableTableGestures.Default).RaiseNameCellDoubleClick(Row()));
}
