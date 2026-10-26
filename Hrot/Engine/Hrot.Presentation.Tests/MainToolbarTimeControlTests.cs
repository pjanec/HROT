using System;
using System.Linq;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Tests for <see cref="MainToolbarTimeControlSection"/> — headless logic seams
/// and gating behaviour, using a fake <see cref="ITimeTransportFacade"/>.
/// </summary>
[Xunit.Collection(Hrot.Presentation.Tests.ImGuiTestCollection.Name)]   // ⭐ one global ImGui context
public class MainToolbarTimeControlTests
{
    [Fact]
    public void PlayPause_Click_CallsTogglePlayPause()
    {
        var fake = new FakeTimeTransportFacade { IsPlayPauseEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnPlayPause();

        Assert.Equal(1, fake.TogglePlayPauseCallCount);
    }

    [Fact]
    public void PlayPause_Click_WhenDisabled_NoOp()
    {
        var fake = new FakeTimeTransportFacade { IsPlayPauseEnabled = false };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnPlayPause();

        Assert.Equal(0, fake.TogglePlayPauseCallCount);
    }

    [Fact]
    public void Step_Click_CallsStep_GatedByIsStepEnabled()
    {
        var fake = new FakeTimeTransportFacade { IsStepEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStep();

        Assert.Equal(1, fake.StepCallCount);
    }

    [Fact]
    public void Step_Click_WhenDisabled_NoOp()
    {
        var fake = new FakeTimeTransportFacade { IsStepEnabled = false };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStep();

        Assert.Equal(0, fake.StepCallCount);
    }

    [Fact]
    public void Stop_Click_CallsStop_GatedByIsStopEnabled()
    {
        var fake = new FakeTimeTransportFacade { IsStopEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStop();

        Assert.Equal(1, fake.StopCallCount);
    }

    [Fact]
    public void Stop_Click_WhenDisabled_NoOp()
    {
        var fake = new FakeTimeTransportFacade { IsStopEnabled = false };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStop();

        Assert.Equal(0, fake.StopCallCount);
    }

    [Fact]
    public void PlayPauseFace_ReflectsIsPaused()
    {
        // Paused → Play icon (so the user can resume).
        Assert.Equal(TransportIcons.BtnShape.Play,
            MainToolbarTimeControlSection.PlayPauseFace(isPaused: true));

        // Running → Pause icon (so the user can pause).
        Assert.Equal(TransportIcons.BtnShape.Pause,
            MainToolbarTimeControlSection.PlayPauseFace(isPaused: false));
    }

    [Fact]
    public void TimeText_FormatsTotalTime()
    {
        // 3661.234 seconds = 1h 1m 1s 234ms.
        Assert.Equal("01:01:01.234", TransportIcons.FormatTime(3661.234));

        // Exactly 2h 30m 45s = 9045 seconds.
        Assert.Equal("02:30:45.000", TransportIcons.FormatTime(9045.0));

        // Sub-second.
        Assert.Equal("00:00:00.500", TransportIcons.FormatTime(0.5));
    }

    [Fact]
    public void RateButton_OpensSelector_SetsTimeScale()
    {
        var fake = new FakeTimeTransportFacade();
        var section = new MainToolbarTimeControlSection(fake);

        section.OnSelectRate(2.0f);

        Assert.Equal(2.0f, fake.LastSetTimeScale);

        section.OnSelectRate(0.5f);

        Assert.Equal(0.5f, fake.LastSetTimeScale);
    }

    [Fact]
    public void Render_Headless_NoThrow()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();

        var fake = new FakeTimeTransportFacade
        {
            IsPaused = true,
            IsPlayPauseEnabled = true,
            IsStepEnabled = true,
            IsStopEnabled = true,
            TotalTime = 3661.234,
            TimeScale = 1.5f,
        };

        var section = new MainToolbarTimeControlSection(fake);

        ImGuiNET.ImGui.Begin("test_toolbar", ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
        ImGuiNET.ImGui.SetWindowSize(new System.Numerics.Vector2(600, 80));

        section.Render();

        ImGuiNET.ImGui.End();
        fixture.Render();
    }

    // ── ⭐ CE-3330 E3 — mode indicator, menu, ■ → ⏏, F15, the axis icon (DAR §9a) ─────────────────────────────

    private static FakeTimeTransportFacade In(TransportMode mode, ReplayAxis axis = ReplayAxis.Sim, double total = 0, double? livePoint = null,
                                              ReplaySession? session = null)
        => new() { Mode = mode, Axis = axis, TotalTime = total, LivePoint = livePoint, Session = session };

    [Theory]
    [InlineData(TransportMode.Edit,    Fdp.Presentation.Fonts.IconsFontAwesome6.Pen, 0.60f, 0.60f, 0.60f)]
    [InlineData(TransportMode.Preview, Fdp.Presentation.Fonts.IconsFontAwesome6.CircleHalfStroke, 0.66f, 0.45f, 0.92f)]
    [InlineData(TransportMode.Live,    Fdp.Presentation.Fonts.IconsFontAwesome6.Circle, 0.25f, 0.80f, 0.30f)]
    [InlineData(TransportMode.Review,  Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft, 1.00f, 0.70f, 0.20f)]
    [InlineData(TransportMode.Replay,  Fdp.Presentation.Fonts.IconsFontAwesome6.Play, 0.30f, 0.60f, 1.00f)]
    public void ModeGlyph_IsTheFontAwesomeGlyphAndColourPerMode(TransportMode mode, string glyph, float r, float g, float b)
    {
        var ind = MainToolbarTimeControlSection.ModeGlyph(mode)!;
        Assert.Equal(glyph, ind.Glyph);
        Assert.Equal(new System.Numerics.Vector4(r, g, b, 1f), ind.Color);
    }

    [Theory]
    [InlineData(TransportMode.Edit,    "")]   // Pen
    [InlineData(TransportMode.Preview, "")]   // CircleHalfStroke
    [InlineData(TransportMode.Live,    "")]   // Circle
    [InlineData(TransportMode.Review,  "")]   // ClockRotateLeft
    [InlineData(TransportMode.Replay,  "")]   // Play
    public void ModeIcon_IsTheFontAwesomeGlyphPerMode(TransportMode mode, string expected)
        => Assert.Equal(expected, MainToolbarTimeControlSection.ModeIcon(mode));

    [Fact]
    public void ModeIcon_IdleHasNone()
        => Assert.Null(MainToolbarTimeControlSection.ModeIcon(TransportMode.Idle));

    [Fact]
    public void ModeGlyph_IdleHasNone()
        => Assert.Null(MainToolbarTimeControlSection.ModeGlyph(TransportMode.Idle));

    [Theory]
    [InlineData(TransportMode.Edit)]
    [InlineData(TransportMode.Preview)]
    [InlineData(TransportMode.Replay)]
    [InlineData(TransportMode.Idle)]
    public void ModeMenu_HasNoMenuOutsideLiveAndReview(TransportMode mode)
        => Assert.Null(new MainToolbarTimeControlSection(In(mode)).ModeMenu());

    [Fact]
    public void LiveMenu_OffersTheReviewWindowsAndTheStart()
    {
        var menu = new MainToolbarTimeControlSection(In(TransportMode.Live, total: 754)).ModeMenu()!;
        Assert.Equal(new[] { "Review the last 30 s", "Review the last 2 min", "Review the last 10 min", "Review from the start" },
                     menu.Items.Select(i => i.Label).ToArray());
        Assert.StartsWith("Live", menu.Header);
    }

    [Fact]
    public void LiveMenu_Review2Min_EntersBySimMinus120_ClampedAtZero()
    {
        var fake = In(TransportMode.Live, total: 754);
        new MainToolbarTimeControlSection(fake).ModeMenu()!.Items[1].Invoke!();
        Assert.Equal(754 - 120, fake.LastEnterReviewAtSim);

        var young = In(TransportMode.Live, total: 50);
        new MainToolbarTimeControlSection(young).ModeMenu()!.Items[1].Invoke!();
        Assert.Equal(0d, young.LastEnterReviewAtSim);

        var start = In(TransportMode.Live, total: 754);
        new MainToolbarTimeControlSection(start).ModeMenu()!.Items[3].Invoke!();
        Assert.Equal(0d, start.LastEnterReviewAtSim);
    }

    private static ReplaySession SessionWithCheckpoint() => new()
    {
        Active = true, IsReview = true, ZeroWallTicks = 100 * TimeSpan.TicksPerSecond, LivePointSimSeconds = 600,
        Checkpoints = new[] { new CheckpointMark { Label = "cp-1", TotalTime = 42.5, WallTicks = 160 * TimeSpan.TicksPerSecond } },
    };

    [Fact]
    public void ReviewMenu_Items_HeaderAndBackToLive()
    {
        var fake = In(TransportMode.Review, total: 273, livePoint: 600, session: SessionWithCheckpoint());
        var menu = new MainToolbarTimeControlSection(fake).ModeMenu()!;
        Assert.Equal("Review · 5:27 behind live", menu.Header);
        Assert.Equal(new[] { "Restart from 30 s before live", "Restart from 2 min before live", "Restart from a checkpoint", "⏏ Back to live" },
                     menu.Items.Select(i => i.Label).ToArray());

        menu.Items[3].Invoke!();
        Assert.Equal(1, fake.ExitReviewCallCount);

        menu.Items[1].Invoke!();
        Assert.Equal(600 - 120, fake.LastSeek);
    }

    [Fact]
    public void ReviewMenu_WithoutALivePoint_OffersNoRestartFromLive()
    {
        var menu = new MainToolbarTimeControlSection(In(TransportMode.Review, livePoint: null)).ModeMenu()!;
        Assert.DoesNotContain(menu.Items, i => i.Label.StartsWith("Restart from 30"));
        Assert.False(menu.Items.Single(i => i.Label == "Restart from a checkpoint").Enabled);   // no checkpoints either
    }

    [Theory]
    [InlineData(ReplayAxis.Sim, 42.5)]    // Sim: the checkpoint's sim time
    [InlineData(ReplayAxis.Wall, 60.0)]   // Wall: seconds after the session's zero ((160 - 100) s)
    public void ReviewMenu_CheckpointSeeksToTheValueOnTheActiveAxis(ReplayAxis axis, double expected)
    {
        var fake = In(TransportMode.Review, axis, total: 10, livePoint: 600, session: SessionWithCheckpoint());
        var sub = new MainToolbarTimeControlSection(fake).ModeMenu()!.Items.Single(i => i.Label == "Restart from a checkpoint");
        Assert.True(sub.Enabled);
        var item = Assert.Single(sub.Children!);
        Assert.Equal("cp-1", item.Label);
        item.Invoke!();
        Assert.Equal(expected, fake.LastSeek);
    }

    [Fact]
    public void StopFace_InAReviewIsBackToLive_ElsewhereStop()
    {
        Assert.Equal(MainToolbarTimeControlSection.StopFaceKind.BackToLive, MainToolbarTimeControlSection.StopFace(TransportMode.Review));
        foreach (var m in new[] { TransportMode.Live, TransportMode.Replay, TransportMode.Preview, TransportMode.Edit, TransportMode.Idle })
            Assert.Equal(MainToolbarTimeControlSection.StopFaceKind.Stop, MainToolbarTimeControlSection.StopFace(m));
    }

    [Fact]
    public void StopTooltip_PerMode()
    {
        Assert.Equal("Stop the exercise", new MainToolbarTimeControlSection(In(TransportMode.Live)).StopTooltip());
        Assert.Equal("Leave the replay", new MainToolbarTimeControlSection(In(TransportMode.Replay)).StopTooltip());
        Assert.Equal("Back to edit", new MainToolbarTimeControlSection(In(TransportMode.Preview)).StopTooltip());
        Assert.Equal("Back to live — 5:27 ahead",
            new MainToolbarTimeControlSection(In(TransportMode.Review, total: 273, livePoint: 600)).StopTooltip());
    }

    [Fact]
    public void F15_StopIsIgnoredFor3SecondsAfterAReviewReturnsToLive_ThenStops()
    {
        var clock = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var fake = In(TransportMode.Review);
        var section = new MainToolbarTimeControlSection(fake, () => clock);
        section.Observe();                 // a frame in the review

        fake.Mode = TransportMode.Live;    // the return landed
        section.Observe();                 // the next frame sees it
        clock = clock.AddSeconds(2.9);
        Assert.Contains("Just returned", section.StopTooltip());
        section.OnStop();
        Assert.Equal(0, fake.StopCallCount);   // the second press of a double press: ignored

        clock = clock.AddSeconds(0.2);     // 3.1 s after the return
        Assert.Equal("Stop the exercise", section.StopTooltip());
        section.OnStop();
        Assert.Equal(1, fake.StopCallCount);
    }

    [Fact]
    public void F15_AStopInLiveThatFollowsNoReview_IsNeverGuarded()
    {
        var fake = In(TransportMode.Live);
        var section = new MainToolbarTimeControlSection(fake, () => DateTime.UnixEpoch);
        section.Observe();
        section.OnStop();
        Assert.Equal(1, fake.StopCallCount);
    }

    [Fact]
    public void AxisToggle_CallsSetAxisWithTheOtherAxis_AndIsDisabledInEdit()
    {
        var live = In(TransportMode.Live, ReplayAxis.Sim);
        var s = new MainToolbarTimeControlSection(live);
        Assert.Equal("S", MainToolbarTimeControlSection.AxisFace(ReplayAxis.Sim));
        Assert.Equal("W", MainToolbarTimeControlSection.AxisFace(ReplayAxis.Wall));
        Assert.Equal("Sim time — pauses skipped", MainToolbarTimeControlSection.AxisTooltip(ReplayAxis.Sim));
        Assert.Equal("Wall time — pauses shown", MainToolbarTimeControlSection.AxisTooltip(ReplayAxis.Wall));
        Assert.True(s.IsAxisEnabled);
        s.OnToggleAxis();
        Assert.Equal(ReplayAxis.Wall, live.Axis);
        s.OnToggleAxis();
        Assert.Equal(ReplayAxis.Sim, live.Axis);

        foreach (var m in new[] { TransportMode.Edit, TransportMode.Preview, TransportMode.Idle })
        {
            var f = In(m, ReplayAxis.Sim);
            var sec = new MainToolbarTimeControlSection(f);
            Assert.False(sec.IsAxisEnabled);
            sec.OnToggleAxis();
            Assert.Equal(ReplayAxis.Sim, f.Axis);
        }
    }

    [Theory]
    [InlineData(TransportMode.Edit)]
    [InlineData(TransportMode.Live)]
    [InlineData(TransportMode.Review)]
    [InlineData(TransportMode.Replay)]
    public void Render_EachMode_NoThrow(TransportMode mode)
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        var fake = In(mode, total: 12, livePoint: 600, session: SessionWithCheckpoint());
        var section = new MainToolbarTimeControlSection(fake);
        ImGuiNET.ImGui.Begin("test_toolbar_modes", ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
        ImGuiNET.ImGui.SetWindowSize(new System.Numerics.Vector2(700, 80));
        section.Render();
        ImGuiNET.ImGui.End();
        fixture.Render();
    }
}
