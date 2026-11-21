using System;
using System.Collections.Generic;

namespace Fdp.Presentation.Input;

/// <summary>
/// Which mouse button a latch action refers to. Values match the Win32 / ImGui ordering.
/// </summary>
public enum LatchButton
{
    /// <summary>Left button.</summary>
    Left = 0,
    /// <summary>Right button.</summary>
    Right = 1,
    /// <summary>Middle button.</summary>
    Middle = 2,
}

/// <summary>What the latch wants the platform layer to do this frame.</summary>
public enum LatchActionKind
{
    /// <summary>Press the button down at <see cref="LatchAction.X"/>/<see cref="LatchAction.Y"/>.</summary>
    PressDown,
    /// <summary>Release the button.</summary>
    ReleaseUp,
}

/// <summary>One instruction for the platform layer, emitted by <see cref="ClickLatchCore.Tick"/>.</summary>
public readonly record struct LatchAction(LatchActionKind Kind, LatchButton Button, int X, int Y);

/// <summary>
/// Makes remote-desktop clicks survive a <b>polled</b> UI.
///
/// <para><b>The problem.</b> Raylib's frame does
/// <c>previousButtonState = currentButtonState</c> once, then <c>glfwPollEvents()</c> drains
/// <i>every</i> queued Windows message. TeamViewer, Parsec and friends inject
/// <c>WM_LBUTTONDOWN</c> and <c>WM_LBUTTONUP</c> microseconds apart, so both land in the
/// <b>same</b> drain: the state goes <c>0 → 1 → 0</c> and ends at <c>0</c>, with the previous
/// state also <c>0</c>. The frame therefore never observes a press — the click is not
/// mis-routed, it is <b>gone</b>. A physical click holds 50-100 ms and spans several frames, so
/// it always lands; that asymmetry is why remote clicking fails most of the time but not
/// always (the survivors are the taps that happen to straddle a poll boundary).</para>
///
/// <para><b>The fix.</b> Watch the raw button messages. When a down and its up arrive inside one
/// frame <i>and</i> the backend does not currently report the button held, that click was lost —
/// so replay it, holding the button down for <see cref="HoldFrames"/> frames. The backend then
/// sees an ordinary, slow click through its normal path. Nothing here reaches into ImGui's
/// per-frame state, so this cannot desync from whatever the backend does inside
/// <c>NewFrame()</c>.</para>
///
/// <para>
/// This type is deliberately free of Win32 and ImGui so the decision logic is testable
/// headlessly; <see cref="Win32ClickLatch"/> supplies the messages and performs the actions.
/// </para>
/// </summary>
public sealed class ClickLatchCore
{
    /// <summary>Number of buttons tracked (left, right, middle).</summary>
    public const int ButtonCount = 3;

    /// <summary>
    /// How many frames a replayed press is held. Two frames (~33 ms at 60 FPS) comfortably spans
    /// a poll boundary while staying far inside ImGui's default 300 ms double-click window, so two
    /// lost taps still replay as a double-click.
    /// </summary>
    public const int HoldFrames = 2;

    private sealed class ButtonState
    {
        // ⚠ THESE ARE PER-FRAME AND MUST BE CLEARED EVERY Tick.
        // Letting them accumulate across frames is the whole defect this comment exists to
        // prevent: a NORMAL click is a down on one frame and an up several frames later, so
        // cross-frame pairing treats every ordinary click as "lost" and replays it. Observed as
        // menus opening on press and closing again on release, and as replays landing at a
        // previous click's position.
        public int  DownsThisFrame;
        public int  UpsThisFrame;
        public readonly Queue<(int X, int Y)> DownPositionsThisFrame = new();

        /// <summary>Complete down+up pairs observed as lost, awaiting replay.</summary>
        public readonly Queue<(int X, int Y)> Pending = new();

        public bool Injecting;
        public int  HoldRemaining;
        public int  Cooldown;
        public int  ActiveX;
        public int  ActiveY;
    }

    private readonly ButtonState[] _buttons = CreateStates();

    private static ButtonState[] CreateStates()
    {
        var a = new ButtonState[ButtonCount];
        for (int i = 0; i < a.Length; i++) a[i] = new ButtonState();
        return a;
    }

    /// <summary>Total clicks this latch has replayed. Diagnostics only.</summary>
    public int ReplayedClicks { get; private set; }

    // ── message intake ───────────────────────────────────────────────────────

