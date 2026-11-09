using System;
using System.IO;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.Presentation.Tests.RunMode;

/// <summary>
/// ⭐ CE-4330 ③ (docs/DESIGN_Recording_Storage.md §3 P1; mockup ③ in <c>docs/UX/recording-storage-visual.svg</c>) — the ONE confirmation drawer
/// (<see cref="ConfirmationModal"/>) draws the close question's radios on a real frame: each with its key's coloured glyph, its label and its muted
/// detail line, Archive preselected, then <i>Close session</i> / Cancel. The frame goes to <c>HROT_RAIL_SHOTS</c> (default: the temp folder's
/// <c>hrot-frames</c>) for the side-by-side with the mockup.
/// </summary>
public sealed class TheCloseQuestionIsDrawnTests
{
    [SkippableFact]
    public void TheCloseQuestionDrawsItsThreeChoices_OnARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var ask = RunModeConfirmation.WithChoices("Night attack · exercise — 18:40, recorded. Every station returns to the Start screen.", null,
            "Close the live session", "mt_run_confirm", RunModeConfirmation.CancelLabel, RunModeChoice.CloseChoices, 0,
            new RunModeConfirmation.Button("Close session", null));

        using var frame = UiFrameHarness.Begin(900, 400);
        frame.StepN(6, () => ConfirmationModal.Draw(ask));
        Assert.Equal(0, ask.Selected);                                     // drawing does not move the default
        var dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        Directory.CreateDirectory(dir);
        frame.Screenshot(Path.Combine(dir, "ce4330-close-question.png"));
    }
}
