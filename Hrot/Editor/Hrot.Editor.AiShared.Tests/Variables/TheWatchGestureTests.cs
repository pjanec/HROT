using System;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94f</c>) — the variable-watch gesture, as a DECISION.</b>
///
/// <para>⭐⭐ <b>The rule is asserted here, not in a draw path</b> — 📌 <c>R-21</c>/<c>R-62</c>: no
/// headless rail can drive ImGui, so the two surfaces render what
/// <see cref="VariableWatchGesture.Decide"/> returns and this pins what it returns.</para>
///
/// <para>⛔⛔ <b>Every refusal carries a REASON</b> — 📌 the user's own rule: <i>"same information
/// value, no false expectations."</i> ⭐ A greyed entry with no explanation is the failure this
/// prevents, not the goal.</para>
/// </summary>
public sealed class TheWatchGestureTests
{
    private static readonly Guid AssetId = new("eeeeeeee-0000-0000-0000-00000000000e");

    private static VariableRow Row(bool stale = false) => new(
        Origin:    new VariableRowOrigin(AssetId, default, "s", "Health", "Alpha"),
        ShortName: "Health", TypeText: "int", ClrType: typeof(int),
        ReadValue: () => Array.Empty<byte>(),
        IsStale:   stale);

    // ══ the command id — BP-346 ══════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The id is DISTINCT from the pin-scoped one.</b> 📐 <c>CommandCatalog.ToggleWatch =
    /// "editor.toggle-watch"</c> exists and means <i>watch this canvas PIN</i>
    /// *(<c>IDebugSession.ToggleWatch(PinId)</c>)* — ⛔ reusing it would silently bind the VARIABLE
    /// gesture to the pin-watch command. 📌 <c>BP-346</c>, the trap Batch 93 named.
    /// </summary>
    [Fact]
    public void TheCommandIdIsNotThePinScopedOne()
    {
        Assert.Equal("editor.toggle-variable-watch", VariableWatchGesture.CommandId);
        Assert.NotEqual("editor.toggle-watch", VariableWatchGesture.CommandId);
    }

    /// <summary>⭐ Both surfaces must spell the SAME id — ⛔ two literals is how they drift apart.</summary>
    [Fact]
    public void BothEntryPointsUseTheSameCommandId()
    {
        // ⚠ By reflection: MyBlueprintContextMenu is internal to NodeEditor.UI, and AiShared must
        //   not reference it the other way round. ⭐ The rail still fails if either side changes.
        var menuType = typeof(NodeEditor.UI.Panels.MyBlueprintPanel).Assembly
            .GetType("NodeEditor.UI.Panels.MyBlueprintContextMenu");
        Assert.NotNull(menuType);

        var outlineId = menuType!
            .GetField("ToggleVariableWatchCommandId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetRawConstantValue();

        Assert.Equal(VariableWatchGesture.CommandId, outlineId);
    }

    // ══ when it is allowed ═══════════════════════════════════════════════════

    /// <summary>⭐⭐ <b>Planning</b> ✅ and <b>Paused</b> ✅ — 📌 spec §7.</summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Paused)]
    public void PinningIsAllowedWhilePlanningAndWhilePaused(VariableRunState state)
    {
        var g = VariableWatchGesture.Decide(Row(), state, isPinned: false);

        Assert.True(g.Enabled);
        Assert.Null(g.DisabledReason);
        Assert.Equal(VariableWatchGesture.WatchLabel, g.Label);
    }

    // ══ when it is refused — and WHY ═════════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>Free-running is FORBIDDEN</b> *(spec §7)*, ⭐ and the reason is actionable: pinning takes
    /// a baseline sample, and one taken while the world moves was already stale when it was read.
    /// </summary>
    [Fact]
    public void PinningIsRefusedWhileFreeRunningWithAReason()
    {
        var g = VariableWatchGesture.Decide(Row(), VariableRunState.Running, isPinned: false);

        Assert.False(g.Enabled);
        Assert.Contains("pause", g.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>⛔ <b>Replay is FORBIDDEN</b> *(spec §7)* — there is no live value to watch.</summary>
    [Fact]
    public void PinningIsRefusedDuringReplayWithAReason()
    {
        var g = VariableWatchGesture.Decide(Row(), VariableRunState.Replay, isPinned: false);

        Assert.False(g.Enabled);
        Assert.Contains("replay", g.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>⚠ A STALE row cannot be pinned — its asset or entity is gone.</summary>
    [Fact]
    public void AStaleRowCannotBePinned()
    {
        var g = VariableWatchGesture.Decide(Row(stale: true), VariableRunState.Paused, isPinned: false);

        Assert.False(g.Enabled);
        Assert.NotNull(g.DisabledReason);
    }

    /// <summary>⭐⭐⭐ <b>Every refusal has a reason, and every allowance has none</b> — the invariant
    /// the user's rule reduces to. ⛔ A greyed entry with no explanation is the defect.</summary>
    [Theory]
    [InlineData(VariableRunState.Planning, false)]
    [InlineData(VariableRunState.Running,  false)]
    [InlineData(VariableRunState.Paused,   false)]
    [InlineData(VariableRunState.Replay,   false)]
    [InlineData(VariableRunState.Planning, true)]
    [InlineData(VariableRunState.Running,  true)]
    [InlineData(VariableRunState.Paused,   true)]
    [InlineData(VariableRunState.Replay,   true)]
    public void ARefusalAlwaysCarriesAReasonAndAnAllowanceNeverDoes(
        VariableRunState state, bool isPinned)
    {
        var g = VariableWatchGesture.Decide(Row(), state, isPinned);

        Assert.Equal(g.Enabled, g.DisabledReason is null);
        Assert.False(string.IsNullOrWhiteSpace(g.Label));
    }

    // ══ it is a TOGGLE ═══════════════════════════════════════════════════════

    /// <summary>⭐ One command, two labels — ⛔ not two commands.</summary>
    [Fact]
    public void ThePinnedRowOffersToStopWatching()
    {
        Assert.Equal(VariableWatchGesture.UnwatchLabel,
            VariableWatchGesture.Decide(Row(), VariableRunState.Paused, isPinned: true).Label);
    }

    /// <summary>
    /// ⭐⭐ <b>UNPINNING is always allowed</b>, in every run state and even for a stale row.
    /// ⛔ Otherwise a row pinned before a run started could not be removed until the run stopped,
    /// which is a trap rather than a safeguard.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Running)]
    [InlineData(VariableRunState.Paused)]
    [InlineData(VariableRunState.Replay)]
    public void UnpinningIsAlwaysAllowed(VariableRunState state)
    {
        Assert.True(VariableWatchGesture.Decide(Row(stale: true), state, isPinned: true).Enabled);
    }
}
