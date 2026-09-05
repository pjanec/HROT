using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Panels;
using Xunit;

namespace NodeEditor.UI.Tests.Panels;

/// <summary>
/// ⭐⭐ <b>Which reason greys a section's "+".</b>
///
/// <para>📌 <b>User ruling, <c>2026-08-17</c>:</b> greying the button with an explanatory tooltip beats
/// letting the click land and then refusing — <i>"same information value, no false expectations."</i>
/// ⭐ A refinement of <c>Q26-B2</c> (which forbids the button VANISHING), not a reversal.</para>
///
/// <para>⚠ <b>Scope.</b> The drawing needs an ImGui context, so what is checkable headlessly is the
/// PRECEDENCE — which is why <c>MyBlueprintPanel.ResolveCreateDisabledReason</c> exists as a pure
/// function rather than living inline in <c>DrawSection</c>. ⛔ Stated rather than implied: nothing
/// here proves a pixel is grey.</para>
/// </summary>
public sealed class SectionCreateDisabledReasonTests
{
    private static MyBlueprintSectionDescriptor Section(string? reason = null)
        => new("locals", "Local Variables", 5, null, true, true, "editor.create-local-variable",
               CreateDisabledReason: reason);

    /// <summary>⭐ Registered handler and no model reason ⇒ the "+" works, as it always has.</summary>
    [Fact]
    public void AUsableSection_HasNoReason()
        => Assert.Null(MyBlueprintPanel.ResolveCreateDisabledReason(Section(), isCommandAvailable: true));

    /// <summary>⭐ The model's reason greys the button even though the handler exists.</summary>
    [Fact]
    public void TheModelsReason_GreysAWorkingCommand()
        => Assert.Equal("'Blend' is a macro.",
            MyBlueprintPanel.ResolveCreateDisabledReason(
                Section("'Blend' is a macro."), isCommandAvailable: true));

    /// <summary>
    /// ⭐ The pre-existing arm survives: a declared command nothing registers still greys and still
    /// names itself. ⛔ <c>BP-12c</c> shipped twice as an inert button; this message is how it is
    /// caught at a glance.
    /// </summary>
    [Fact]
    public void AnUnregisteredCommand_StillReportsItself()
        => Assert.Equal("Not implemented (editor.create-local-variable)",
            MyBlueprintPanel.ResolveCreateDisabledReason(Section(), isCommandAvailable: false));

    /// <summary>
    /// ⭐⭐ <b>Both at once: the model's reason wins.</b> ⚠ A designer can act on "this graph is a
    /// macro"; "Not implemented" would send them hunting a bug that is not there.
    /// </summary>
    [Fact]
    public void WhenBothApply_TheDesignerFacingReasonWins()
        => Assert.Equal("'Blend' is a macro.",
            MyBlueprintPanel.ResolveCreateDisabledReason(
                Section("'Blend' is a macro."), isCommandAvailable: false));
}
