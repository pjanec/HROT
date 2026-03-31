using System;
using Hrot.IG.Systems;

namespace Hrot.IG.UI;

/// <summary>
/// Pure-logic state driving the Debug Panel (IG.5.1).
///
/// Wraps <see cref="MapUserConfig"/> and exposes toggle helpers so that
/// ImGui checkbox interactions modify the operator configuration in a single,
/// fully-testable location, isolated from any ImGui draw-call surface.
/// </summary>
public class DebugPanelState
{
    private readonly MapUserConfig _config;

    /// <param name="config">
    /// The application's shared <see cref="MapUserConfig"/> instance.
    /// Mutations are visible immediately to <see cref="StyleResolutionSystem"/>.
    /// </param>
    public DebugPanelState(MapUserConfig config)
        => _config = config ?? throw new ArgumentNullException(nameof(config));

    // ── MapUserConfig mirrors ─────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="MapUserConfig.ForceHostile"/>.
    /// Setting this to <c>true</c> forces all entities to render as hostile.
    /// </summary>
    public bool ForceHostile
    {
        get => _config.ForceHostile;
        set => _config.ForceHostile = value;
    }

    /// <summary>
    /// Mirrors <see cref="MapUserConfig.HideLabels"/>.
    /// Setting this to <c>true</c> suppresses all entity label draw calls.
    /// </summary>
    public bool HideLabels
    {
        get => _config.HideLabels;
        set => _config.HideLabels = value;
    }

    // ── Toggle helpers ────────────────────────────────────────────────────────

    /// <summary>Flips <see cref="MapUserConfig.ForceHostile"/> between <c>true</c> and <c>false</c>.</summary>
    public void ToggleForceHostile() => _config.ForceHostile = !_config.ForceHostile;

    /// <summary>Flips <see cref="MapUserConfig.HideLabels"/> between <c>true</c> and <c>false</c>.</summary>
    public void ToggleHideLabels() => _config.HideLabels = !_config.HideLabels;
}
