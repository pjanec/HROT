using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100c</c>) — <c>[x]</c> closes the dialog, and STAYS closed.</b>
///
/// <para>🔴🔴 <b>The defect.</b> ImGui clears the <c>ref bool</c> when <c>[x]</c> is clicked. ⛔ But
/// <c>VariableEditModal.IsOpen</c> is <c>_binder.ActiveSession != null</c>, which <c>[x]</c> never
/// touched ⇒ the guard at the top of <c>Draw</c> let the next frame straight through and
/// <c>OpenPopup</c> <b>reopened what the designer had just closed</b>. ⚠ The dialog could only be
/// dismissed through Cancel.</para>
///
/// <para>⭐⭐⭐ <b>WHY THIS RAIL MUST SPAN TWO FRAMES — and why every earlier rail missed it.</b>
/// 📌 The handoff, verbatim: <i>"after a frame in which <c>[x]</c> is signalled, <c>IsPopupOpen</c> is
/// false on the NEXT frame too. ⛔ Not just the same frame."</i> ⇒ ⭐ <b>a same-frame assertion PASSES
/// against the broken code</b>: the popup really did close, and it was the following frame that
/// resurrected it. ⚠⚠ <b>That one-frame gap is the whole bug</b>, and it is invisible to any rail that
/// does not render twice.</para>
///
/// <para>⚠⚠ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*, precisely: ⛔ <b>ONE LINE — the <c>if</c>
/// inside <c>Draw</c> that notices ImGui cleared the flag and calls
/// <c>CloseFromWindowChrome()</c>.</b> ⭐ Everything else is real: the rail calls that method, the real
/// modal renders in a real frame, and the reopen — <b>the half no headless rail could ever see</b> — is
/// asserted across frames.</para>
///
/// <para>📐 <b>An earlier draft of this rail signalled the close with
/// <c>ImGui.CloseCurrentPopup()</c> after <c>Draw()</c> returned, and it FAILED with <i>"[x] did not
/// close the dialog"</i> — ⭐ correctly:</b> that call is a no-op outside a popup's
/// <c>Begin</c>/<c>End</c> pair, so it was signalling nothing. ⚠ <b>The rail caught its own author
/// before it could certify anything</b>, which is worth recording: the first thing the frame harness
/// measured was a mistake in a test.</para>
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class TheCloseBoxActuallyClosesTests
{
#pragma warning disable CS0649
    private struct Counter { public int Count; }
#pragma warning restore CS0649

    private static VariableRow Row() => new(
        Origin:    new VariableRowOrigin(Guid.NewGuid(), new Entity(1, 1), "Variables", "Count", "Count4"),
        ShortName: "Count", TypeText: "Counter", ClrType: typeof(Counter),
        ReadValue: () => Array.Empty<byte>(),
        RowKind:   VariableRowKind.Normal, IsStale: false);

    /// <summary>
    /// ⭐⭐⭐ <b>Close it, then render two more frames and check it is STILL closed.</b>
    ///
    /// <para>⭐ The strongest assertion is not on ImGui's popup stack but on the SESSION: a dialog whose
    /// popup is shut while its session lives is a dialog that will come back. ⇒ both are asserted, and
    /// <c>ActiveSession</c> is the one that would have caught this in Batch 96.</para>
    /// </summary>
    [SkippableFact]
    public void TheCloseBoxEndsTheSession_AndItDoesNotReopenNextFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new ComponentEditServiceBuilder().Build()),
            entryResolver: _ => new BlackboardVariableEntry("Count", typeof(Counter), Comment: null),
            runState:      () => VariableRunState.Planning);
        var modal = new VariableEditModal(binder, () => VariableRunState.Planning);

        binder.OnEditValue(Row());
        Assert.NotNull(binder.ActiveSession);           // ⛔ anti-vacuity

        bool openAfterClose = true, openFrameAfterThat = true;

        using (var frame = UiFrameHarness.Begin())
        {
            // ⭐ Frames 0–2: let ImGui settle the popup, exactly as a designer's first sight of it.
            frame.StepN(3, () => modal.Draw());
            Assert.True(ImGuiOpenDuringLastStep(frame, modal), "the dialog never opened at all");

            // ⭐⭐ SIGNAL THE CLOSE — the method ImGui's own [x] handling reaches.
            modal.CloseFromWindowChrome();

            // ⚠ ONE REAP FRAME, and the reason is ImGui's, not ours: a modal is dismissed by NOT being
            //   submitted, and ImGui only retires the un-submitted popup at the END of that frame.
            //   ⛔ Reading IsPopupOpen during it reports the popup ImGui has not yet let go of.
            //   ⭐ It cannot hide the defect this rail is for: under the old code the session survived
            //     `[x]`, so `Draw` would RE-SUBMIT here and the popup would never be retired at all.
            frame.Step(() => modal.Draw());

            // ⭐ Now it must be gone…
            frame.Step(() => { modal.Draw(); openAfterClose = ImGui.IsPopupOpen(modal.PopupId); });

            // ⭐⭐⭐ …AND STAY GONE — the frame in which the broken code brought it back.
            frame.Step(() => { modal.Draw(); openFrameAfterThat = ImGui.IsPopupOpen(modal.PopupId); });
        }

        Assert.False(openAfterClose,      "[x] did not close the dialog");
        Assert.False(openFrameAfterThat,  "[x] closed the dialog and the NEXT frame reopened it");
        Assert.Null(binder.ActiveSession);
        Assert.False(modal.IsOpen);
    }

    /// <summary>
    /// ⭐⭐ <b>And <c>[x]</c> DISCARDS — it must not commit.</b> ⛔ A close box that saves is worse than
    /// one that does not close: the designer's escape hatch would write. ⭐ Asserted through the
    /// binder's own outcome, which stays <c>null</c> because nothing was committed.
    /// </summary>
    [SkippableFact]
    public void TheCloseBoxCommitsNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new ComponentEditServiceBuilder().Build()),
            entryResolver: _ => new BlackboardVariableEntry("Count", typeof(Counter), Comment: null),
            runState:      () => VariableRunState.Planning);
        var modal = new VariableEditModal(binder, () => VariableRunState.Planning);

        binder.OnEditValue(Row());

        using (var frame = UiFrameHarness.Begin())
        {
            frame.StepN(3, () => modal.Draw());
            modal.CloseFromWindowChrome();
            frame.StepN(2, () => modal.Draw());
        }

        Assert.Null(binder.LastOutcome);   // ⛔ nothing was committed, not even a refusal
    }

    /// <summary>⭐ Reads the popup state from inside one extra frame — <c>IsPopupOpen</c> is only
    /// meaningful within a frame, so it cannot simply be called after <c>StepN</c>.</summary>
    private static bool ImGuiOpenDuringLastStep(UiFrameSession frame, VariableEditModal modal)
    {
        bool open = false;
        frame.Step(() => { modal.Draw(); open = ImGui.IsPopupOpen(modal.PopupId); });
        return open;
    }
}
