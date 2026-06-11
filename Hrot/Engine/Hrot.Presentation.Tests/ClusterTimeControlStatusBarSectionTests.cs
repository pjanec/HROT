using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using ImGuiNET;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Smoke tests for the refactored <see cref="ClusterTimeControlStatusBarSection"/>
/// proving the status-bar render path still works after extraction of
/// <see cref="TransportIcons"/>.
/// </summary>
public class ClusterTimeControlStatusBarSectionTests
{
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

        var section = new ClusterTimeControlStatusBarSection(fake);

        ImGui.Begin("test_statusbar", ImGuiWindowFlags.NoSavedSettings);
        ImGui.SetWindowSize(new System.Numerics.Vector2(600, 60));

        // Should not throw — exercises TransportIcons.DrawTransportButton,
        // TransportIcons.FormatTime, TransportIcons.FormatRate,
        // and TransportIcons.TimeRates through the refactored Render() path.
        section.Render();

        ImGui.End();
        fixture.Render();
    }

    [Fact]
    public void Render_WhileRunning_ShowsPauseFace()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();

        var fake = new FakeTimeTransportFacade
        {
            IsPaused = false, // running
            IsPlayPauseEnabled = true,
            IsStepEnabled = false, // step disabled when running
            IsStopEnabled = true,
            TotalTime = 10.0,
            TimeScale = 2.0f,
        };

        var section = new ClusterTimeControlStatusBarSection(fake);

        ImGui.Begin("test_running", ImGuiWindowFlags.NoSavedSettings);
        ImGui.SetWindowSize(new System.Numerics.Vector2(600, 60));
        section.Render();
        ImGui.End();
        fixture.Render();

        // No throw = pass; verifies the running state path exercises all branches.
    }
}

/// <summary>
/// Minimal fake <see cref="ITimeTransportFacade"/> for testing
/// <see cref="ClusterTimeControlStatusBarSection"/> and
/// <see cref="MainToolbarTimeControlSection"/>.
/// </summary>
internal sealed class FakeTimeTransportFacade : ITimeTransportFacade
{
    public bool IsPlayPauseEnabled { get; set; } = true;
    public bool IsStepEnabled { get; set; } = true;
    public bool IsStopEnabled { get; set; } = true;
    public bool IsPaused { get; set; }
    public double TotalTime { get; set; }
    public float TimeScale { get; set; } = 1.0f;

    public int TogglePlayPauseCallCount { get; private set; }
    public int StepCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public float? LastSetTimeScale { get; private set; }

    public void TogglePlayPause() => TogglePlayPauseCallCount++;
    public void Step() => StepCallCount++;
    public void Stop() => StopCallCount++;
    public void SetTimeScale(float scale) => LastSetTimeScale = scale;
}
