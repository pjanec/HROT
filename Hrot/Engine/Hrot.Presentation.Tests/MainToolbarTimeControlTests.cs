using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Tests for <see cref="MainToolbarTimeControlSection"/> — headless logic seams
/// and gating behaviour, using a fake <see cref="ITimeTransportFacade"/>.
/// </summary>
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
}
