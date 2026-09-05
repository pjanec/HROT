using System;
using System.Runtime.InteropServices;

namespace Fdp.Presentation.Input;

/// <summary>
/// Windows platform half of <see cref="ClickLatchCore"/>: watches the window's raw mouse-button
/// messages and replays the clicks a polled backend would otherwise drop (remote desktop —
/// TeamViewer, Parsec, RDP; see <see cref="ClickLatchCore"/> for why they are dropped).
///
/// <para>
/// The window procedure is <b>subclassed</b>, not replaced: every message is forwarded to the
/// original proc, so GLFW keeps behaving exactly as before. All this adds is observation.
/// </para>
///
/// <para>
/// Replayed clicks are stamped with <see cref="ExtraInfoTag"/> in the injected event's
/// <c>dwExtraInfo</c> and skipped on the way back in, so a replay cannot feed itself. Without
/// that tag the first replay would be observed as another lost click and the latch would spin.
/// </para>
///
/// <para>
/// ⭐ <b>Cross-platform safety.</b> Prefer <see cref="ClickLatch.Create"/> over constructing this
/// directly: it returns <see cref="NoOpClickLatch"/> off Windows. Constructing this type on Linux
/// is nonetheless safe — every Win32 entry point sits behind the
/// <see cref="OperatingSystem.IsWindows"/> guard in the constructor, so no <c>user32.dll</c> import
/// is ever resolved and <see cref="IsActive"/> stays <see langword="false"/>. That guard must stay
/// FIRST: <c>Process.MainWindowHandle</c> below throws <see cref="PlatformNotSupportedException"/>
/// on Unix.
/// </para>
///
/// <para>
/// Disabled entirely when <c>HROT_DISABLE_CLICK_LATCH=1</c> — a kill switch worth having on a Win32
/// hook, so a suspected input problem can be bisected without a rebuild.
/// </para>
/// </summary>
public sealed class Win32ClickLatch : IClickLatch
{
    /// <summary>Marker written into injected events so they are ignored on observation.</summary>
    public static readonly IntPtr ExtraInfoTag = new(0x484C4154);   // 'HLAT'

    private const int GWLP_WNDPROC = -4;

    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206;
    private const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MBUTTONDBLCLK = 0x0209;
    private const uint WM_KILLFOCUS   = 0x0008;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly IntPtr           _hWnd;
    private readonly ClickLatchCore   _core = new();
    private readonly WndProcDelegate  _proc;          // strong ref — must outlive the subclass
    private readonly IntPtr           _originalProc;
    private readonly object           _gate = new();
    private bool                      _disposed;

    /// <inheritdoc/>
    public bool IsActive { get; }

    /// <inheritdoc/>
    public int ReplayedClicks => _core.ReplayedClicks;

    /// <summary>
    /// Installs the latch. Pass a handle explicitly, or leave it default to resolve this process's
    /// main window — <c>Raylib.GetWindowHandle()</c> returns a <c>void*</c> and would force the
    /// caller into an <c>unsafe</c> block, which is not worth spreading for one interop detail.
    ///
    /// <para>Construction never throws: on any failure the latch simply stays inactive.</para>
    /// </summary>
    public Win32ClickLatch(IntPtr windowHandle = default)
    {
        _proc = WndProc;

        if (!OperatingSystem.IsWindows()) return;
        if (Environment.GetEnvironmentVariable("HROT_DISABLE_CLICK_LATCH") == "1") return;

        if (windowHandle == IntPtr.Zero)
        {
            try
            {
                using var self = System.Diagnostics.Process.GetCurrentProcess();
                windowHandle = self.MainWindowHandle;
            }
            catch (Exception) { return; }
        }
        if (windowHandle == IntPtr.Zero) return;

        _hWnd = windowHandle;
        try
        {
            var newProc  = Marshal.GetFunctionPointerForDelegate(_proc);
            _originalProc = SetWindowProc(_hWnd, newProc);
            IsActive = _originalProc != IntPtr.Zero;
        }
        catch (Exception)
        {
            // A hook that cannot be installed must not take the app down with it.
            IsActive = false;
        }
    }

