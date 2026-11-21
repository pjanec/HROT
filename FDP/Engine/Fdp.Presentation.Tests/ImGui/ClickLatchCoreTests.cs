using Fdp.Presentation.Input;

namespace Fdp.Presentation.Tests.ImGui;

/// <summary>
/// Tests for <see cref="ClickLatchCore"/> — the decision half of the remote-click fix.
///
/// <para>
/// The behaviour that matters: a click whose down and up both land inside one frame is LOST by a
/// polled backend and must be replayed; a click the backend actually saw must NOT be, or every
/// remote click would fire twice.
/// </para>
/// </summary>
public sealed class ClickLatchCoreTests
{
    private static bool[] NoneDown() => new[] { false, false, false };
    private static bool[] LeftDown() => new[] { true, false, false };

    /// <summary>Runs frames until the replay completes, returning every action in order.</summary>
    private static List<LatchAction> Drain(ClickLatchCore core, int frames, bool[]? realDown = null)
    {
        var all = new List<LatchAction>();
        for (int i = 0; i < frames; i++)
            all.AddRange(core.Tick(realDown ?? NoneDown()));
        return all;
    }

    // ── the defect ───────────────────────────────────────────────────────────

    [Fact]
    public void DownAndUpInOneFrame_IsReplayedAsAHeldClick()
    {
        var core = new ClickLatchCore();

        // What a remote tap looks like: both messages before the next frame.
        core.OnButtonDown(LatchButton.Left, 120, 40);
        core.OnButtonUp(LatchButton.Left);

        var actions = Drain(core, ClickLatchCore.HoldFrames + 2);

        Assert.Equal(2, actions.Count);
        Assert.Equal(LatchActionKind.PressDown, actions[0].Kind);
        Assert.Equal(LatchActionKind.ReleaseUp, actions[1].Kind);
        Assert.Equal(LatchButton.Left, actions[0].Button);
        Assert.Equal(1, core.ReplayedClicks);
    }

    [Fact]
    public void TheReplayUsesTheClicksOwnPosition_NotWhereverTheCursorEndedUp()
    {
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 733, 219);
        core.OnButtonUp(LatchButton.Left);

        var press = Drain(core, 1).Single();