    /// <summary>
    /// Records a raw button-down. <paramref name="x"/>/<paramref name="y"/> are client-space
    /// coordinates from the message, used so the replay lands where the user actually clicked
    /// even if the cursor has since moved.
    /// </summary>
    public void OnButtonDown(LatchButton button, int x, int y)
    {
        if (!TryGet(button, out var s)) return;
        s.DownsThisFrame++;
        s.DownPositionsThisFrame.Enqueue((x, y));
    }

    /// <summary>Records a raw button-up.</summary>
    public void OnButtonUp(LatchButton button)
    {
        if (!TryGet(button, out var s)) return;
        s.UpsThisFrame++;
    }

    // ── per-frame decision ───────────────────────────────────────────────────

    /// <summary>
    /// Call once per frame, before the backend polls input.
    /// </summary>
    /// <param name="backendReportsDown">
    /// What the polling backend currently believes, per button. A <c>true</c> here means a real
    /// press is being handled normally, so anything pending for that button is discarded rather
    /// than replayed — this is what stops a click that DID land from being duplicated.
    /// </param>
    /// <returns>Actions for the platform layer to perform, in order. Empty on most frames.</returns>
    public IReadOnlyList<LatchAction> Tick(ReadOnlySpan<bool> backendReportsDown)
    {
        List<LatchAction>? actions = null;

        for (int i = 0; i < ButtonCount; i++)
        {
            var s      = _buttons[i];
            var button = (LatchButton)i;
            bool realDown = i < backendReportsDown.Length && backendReportsDown[i];

            // A click is LOST only when its down AND its up both arrived inside THIS one frame —
            // that is precisely the case the backend cannot see. Pair those, then discard the
            // remainder unconditionally:
            //   * an unpaired down  -> the button is still held; the backend is handling it
            //   * an unpaired up    -> its down was on an earlier frame, so the backend saw that too
            // ⚠ Carrying either across frames turns every ordinary click into a false "lost" click.
            int pairs = Math.Min(s.DownsThisFrame, s.UpsThisFrame);
            for (int p = 0; p < pairs; p++)
            {
                var pos = s.DownPositionsThisFrame.Count > 0
                    ? s.DownPositionsThisFrame.Dequeue()
                    : (0, 0);
                s.Pending.Enqueue(pos);
            }
            s.DownsThisFrame = 0;
            s.UpsThisFrame   = 0;
            s.DownPositionsThisFrame.Clear();

            if (s.Injecting)
            {
                // Mid-replay. Hold, then release.
                if (--s.HoldRemaining <= 0)
                {
                    (actions ??= new()).Add(
                        new LatchAction(LatchActionKind.ReleaseUp, button, s.ActiveX, s.ActiveY));
                    s.Injecting = false;
                    s.Cooldown  = 1;   // one clear frame, so two replays read as two clicks
                }
                continue;
            }

            if (s.Cooldown > 0)
            {
                s.Cooldown--;
                continue;
            }

            if (realDown)
            {
                // A genuine press is in flight (or a straddling click the backend caught).
                // Whatever we paired belongs to it — drop it rather than replay a duplicate.
                s.Pending.Clear();
                continue;
            }

            if (s.Pending.Count > 0)
            {
                var (x, y) = s.Pending.Dequeue();
                s.ActiveX = x;
                s.ActiveY = y;
                s.Injecting     = true;
                s.HoldRemaining = HoldFrames;
                ReplayedClicks++;
                (actions ??= new()).Add(new LatchAction(LatchActionKind.PressDown, button, x, y));
            }
        }

        return actions ?? (IReadOnlyList<LatchAction>)Array.Empty<LatchAction>();
    }

    /// <summary>
    /// Drops all accumulated and in-flight state. Use when the window loses focus, so a click
    /// observed before the switch cannot replay into an unrelated surface afterwards.
    /// </summary>
    public void Reset()
    {
        foreach (var s in _buttons)
        {
            s.DownsThisFrame = 0;
            s.UpsThisFrame   = 0;
            s.DownPositionsThisFrame.Clear();
            s.Pending.Clear();
            s.Injecting     = false;
            s.HoldRemaining = 0;
            s.Cooldown      = 0;
        }
    }

    /// <summary>True while a replay is in flight for <paramref name="button"/>.</summary>
    public bool IsReplaying(LatchButton button) => TryGet(button, out var s) && s.Injecting;

    private bool TryGet(LatchButton button, out ButtonState state)
    {
        int i = (int)button;
        if (i < 0 || i >= ButtonCount) { state = null!; return false; }
        state = _buttons[i];
        return true;
    }
}
