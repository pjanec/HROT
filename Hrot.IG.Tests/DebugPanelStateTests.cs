using Hrot.IG.Systems;
using Hrot.IG.UI;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.5.1: <see cref="DebugPanelState"/>.
///
/// Validates that state mutations applied through <see cref="DebugPanelState"/>
/// propagate correctly to the underlying <see cref="MapUserConfig"/> instance,
/// ensuring ImGui checkbox interactions reach the <see cref="StyleResolutionSystem"/>
/// without requiring any ImGui draw calls in tests.
/// </summary>
public class DebugPanelStateTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static (DebugPanelState state, MapUserConfig config) Create()
    {
        var config = new MapUserConfig();
        var state  = new DebugPanelState(config);
        return (state, config);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ForceHostile toggle
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setting <see cref="DebugPanelState.ForceHostile"/> to <c>true</c> must
    /// propagate immediately to <see cref="MapUserConfig.ForceHostile"/>.
    /// </summary>
    [Fact]
    public void ForceHostile_SetTrue_PropagatestoConfig()
    {
        var (state, config) = Create();

        state.ForceHostile = true;

        Assert.True(config.ForceHostile);
    }

    /// <summary>
    /// Setting <see cref="DebugPanelState.ForceHostile"/> to <c>false</c> must
    /// propagate immediately to <see cref="MapUserConfig.ForceHostile"/>.
    /// </summary>
    [Fact]
    public void ForceHostile_SetFalse_PropagatestoConfig()
    {
        var (state, config) = Create();
        config.ForceHostile = true; // Pre-seed

        state.ForceHostile = false;

        Assert.False(config.ForceHostile);
    }

    /// <summary>
    /// Reading <see cref="DebugPanelState.ForceHostile"/> must reflect the
    /// current value of <see cref="MapUserConfig.ForceHostile"/>.
    /// </summary>
    [Fact]
    public void ForceHostile_Getter_ReflectsConfigValue()
    {
        var (state, config) = Create();
        config.ForceHostile = true;

        Assert.True(state.ForceHostile);
    }

    /// <summary>
    /// <see cref="DebugPanelState.ToggleForceHostile"/> must flip
    /// <see cref="MapUserConfig.ForceHostile"/> from <c>false</c> to <c>true</c>.
    /// </summary>
    [Fact]
    public void ToggleForceHostile_FromFalse_SetsTrue()
    {
        var (state, config) = Create();
        config.ForceHostile = false;

        state.ToggleForceHostile();

        Assert.True(config.ForceHostile);
    }

    /// <summary>
    /// <see cref="DebugPanelState.ToggleForceHostile"/> must flip
    /// <see cref="MapUserConfig.ForceHostile"/> from <c>true</c> to <c>false</c>.
    /// </summary>
    [Fact]
    public void ToggleForceHostile_FromTrue_SetsFalse()
    {
        var (state, config) = Create();
        config.ForceHostile = true;

        state.ToggleForceHostile();

        Assert.False(config.ForceHostile);
    }

    /// <summary>
    /// Two consecutive <see cref="DebugPanelState.ToggleForceHostile"/> calls
    /// must return the flag to its original value.
    /// </summary>
    [Fact]
    public void ToggleForceHostile_TwiceInARow_ReturnsToPreviousState()
    {
        var (state, config) = Create();
        config.ForceHostile = false;

        state.ToggleForceHostile();
        state.ToggleForceHostile();

        Assert.False(config.ForceHostile);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HideLabels toggle
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setting <see cref="DebugPanelState.HideLabels"/> to <c>true</c> must
    /// propagate immediately to <see cref="MapUserConfig.HideLabels"/>.
    /// </summary>
    [Fact]
    public void HideLabels_SetTrue_PropagatestoConfig()
    {
        var (state, config) = Create();

        state.HideLabels = true;

        Assert.True(config.HideLabels);
    }

    /// <summary>
    /// Setting <see cref="DebugPanelState.HideLabels"/> to <c>false</c> must
    /// propagate immediately to <see cref="MapUserConfig.HideLabels"/>.
    /// </summary>
    [Fact]
    public void HideLabels_SetFalse_PropagatestoConfig()
    {
        var (state, config) = Create();
        config.HideLabels = true; // Pre-seed

        state.HideLabels = false;

        Assert.False(config.HideLabels);
    }

    /// <summary>
    /// <see cref="DebugPanelState.ToggleHideLabels"/> must flip
    /// <see cref="MapUserConfig.HideLabels"/> from <c>false</c> to <c>true</c>.
    /// </summary>
    [Fact]
    public void ToggleHideLabels_FromFalse_SetsTrue()
    {
        var (state, config) = Create();
        config.HideLabels = false;

        state.ToggleHideLabels();

        Assert.True(config.HideLabels);
    }

    /// <summary>
    /// <see cref="DebugPanelState.ToggleHideLabels"/> must flip
    /// <see cref="MapUserConfig.HideLabels"/> from <c>true</c> to <c>false</c>.
    /// </summary>
    [Fact]
    public void ToggleHideLabels_FromTrue_SetsFalse()
    {
        var (state, config) = Create();
        config.HideLabels = true;

        state.ToggleHideLabels();

        Assert.False(config.HideLabels);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Independence of flags
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toggling <see cref="DebugPanelState.ForceHostile"/> must not affect
    /// <see cref="MapUserConfig.HideLabels"/>.
    /// </summary>
    [Fact]
    public void ToggleForceHostile_DoesNotAffectHideLabels()
    {
        var (state, config) = Create();
        config.HideLabels = false;

        state.ToggleForceHostile();

        Assert.False(config.HideLabels);
    }

    /// <summary>
    /// Toggling <see cref="DebugPanelState.HideLabels"/> must not affect
    /// <see cref="MapUserConfig.ForceHostile"/>.
    /// </summary>
    [Fact]
    public void ToggleHideLabels_DoesNotAffectForceHostile()
    {
        var (state, config) = Create();
        config.ForceHostile = false;

        state.ToggleHideLabels();

        Assert.False(config.ForceHostile);
    }
}