        Assert.Equal(733, press.X);
        Assert.Equal(219, press.Y);
    }

    [Fact]
    public void TheReplayIsHeldForMoreThanOneFrame()
    {
        // The whole point: one frame of "down" is what the backend already misses.
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 1, 1);
        core.OnButtonUp(LatchButton.Left);

        Assert.Single(core.Tick(NoneDown()));            // press
        Assert.True(core.IsReplaying(LatchButton.Left));

        for (int i = 1; i < ClickLatchCore.HoldFrames; i++)
        {
            Assert.Empty(core.Tick(NoneDown()));         // still held
            Assert.True(core.IsReplaying(LatchButton.Left));
        }

        var release = core.Tick(NoneDown()).Single();
        Assert.Equal(LatchActionKind.ReleaseUp, release.Kind);
        Assert.False(core.IsReplaying(LatchButton.Left));
    }

    // ── must NOT double-fire ─────────────────────────────────────────────────

    [Fact]
    public void AClickTheBackendSaw_IsNotReplayed()
    {
        // The ~1-in-10 tap that straddles a poll boundary: the backend reports the button down,
        // so it is handling the click itself. Replaying would fire it twice.
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 10, 10);
        core.OnButtonUp(LatchButton.Left);

        Assert.Empty(core.Tick(LeftDown()));
        Assert.Empty(Drain(core, 5));
        Assert.Equal(0, core.ReplayedClicks);
    }

    [Fact]
    public void AHeldButton_IsLeftAloneEntirely()
    {
        // A real press-and-hold (or a drag): down with no up. Nothing to replay.
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 5, 5);

        Assert.Empty(Drain(core, 5, LeftDown()));
        Assert.Equal(0, core.ReplayedClicks);
    }

    /// <summary>
    /// ⭐ THE REGRESSION. An ordinary click is a down on one frame and an up several frames later.
    /// The first version paired those across frames, so every normal click was replayed: the user's
    /// click opened a menu and the phantom replay closed it again, sometimes at a previous click's
    /// position. Pairing is now scoped to a single frame.
    /// </summary>
    [Fact]
    public void ANormalSlowClick_IsNeverReplayed()
    {
        var core = new ClickLatchCore();

        // Frame 1: the down arrives; the backend now reports the button held.
        core.OnButtonDown(LatchButton.Left, 7, 7);
        Assert.Empty(core.Tick(LeftDown()));

        // Frames 2-3: still held, nothing new.
        Assert.Empty(core.Tick(LeftDown()));
        Assert.Empty(core.Tick(LeftDown()));

        // Frame 4: the up arrives, on a LATER frame than the down.
        core.OnButtonUp(LatchButton.Left);
        Assert.Empty(core.Tick(NoneDown()));

        // ...and nothing may surface afterwards either.
        Assert.Empty(Drain(core, 10));
        Assert.Equal(0, core.ReplayedClicks);
    }

    [Fact]
    public void ANormalClick_IsNotReplayedEvenIfTheBackendNeverReportsItDown()
    {
        // Belt and braces: the frame scoping alone must be sufficient, without leaning on the
        // backend's own state. Down and up on DIFFERENT frames is not a lost click, full stop.
        var core = new ClickLatchCore();

        core.OnButtonDown(LatchButton.Left, 7, 7);
        Assert.Empty(core.Tick(NoneDown()));

        core.OnButtonUp(LatchButton.Left);
        Assert.Empty(core.Tick(NoneDown()));

        Assert.Empty(Drain(core, 5));
        Assert.Equal(0, core.ReplayedClicks);
    }

    [Fact]
    public void AnUnpairedUp_IsDiscardedRatherThanHeldForAFutureDown()
    {
        // A stray up (its down was on an earlier frame) must not sit in the state waiting to pair
        // with the NEXT down — that would replay a click the user never finished making.
        var core = new ClickLatchCore();

        core.OnButtonUp(LatchButton.Left);
        Assert.Empty(core.Tick(NoneDown()));

        core.OnButtonDown(LatchButton.Left, 1, 1);
        Assert.Empty(core.Tick(LeftDown()));

        Assert.Empty(Drain(core, 5));
        Assert.Equal(0, core.ReplayedClicks);
    }

    [Fact]
    public void PressAndHold_AcrossManyFrames_ThenRelease_IsNeverReplayed()
    {
        // The manual workaround people use over TeamViewer today. It must keep working untouched.
        var core = new ClickLatchCore();

        core.OnButtonDown(LatchButton.Left, 4, 4);
        for (int i = 0; i < 20; i++) Assert.Empty(core.Tick(LeftDown()));

        core.OnButtonUp(LatchButton.Left);
        Assert.Empty(core.Tick(NoneDown()));
        Assert.Equal(0, core.ReplayedClicks);
    }

    [Fact]
    public void ADragThenReleaseElsewhere_IsNeverReplayed()
    {
        var core = new ClickLatchCore();

        core.OnButtonDown(LatchButton.Left, 10, 10);
        Assert.Empty(core.Tick(LeftDown()));
        for (int i = 0; i < 5; i++) Assert.Empty(core.Tick(LeftDown()));   // dragging

        core.OnButtonUp(LatchButton.Left);                                  // released far away
        Assert.Empty(core.Tick(NoneDown()));
        Assert.Equal(0, core.ReplayedClicks);
    }

    // ── double-click ─────────────────────────────────────────────────────────

    [Fact]
    public void TwoLostTaps_ReplayAsTwoSeparateClicks_WithAClearFrameBetween()
    {
        // Double-click is the gesture the visual check needs (D2/D3), so two taps collapsing into
        // one replay would be a silent downgrade.
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 50, 60);
        core.OnButtonUp(LatchButton.Left);
        core.OnButtonDown(LatchButton.Left, 50, 60);
        core.OnButtonUp(LatchButton.Left);

        var actions = Drain(core, 12);

        Assert.Equal(2, core.ReplayedClicks);
        Assert.Equal(4, actions.Count);
        Assert.Equal(LatchActionKind.PressDown, actions[0].Kind);
        Assert.Equal(LatchActionKind.ReleaseUp, actions[1].Kind);
        Assert.Equal(LatchActionKind.PressDown, actions[2].Kind);
        Assert.Equal(LatchActionKind.ReleaseUp, actions[3].Kind);
    }

    [Fact]
    public void TwoLostTaps_ReplayFastEnoughToCountAsADoubleClick()
    {
        // ImGui's default double-click window is 300 ms. Count the frames the pair costs and
        // assert it fits at 60 FPS -- otherwise the replay is correct but useless for D2.
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 1, 1);
        core.OnButtonUp(LatchButton.Left);
        core.OnButtonDown(LatchButton.Left, 1, 1);
        core.OnButtonUp(LatchButton.Left);

        int frames = 0;
        while (core.ReplayedClicks < 2 || core.IsReplaying(LatchButton.Left))
        {
            core.Tick(NoneDown());
            if (++frames > 100) break;   // guard against a stuck machine
        }

        Assert.True(frames * (1000.0 / 60.0) < 300.0,
            $"two replays took {frames} frames (~{frames * 1000.0 / 60.0:F0} ms) -- outside ImGui's 300 ms double-click window");
    }

    // ── other buttons, and isolation ─────────────────────────────────────────

    [Theory]
    [InlineData(LatchButton.Right)]
    [InlineData(LatchButton.Middle)]
    public void RightAndMiddleAreLatchedToo(LatchButton button)
    {
        // Right-click is how the Details row menu opens, so it matters as much as left.
        var core = new ClickLatchCore();
        core.OnButtonDown(button, 3, 4);
        core.OnButtonUp(button);

        var press = Drain(core, 1).Single();
        Assert.Equal(button, press.Button);
    }

    [Fact]
    public void ButtonsDoNotInterfereWithEachOther()
    {
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 1, 1);
        core.OnButtonUp(LatchButton.Left);
        core.OnButtonDown(LatchButton.Right, 2, 2);
        core.OnButtonUp(LatchButton.Right);

        var first = core.Tick(NoneDown());

        Assert.Equal(2, first.Count);            // both press on the same frame
        Assert.Contains(first, a => a.Button == LatchButton.Left);
        Assert.Contains(first, a => a.Button == LatchButton.Right);
    }

    [Fact]
    public void ALeftClickIsStillReplayedWhileTheRightButtonIsGenuinelyHeld()
    {
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 9, 9);
        core.OnButtonUp(LatchButton.Left);

        var press = core.Tick(new[] { false, true, false }).Single();   // right held, left not

        Assert.Equal(LatchButton.Left, press.Button);
    }

    // ── housekeeping ─────────────────────────────────────────────────────────

    [Fact]
    public void Reset_DropsPendingWork_SoAClickCannotReplayAfterFocusMoves()
    {
        var core = new ClickLatchCore();
        core.OnButtonDown(LatchButton.Left, 1, 1);
        core.OnButtonUp(LatchButton.Left);

        core.Reset();

        Assert.Empty(Drain(core, 5));
        Assert.Equal(0, core.ReplayedClicks);
    }

    [Fact]
    public void QuietFrames_EmitNothingAndAllocateNoActions()
    {
        var core = new ClickLatchCore();
        for (int i = 0; i < 100; i++) Assert.Empty(core.Tick(NoneDown()));
    }
}

