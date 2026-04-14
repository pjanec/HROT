using System;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Hrot.IG.UI;

/// <summary>
/// Translucent ImGui overlay displaying real-time performance counters (IG.5.4).
///
/// Positioned in the top-right corner of the screen; toggled on/off with the
/// <c>F3</c> key.  Data is sourced from <see cref="PerformanceMetrics"/>, which
/// must be refreshed via <see cref="PerformanceMetrics.Snapshot"/> each frame
/// before <see cref="Draw"/> is called.
///
/// The overlay is non-interactive (no-decoration, no-move window) so it does
/// not consume mouse or keyboard events that should reach the MapCanvas.
///
/// Visual output is not unit-tested — test <see cref="PerformanceMetrics"/> directly.
///
/// Call <see cref="Draw"/> each frame between <c>rlImGui.Begin()</c> and
/// <c>rlImGui.End()</c>.
/// </summary>
public class PerformanceOverlay
{
    // ── Layout constants ──────────────────────────────────────────────────────

    private const float OverlayWidth    = 240f;
    private const float OverlayHeight   = 110f;
    private const float OverlayMarginX  = 10f;
    private const float OverlayMarginY  = 10f;
    private const float OverlayAlpha    = 0.80f;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly PerformanceMetrics _metrics;

    /// <summary>
    /// <c>true</c> when the overlay is visible.
    /// Toggled by <c>F3</c> inside <see cref="Draw"/>.
    /// Starts visible so developers see it immediately on launch.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <param name="metrics">Performance metrics instance refreshed by the application shell.</param>
    public PerformanceOverlay(PerformanceMetrics metrics)
        => _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    // ── Draw ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks for F3 toggle and, when <see cref="IsVisible"/>, emits the
    /// performance overlay ImGui window.
    ///
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        // F3 toggles overlay visibility.
        if (Raylib.IsKeyPressed(KeyboardKey.F3))
            IsVisible = !IsVisible;

        if (!IsVisible)
            return;

        // Pin overlay to the top-right corner of the current screen.
        float posX = Raylib.GetScreenWidth()  - OverlayWidth  - OverlayMarginX;
        ImGui.SetNextWindowPos(new Vector2(posX, OverlayMarginY));
        ImGui.SetNextWindowSize(new Vector2(OverlayWidth, OverlayHeight));
        ImGui.SetNextWindowBgAlpha(OverlayAlpha);

        var flags = ImGuiWindowFlags.NoDecoration
                  | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoSavedSettings
                  | ImGuiWindowFlags.NoNav
                  | ImGuiWindowFlags.NoMouseInputs;

        IgPanelColors.Push();
        bool panelVisible = ImGui.Begin("Performance", flags);
        IgPanelColors.Pop();
        if (!panelVisible) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>
    /// Renders the overlay content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        ImGui.Text($"FPS        : {_metrics.Fps}");
        ImGui.Text($"Frame Time : {_metrics.FrameTimeMs:F2} ms");
        ImGui.Separator();
        ImGui.Text($"Entities   : {_metrics.TotalEntityCount}");
        ImGui.Text($"Visible    : {_metrics.VisibleEntityCount}");
    }
}