    /// <inheritdoc/>
    public void Tick(bool leftDown, bool rightDown, bool middleDown)
    {
        if (!IsActive || _disposed) return;

        Span<bool> real = stackalloc bool[ClickLatchCore.ButtonCount];
        real[0] = leftDown; real[1] = rightDown; real[2] = middleDown;

        System.Collections.Generic.IReadOnlyList<LatchAction> actions;
        lock (_gate) { actions = _core.Tick(real); }

        for (int i = 0; i < actions.Count; i++) Perform(actions[i]);
    }

    // ── observation ──────────────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Our own replays carry the tag; observing them would make the latch feed itself.
        bool synthetic = GetMessageExtraInfo() == ExtraInfoTag;

        if (!synthetic)
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN: case WM_LBUTTONDBLCLK: Down(LatchButton.Left,   lParam); break;
                case WM_RBUTTONDOWN: case WM_RBUTTONDBLCLK: Down(LatchButton.Right,  lParam); break;
                case WM_MBUTTONDOWN: case WM_MBUTTONDBLCLK: Down(LatchButton.Middle, lParam); break;

                case WM_LBUTTONUP: Up(LatchButton.Left);   break;
                case WM_RBUTTONUP: Up(LatchButton.Right);  break;
                case WM_MBUTTONUP: Up(LatchButton.Middle); break;

                // Losing focus mid-gesture: drop everything, so a click observed before the
                // switch cannot replay into whatever is focused afterwards.
                case WM_KILLFOCUS: lock (_gate) { _core.Reset(); } break;
            }
        }

        return CallWindowProc(_originalProc, hWnd, msg, wParam, lParam);
    }

    private void Down(LatchButton b, IntPtr lParam)
    {
        // Client-space coords are packed as two SIGNED 16-bit values; a click can legitimately
        // report negative x/y while dragging outside the window, so sign-extend rather than mask.
        int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        lock (_gate) { _core.OnButtonDown(b, x, y); }
    }

    private void Up(LatchButton b)
    {
        lock (_gate) { _core.OnButtonUp(b); }
    }

    // ── replay ───────────────────────────────────────────────────────────────

    private void Perform(LatchAction action)
    {
        // ⛔ Deliberately does NOT move the cursor. Warping it to the recorded click position was a
        // defect: a replay could yank the pointer to a stale location and fire there. A remote tool
        // moves the cursor before it clicks, and the replay follows within a frame, so the cursor is
        // already where it needs to be. The position is carried on the action for diagnostics only.
        uint flags = action.Button switch
        {
            LatchButton.Left   => action.Kind == LatchActionKind.PressDown ? MOUSEEVENTF_LEFTDOWN   : MOUSEEVENTF_LEFTUP,
            LatchButton.Right  => action.Kind == LatchActionKind.PressDown ? MOUSEEVENTF_RIGHTDOWN  : MOUSEEVENTF_RIGHTUP,
            LatchButton.Middle => action.Kind == LatchActionKind.PressDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => 0u,
        };
        if (flags == 0) return;

        var input = new INPUT
        {
            type = 0, // INPUT_MOUSE
            mi = new MOUSEINPUT { dwFlags = flags, dwExtraInfo = ExtraInfoTag },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Restores the original window procedure.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!IsActive) return;
        try { SetWindowProc(_hWnd, _originalProc); } catch (Exception) { /* shutting down anyway */ }
    }

    // ── interop ──────────────────────────────────────────────────────────────

    private static IntPtr SetWindowProc(IntPtr hWnd, IntPtr proc)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, GWLP_WNDPROC, proc)
                            : SetWindowLong32(hWnd, GWLP_WNDPROC, proc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int    dx;
        public int    dy;
        public uint   mouseData;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint       type;
        public MOUSEINPUT mi;
    }
}