/// <summary>
/// Cross-platform safety of the latch wrapper. The inert path exercised here is the one Linux
/// takes, so these run identically on both and are the guard against the Win32 half being reached
/// where there is no Win32.
/// </summary>
public sealed class ClickLatchPlatformTests
{
    [Fact]
    public void Create_ReturnsSomethingUsable_OnWhateverPlatformTheTestsRunOn()
    {
        using var latch = ClickLatch.Create();

        Assert.NotNull(latch);
        latch.Tick(false, false, false);          // must not throw
        Assert.True(latch.ReplayedClicks >= 0);
    }

    [Fact]
    public void OffWindows_CreateYieldsAnInertLatch()
    {
        using var latch = ClickLatch.Create();

        if (!OperatingSystem.IsWindows())
        {
            Assert.IsType<NoOpClickLatch>(latch);
            Assert.False(latch.IsActive);
        }
        else
        {
            // On Windows it may legitimately be either, depending on the kill switch and whether
            // a window exists in the test host — the contract is only that it is usable.
            Assert.True(latch is Win32ClickLatch or NoOpClickLatch);
        }
    }

    [Fact]
    public void NoOpLatch_IsSafeToTickAndDisposeRepeatedly()
    {
        // `using var` on the shared instance disposes it; that must stay harmless.
        var latch = NoOpClickLatch.Instance;

        latch.Tick(true, true, true);
        latch.Dispose();
        latch.Dispose();
        latch.Tick(false, false, false);

        Assert.False(latch.IsActive);
        Assert.Equal(0, latch.ReplayedClicks);
    }

    [Fact]
    public void KillSwitch_ProducesAnInactiveLatchWhoseTickAndDisposeTouchNoInterop()
    {
        // This is also the Linux shape: IsActive == false, and every method returns before any
        // user32 entry point would be resolved.
        const string key = "HROT_DISABLE_CLICK_LATCH";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "1");

            using var latch = new Win32ClickLatch();

            Assert.False(latch.IsActive);
            latch.Tick(true, true, true);      // must be a no-op, not a P/Invoke
            Assert.Equal(0, latch.ReplayedClicks);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public void ConstructingTheWin32LatchWithAnInvalidHandle_DoesNotThrow()
    {
        using var latch = new Win32ClickLatch(new IntPtr(-1));

        latch.Tick(false, false, false);
        Assert.True(latch.ReplayedClicks >= 0);
    }
}
