using System;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Hrot.Player.UI;

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

    private readonly Fdp.Toolkit.Input.KeymapReader _keys;   // ⭐ B-10 K4 (§6g) — F3 is the keymap's debug row (Global, "debug"), read here

    /// <param name="metrics">Performance metrics instance refreshed by the application shell.</param>
    /// <param name="keymap">The keymap whose debug rows toggle the overlay; default the process's.</param>
    /// <param name="input">The device the rows are read from; default Raylib's (a rail hands in its own).</param>
    public PerformanceOverlay(PerformanceMetrics metrics, Fdp.Toolkit.Input.Keymap? keymap = null, Fdp.Toolkit.Input.IChordInput? input = null)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _keys = Hrot.UI.Common.DebugKeys.Reader(keymap ?? Fdp.Toolkit.Input.Keymap.Process,
            input ?? new Fdp.Presentation.Input.ProviderChordInput(new Fdp.Toolkit.Vis2D.Defaults.RaylibInputProvider()));
    }

    /// <summary>F3 (the keymap's debug row, <see cref="Hrot.UI.Common.DebugKeys.PerformanceOverlay"/>) toggles the overlay.</summary>
    internal void HandleKeys()
    {
        if (_keys.Pressed(Hrot.UI.Common.DebugKeys.PerformanceOverlay))
            IsVisible = !IsVisible;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks for F3 toggle and, when <see cref="IsVisible"/>, emits the
    /// performance overlay ImGui window.
    ///
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        HandleKeys();

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

        PlayerPanelColors.Push();
        bool panelVisible = ImGui.Begin("Performance", flags);
        PlayerPanelColors.Pop();
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
