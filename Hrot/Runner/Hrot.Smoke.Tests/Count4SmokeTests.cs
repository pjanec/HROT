using System;

namespace Hrot.Smoke.Tests;

/// <summary>
/// ⭐⭐⭐ <b>THE FIRST SMOKE TEST — the user's sentence, executable.</b>
///
/// <para>🔴 <b>User, <c>2026-08-20</c>:</b> <i>"one entity carrying simple behavior (like the Count4
/// blueprint) and running it, watching if it does what it usually does, checking the panels if they
/// show what they usually do."</i></para>
///
/// <para>📐 <b>Why this is worth more than more unit tests</b> *(<c>DESIGN_Smoke_Suite.md</c> §4,
/// measured)*: ~8 000 unit tests ran green through <b>five batches in which the feature was dead</b>,
/// and of the five defects a human found on <c>2026-08-20</c>, ⭐ <b>T2 would have caught one and T3
/// three</b> — ⛔ four of five, without a human.</para>
///
/// <para>⭐⭐ <b>DEVIATIONS from <c>DESIGN_Smoke_Suite.md</c>'s class diagram</b> *(📌 obligation ③ —
/// an implementing task CHECKS the diagrams and reports the match)*. The design draws <b>4 classes</b>
/// and <b>1 sequence</b>; three classes are built as drawn — <c>SmokeFixture</c>,
/// <c>EditorHarness</c> *(existing, composed)*, <c>EditorPanels</c> *(new)* — and:
/// <list type="bullet">
///   <item>⛔ <b><c>EditorPanels.Workspace : PerspectiveWorkspace</c> does not exist.</b> ⭐ Built as
///   <c>PerspectiveWorkspaceRegistrar</c> instead. 📌 <c>R-121</c>'s extraction is explicitly OUT of
///   this batch, and the registrar is what production actually holds — ⇒ ⭐ the deviation makes the
///   diagram MORE true, not less.</item>
///   <item>⚠ <b>Two Details panels exist, and Blueprint uses its own.</b> The diagram's single
///   <c>DetailsRows</c> is <c>BlueprintDetailsWindow.Variables.Model</c> here;
///   <c>registrar.AiDetails</c> is <c>null</c> on Blueprint by construction
///   *(<c>HostKindOf("Blueprint") == null</c>)* — ⛔ not a defect, and 📌 the reason
///   <c>BlueprintDetailsWindow</c> is registered as an extra window.</item>
///   <item>⛔ <b><c>UiFrameSession</c> is not referenced</b> — the design marks it <i>T3 only</i>, and
///   T3 is not this batch.</item>
/// </list>
/// ⭐ The SEQUENCE diagram is followed exactly: load → attach → pump → T1 → Details text → Watch
/// text.</para>
/// </summary>
public sealed class Count4SmokeTests
{
    private const string Variable = "Count";

    /// <summary>
    /// ⭐⭐ <b>T1 — the sim does what it usually does.</b>
    ///
    /// <para>📐 <c>Count4</c>'s generated tick is <c>Count += 11</c> then <c>Delay(1s)</c>, and a pumped
    /// frame is 5 ms ⇒ ⭐ after a handful of frames the count is <b>11</b> and STAYS 11 until a second
    /// of sim time has passed. ⚠ That makes this scenario a better smoke than a bare per-frame counter:
    /// it exercises the LATENT CURSOR, so a broken resume shows up as 22, or as 0.</para>
    ///
    /// <para>⛔ <b>It would have been <c>0</c> before <c>102c</c></b> at <c>frames: 1</c> — the harness's
    /// first pumped frame carried <c>dt = 0</c> and <c>BlueprintTickSystem</c> skipped it.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void TheCounterRuns(int frames)
    {
        using var smoke = new SmokeFixture();
        smoke.PumpFrames(frames);

        Assert.Equal(SmokeFixture.CountPerPass, smoke.Count);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>T2 — THE PANELS SHOW WHAT THEY USUALLY DO. This is the tier that exists because of
    /// defect #4.</b>
    ///
    /// <para>🔴 <b>What the human found on <c>2026-08-20</c>:</b> the Watch read <c>0</c> while the sim
    /// was running. ⚠ Every unit test was green: the model was right, the WIRE was not. ⇒ ⭐ this
    /// asserts the two panels' rendered TEXT, and compares them to each other — 📌 <b>two panels
    /// disagreeing is the signal</b>, and no single-panel assertion can see it.</para>
    ///
    /// <para>⛔ <b>No pixels.</b> The strings come from the production
    /// <c>VariableValueFormatter</c> through the production run-state arm.</para>
    /// </summary>
    [Fact]
    public void BothPanelsShowTheRunningValue()
    {
        using var smoke = new SmokeFixture();

        // ⭐ The registrar must have joined the outline to the Details panel, or the click below routes
        //   nowhere and every assertion after it would be vacuous.
        Assert.True(smoke.Panels.OutlineIsRoutedToDetails,
            "The outline is not routed to the Details panel, so an outline click shows nothing. "
          + "Every assertion below would pass vacuously.");

        smoke.PumpFrames(4);

        smoke.Panels.SelectInOutline(Variable);
        smoke.Panels.PinToWatch(Variable);

        var details = smoke.Panels.DetailsRowText(Variable);
        var watch   = smoke.Panels.WatchRowText(Variable);
        var live    = smoke.Count;

        // ⭐⭐ 📌 The design: "not just pass/fail — print the row texts, so a red says 'Watch showed 0,
        //    Details showed 11' rather than 'Assert.Equal failed'."
        var seen = $"blackboard={live}, Details showed \"{details}\", Watch showed \"{watch}\"";

        Assert.True(details == watch,
            $"The two panels disagree about the same variable: {seen}. "
          + "One of them is reading the wrong arm, or a stale sample.");

        Assert.True(details == live.ToString(),
            $"The panels do not show the running value: {seen}. "
          + "The sim moved and the panel did not.");
    }
}
