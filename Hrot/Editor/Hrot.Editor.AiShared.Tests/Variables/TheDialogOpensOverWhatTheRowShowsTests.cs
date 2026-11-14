using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>The dialog opens over WHAT THE ROW IS SHOWING.</b>
///
/// <para>🔴🔴 <b>Measured from the user's report</b> *(<c>2026-08-20</c>: "opened Edit on a variable row
/// which was showing '312'. The Edit variable dialog opened with value '0'")*. 📐
/// <c>VariableEditLauncher.Open</c> received the run state, used it for the POLICY check only, and then
/// called <c>OpenSession(entry, scope)</c> — which hydrates <c>varEntry.DefaultValueJson</c> and
/// nothing else. ⇒ ⛔ <b>a PAUSED edit always opened at the declaration's default.</b></para>
///
/// <para>⛔⛔ <b>And it was one working live-writer away from DATA LOSS</b>: while paused the commit
/// targets the LIVE blackboard, so OK would have written the default over the running value. ⭐ The user
/// only saw a refusal because this blueprint had no live-write path — 📌 exactly the <c>BP-367</c>
/// pattern, where a second defect stayed invisible because a first one refused.</para>
/// </summary>
public sealed class TheDialogOpensOverWhatTheRowShowsTests
{
    private static VariableEditLauncher Launcher() =>
        new(new StructEdit.Reflection.ComponentEditServiceBuilder().Build());

    private static BlackboardVariableEntry Entry() =>
        new("Count", typeof(int), Comment: null, DefaultValueJson: "0");

    /// <summary>⭐ A row whose LIVE object arm reads <paramref name="live"/>.</summary>
    private static VariableRow Row(object? live) => new(
        Origin:    new VariableRowOrigin(System.Guid.NewGuid(), default, "Variables", "Count", "Bp"),
        ShortName: "Count",
        TypeText:  "int",
        ClrType:   typeof(int),
        ReadValue: () => System.Array.Empty<byte>(),
        ReadValueObject: live is null ? null : () => live);

    private static object? Opened(VariableRunState runState, object? live)
    {
        using var session = Launcher().Open(Row(live), VariableEditAction.EditValue, runState, Entry())!;
        return ScalarEditBox.Unwrap(session.Commit(), typeof(int));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>PAUSED — the dialog opens over the LIVE value.</b> ⛔ This is the user's case, and it
    /// read <c>0</c>.
    /// </summary>
    [Fact]
    public void PausedOpensOverTheLiveValue()
        => Assert.Equal(312, Opened(VariableRunState.Paused, 312));

    /// <summary>
    /// ⭐ <b>PLANNING opens over the DECLARATION</b> — 📌 <c>Q32</c> ruling 3, and the commit targets the
    /// initial value there, so seeding from a live arm would edit one thing and write another.
    /// </summary>
    [Fact]
    public void PlanningOpensOverTheDeclaration()
        => Assert.Equal(0, Opened(VariableRunState.Planning, 312));

    /// <summary>⚠ A row with NO live arm falls back to the declaration — ⛔ never a guess.</summary>
    [Fact]
    public void WithoutALiveArmTheDeclarationIsUsed()
        => Assert.Equal(0, Opened(VariableRunState.Paused, null));

    /// <summary>
    /// ⚠ <b>A live value of the WRONG TYPE is ignored</b>, not coerced and not thrown on.
    /// ⭐ A provider that hands back something unexpected must degrade to the declaration.
    /// </summary>
    [Fact]
    public void AWrongTypedLiveValueFallsBackToTheDeclaration()
        => Assert.Equal(0, Opened(VariableRunState.Paused, "not an int"));
}
