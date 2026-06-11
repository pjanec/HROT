using Hrot.UI.Common.Panels;
using ImGuiNET;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Tests for the <see cref="TransportIcons"/> shared helper — drawing and formatting.
/// </summary>
public class TransportIconsTests
{
    [Fact]
    public void Draw_AllShapes_Headless_NoThrow()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();

        // Open a child window so that GetWindowDrawList() and InvisibleButton/Dummy have a parent.
        ImGui.Begin("test_transport", ImGuiWindowFlags.NoSavedSettings);
        ImGui.SetWindowSize(new System.Numerics.Vector2(400, 200));

        foreach (TransportIcons.BtnShape shape in Enum.GetValues<TransportIcons.BtnShape>())
        {
            // Enabled: should return false in headless (no mouse click).
            bool clickedEnabled = TransportIcons.DrawTransportButton(
                $"##en_{shape}", 64f, shape, enabled: true);
            Assert.False(clickedEnabled,
                $"Enabled DrawTransportButton for {shape} should return false in headless (no mouse click)");

            ImGui.SameLine();

            // Disabled: should return false (no hit area at all).
            bool clickedDisabled = TransportIcons.DrawTransportButton(
                $"##dis_{shape}", 64f, shape, enabled: false);
            Assert.False(clickedDisabled,
                $"Disabled DrawTransportButton for {shape} should return false");
        }

        ImGui.End();
        fixture.Render();
    }

    [Fact]
    public void FormatRate_Integers_NoDecimalPoint()
    {
        Assert.Equal("1x", TransportIcons.FormatRate(1.0f));
        Assert.Equal("2x", TransportIcons.FormatRate(2.0f));
        Assert.Equal("10x", TransportIcons.FormatRate(10.0f));
    }

    [Fact]
    public void FormatRate_Fractional_OneDecimalPlace()
    {
        Assert.Equal("0.1x", TransportIcons.FormatRate(0.1f));
        Assert.Equal("1.5x", TransportIcons.FormatRate(1.5f));
        Assert.Equal("0.5x", TransportIcons.FormatRate(0.5f));
    }

    [Fact]
    public void FormatTime_FormatsHhMmSsMmm()
    {
        // 3661.234 seconds = 1h 1m 1s 234ms
        Assert.Equal("01:01:01.234", TransportIcons.FormatTime(3661.234));

        // 0 seconds
        Assert.Equal("00:00:00.000", TransportIcons.FormatTime(0.0));

        // 59.999 seconds = nearly a minute
        Assert.Equal("00:00:59.999", TransportIcons.FormatTime(59.999));

        // 3600 seconds = exactly 1 hour
        Assert.Equal("01:00:00.000", TransportIcons.FormatTime(3600.0));

        // Large value: 100 hours
        Assert.Equal("100:00:00.000", TransportIcons.FormatTime(360000.0));
    }

    [Fact]
    public void TimeRates_HasExpectedValues()
    {
        Assert.Equal(new[] { 0.1f, 0.5f, 1.0f, 1.5f, 2.0f, 5.0f, 10.0f }, TransportIcons.TimeRates);
    }
}
