using System;
using System.Linq;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using NodeEditor.Core.Widgets;
using Xunit;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3406</c> (R-3, AQ108 §0 D1′ + D29 + D30; ind.png) — the indicator shows the mode the cluster IS in: one rail per state of its picture. No dim level any more.
/// </summary>
public sealed class RunIndicatorTests
{
    private sealed class Ender : IExerciseEnder { public bool CanEnd => false; public bool HasEnded { get; set; } public bool End() => false; }

    private static void Is(RunModeIndicator i, string glyph, System.Numerics.Vector4 colour, string word, IndicatorLevel level)
    {
        Assert.Equal(glyph, i.Glyph); Assert.Equal(colour, i.Color); Assert.Equal(word, i.Word); Assert.Equal(level, i.Level);
    }

    /// <summary>Edit: the YELLOW pen on a saveable world; the violet EYE (steady) when <c>SaveDisabledReason</c> says no (a variant world, an error) — the same answer the Save slot reads.
    /// 🔴 Mutations: the pen always (the reason ignored) ⇒ the eye row; the eye's colour the pen's.</summary>
    [Fact]
    public void R3_Edit_PenWhereSaveable_EyeWhereNot()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Live);
        Is(rig.Choice.Indicator, IconsFontAwesome6.Pen, IconPalette.Yellow, "Edit", IndicatorLevel.Bright);
        Assert.Contains("Editing — your changes save to hill-attack. ▶ previews them.", rig.Choice.IndicatorTooltip());
        rig.C.SaveDisabledReason = "built with variant 3 — load as authored to save";
        Is(rig.Choice.Indicator, IconsFontAwesome6.Eye, IconPalette.Violet, "Edit", IndicatorLevel.Bright);
        Assert.Equal("Editing a variant (preview only) — not saveable: built with variant 3 — load as authored to save", rig.Choice.IndicatorTooltip());
    }

    /// <summary>Preview: the violet eye, blinking while it runs, steady while paused.</summary>
    [Fact]
    public void R3_Preview_VioletEye_BlinkingWhileRunning()
    {
        Is(With(ClusterState.OperatingPreview, running: true, clock: 4).Choice.Indicator, IconsFontAwesome6.Eye, IconPalette.Violet, "Preview", IndicatorLevel.Blink);
        Is(With(ClusterState.OperatingPreview, running: false, clock: 4).Choice.Indicator, IconsFontAwesome6.Eye, IconPalette.Violet, "Preview", IndicatorLevel.Bright);
    }

    /// <summary>Live: ● red when recorded, green when not; steady paused (the briefing), blinking running; Live · ended = the checkered flag in Live's colour.
    /// 🔴 Mutations: the flag row dropped (<c>LiveEnded</c> ignored) ⇒ the circle stays; red/green swapped.</summary>
    [Fact]
    public void R3_Live_RedGreen_SteadyBlinking_AndTheFlagWhenEnded()
    {
        var red = new System.Numerics.Vector4(0.90f, 0.22f, 0.22f, 1f);
        var green = IconPalette.Green;
        Is(With(ClusterState.OperatingLive).Choice.Indicator, IconsFontAwesome6.Circle, red, "Live", IndicatorLevel.Bright);                        // the briefing
        Is(With(ClusterState.OperatingLive, running: true, clock: 5).Choice.Indicator, IconsFontAwesome6.Circle, red, "Live", IndicatorLevel.Blink);
        Is(With(ClusterState.OperatingLive, running: true, clock: 5, runNotRecorded: true).Choice.Indicator, IconsFontAwesome6.Circle, green, "Live", IndicatorLevel.Blink);

        foreach (var (notRec, colour) in new[] { (false, red), (true, green) })
        {
            var rig = With(ClusterState.OperatingLive, running: true, clock: 754, runNotRecorded: notRec);
            var ender = new Ender();
            rig.F.Ender = ender;
            ender.HasEnded = true; rig.Choice.Update();
            Is(rig.Choice.Indicator, IconsFontAwesome6.FlagCheckered, colour, "Live", IndicatorLevel.Bright);
        }
        var brief = With(ClusterState.OperatingLive);
        Assert.Equal("Live — recorded. ▶ starts the exercise. Click: switch mode", brief.Choice.IndicatorTooltip());
        Assert.Equal("Live — not recorded, running. Click: switch mode", With(ClusterState.OperatingLive, running: true, clock: 5, runNotRecorded: true).Choice.IndicatorTooltip());
        var ended = With(ClusterState.OperatingLive, clock: 9); ended.F.Ender = new Ender { HasEnded = true }; ended.Choice.Update();
        Assert.Equal("Exercise ended — results ready. Click: Run again · checkpoint · DAR · AAR · Close", ended.Choice.IndicatorTooltip());
    }

    /// <summary>DAR amber ⟲; AAR blue ▶.</summary>
    [Fact]
    public void R3_DarAmber_AarBlue()
    {
        Is(With(ClusterState.OperatingReview, clock: 3).Choice.Indicator, IconsFontAwesome6.ClockRotateLeft, IconPalette.Amber, "DAR", IndicatorLevel.Bright);
        Is(With(ClusterState.OperatingReplay, clock: 3).Choice.Indicator, IconsFontAwesome6.Play, IconPalette.Blue, "AAR", IndicatorLevel.Bright);
        Assert.StartsWith("AAR — hill-attack. Click:", With(ClusterState.OperatingReplay).Choice.IndicatorTooltip());
    }

    /// <summary>⭐ 0 B: reading the indicator returns the SAME interned instance every time (it is read every frame).
    /// 🔴 Mutation: <c>Intern</c> building a new record each call ⇒ the reference differs.</summary>
    [Fact]
    public void R3_TheIndicator_IsInterned_SoAReadAllocatesNothing()
    {
        var rig = With(ClusterState.OperatingLive, running: true, clock: 5);
        Assert.Same(rig.Choice.Indicator, rig.Choice.Indicator);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = rig.Choice.Indicator;
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 2048);
    }

    /// <summary>story ② — the Edit menu: Edit ✓ · Live · AAR — open a recording… · separator · Close (Run to … sits between, where the host has it).
    /// 🔴 Mutation: the Close item dropped from <c>ModeMenu</c>.</summary>
    [Fact]
    public void R3_TheEditMenu_EditLiveAarSeparatorClose()
    {
        var rig = With(ClusterState.OperatingEdit, withDoor: true);
        rig.Choice.PickRecording = () => { };
        var menu = new MainToolbarTimeControlSection(rig.F, choice: rig.Choice).ModeMenu()!;
        Assert.Equal(new[] { "Edit", "Live", "AAR — open a recording…", "", "Close" }, menu.Items.Select(i => i.Label));
        Assert.True(menu.Items[0].Selected);
        Assert.True(menu.Items[3].IsSeparator);
        menu.Items[4].Invoke!();
        Assert.Equal(new[] { "Leave:Close" }, rig.C.Calls);
        Assert.DoesNotContain(new MainToolbarTimeControlSection(With(ClusterState.OperatingLive, clock: 5).F, choice: With(ClusterState.OperatingLive, clock: 5).Choice).ModeMenu()!.Items, i => i.Label == "Close");
    }
}
