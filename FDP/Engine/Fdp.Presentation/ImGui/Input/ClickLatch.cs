using System;

namespace Fdp.Presentation.Input;

/// <summary>
/// Per-frame hook that replays mouse clicks a polled backend would otherwise drop.
/// See <see cref="ClickLatchCore"/> for why they are dropped.
/// </summary>
public interface IClickLatch : IDisposable
{
    /// <summary>True when the latch is installed and observing input.</summary>
    bool IsActive { get; }

    /// <summary>How many clicks have been replayed. Diagnostics only.</summary>
    int ReplayedClicks { get; }

    /// <summary>
    /// Call once per frame, BEFORE the backend polls input, passing what the backend currently
    /// believes about each button.
    /// </summary>
    void Tick(bool leftDown, bool rightDown, bool middleDown);
}

/// <summary>
/// Chooses a click latch for the running platform.
///
/// <para>
/// The lost-click problem is caused by message-injecting remote-desktop tools on Windows
/// (TeamViewer, Parsec, RDP). Elsewhere there is nothing to compensate for and no Win32 to do it
/// with, so the latch is a no-op — the render loop calls <see cref="Create"/> unconditionally and
/// does not branch on the platform itself.
/// </para>
/// </summary>
public static class ClickLatch
{
    /// <summary>
    /// Creates the latch for this platform: a real one on Windows, an inert one everywhere else.
    /// Never throws — a latch that cannot install must not stop the app from starting.
    /// </summary>
    public static IClickLatch Create()
    {
        if (!OperatingSystem.IsWindows()) return NoOpClickLatch.Instance;

        try { return new Win32ClickLatch(); }
        catch (Exception) { return NoOpClickLatch.Instance; }
    }

    /// <summary>
    /// ⭐⭐⭐ Creates the latch bound to the <b>raylib</b> window explicitly, rather than to whichever
    /// top-level window <c>Process.MainWindowHandle</c> happens to return.
    ///
    /// <para>🔴 <b>Why this overload exists (<c>CE-297</c>).</b> <see cref="Create"/> resolves the
    /// process's MAIN window, which is correct only when the raylib window IS the main one — true of
    /// clusterrunner and <c>FdpApplication</c>, and <b>false in the Stride host</b>, which owns a
    /// Direct3D window of its own alongside the raylib editor window. ⛔ Binding the latch to the
    /// wrong window is a SILENT no-op: it installs, reports <c>IsActive</c>, observes messages nobody
    /// is losing, and the editor's clicks keep vanishing.</para>
    ///
    /// <para>⚠ Must be called AFTER <c>Raylib.InitWindow</c> — before it there is no handle and this
    /// returns an inert latch. ⭐ <c>Raylib.GetWindowHandle()</c> returns a <c>void*</c>, so the
    /// <c>unsafe</c> block lives here, once, instead of in every host that needs one.</para>
    /// </summary>
    public static IClickLatch CreateForRaylibWindow()
    {
        if (!OperatingSystem.IsWindows()) return NoOpClickLatch.Instance;

        try
        {
            IntPtr hWnd;
            unsafe { hWnd = (IntPtr)Raylib_cs.Raylib.GetWindowHandle(); }
            if (hWnd == IntPtr.Zero) return NoOpClickLatch.Instance;

            return new Win32ClickLatch(hWnd);
        }
        catch (Exception) { return NoOpClickLatch.Instance; }
    }
}

/// <summary>
/// Does nothing, for platforms with no lost-click problem to solve. Also the fallback when the
/// Windows latch cannot install.
/// </summary>
public sealed class NoOpClickLatch : IClickLatch
{
    /// <summary>The shared instance. Stateless, so one is enough.</summary>
    public static readonly NoOpClickLatch Instance = new();

    /// <inheritdoc/>
    public bool IsActive => false;

    /// <inheritdoc/>
    public int ReplayedClicks => 0;

    /// <inheritdoc/>
    public void Tick(bool leftDown, bool rightDown, bool middleDown) { }

    /// <summary>No-op. Safe to call repeatedly, including on the shared instance.</summary>
    public void Dispose() { }
}
