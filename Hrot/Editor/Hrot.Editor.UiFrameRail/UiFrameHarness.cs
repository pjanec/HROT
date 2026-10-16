using System;
using System.Threading;

namespace Hrot.Editor.UiFrameRail;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100a</c>) — THE FRAME RAIL: real ImGui frames, in a test.</b>
///
/// <para>📌 <b><c>R-124</c></b>. ⛔⛔ <b>This supersedes the premise of <c>R-21</c>/<c>R-62</c></b>
/// *("no headless rail can drive ImGui")* — ⚠ <b>which were TRUE when written</b>, and everything built
/// under them was correct at the time. ⭐ The stack is <b>Raylib-cs 7.0.2 + rlImgui-cs 3.2.0</b>, this
/// machine has <b>Xvfb + Mesa software GL</b> ⇒ a real frame can be rendered and measured.</para>
///
/// <para>🔴🔴 <b>Why it had to exist.</b> Five consecutive batches shipped <c>3852 / 0</c> green while the
/// feature was dead, because <b>every defect lived in the one region no rail could reach</b> — a popup
/// width, an ignored <c>ref</c> flag, a missing <c>Draw()</c> call. ⛔ Not carelessness: the instrument
/// was missing.</para>
///
/// <para>⭐⭐⭐ <b>It is NOT a screenshot-diff harness.</b> 📌 <c>R-124</c>: <i>"the strongest form needs no
/// image comparison."</i> ⭐ The assertions are ORDINARY assertions that happen to run inside a rendered
/// frame — <c>GetContentRegionAvail()</c>, <c>IsPopupOpen(id)</c>, <c>GetItemRectSize()</c>.
/// ⚠ <see cref="UiFrameSession.Screenshot"/> exists for <b>evidence a human can look at</b>, ⛔ never as
/// a gate: font and driver drift make image comparison brittle.</para>
///
/// <para>⛔ <b>What it still does NOT do: simulate a human.</b> ⭐ It does not have to — every defect it
/// was built for is <b>state → draw</b>, so a rail puts the app in a state PROGRAMMATICALLY and then
/// renders. ⚠ Input simulation is a separate, later question.</para>
/// </summary>
public static class UiFrameHarness
{
    /// <summary>
    /// ⛔⛔ <b>Raylib is NOT re-entrant — one window per PROCESS.</b> ⚠ Two concurrent sessions crash the
    /// test host, so <see cref="Begin"/> takes this and <see cref="UiFrameSession.Dispose"/> releases it.
    /// ⭐ xUnit callers must ALSO serialise with a collection: this lock turns a crash into a hang-free
    /// wait, ⛔ it does not make parallel rendering work.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>⭐ Cached so the availability probe's cost and its log noise are paid once.</summary>
    private static bool?  _available;
    private static string _unavailableReason = "";

    /// <summary>
    /// ⭐⭐ Why <see cref="IsAvailable"/> said no. ⛔ Empty when it said yes.
    /// ⭐ A skip MUST print this — 📌 <i>"a skip that hides a real failure is worse than no rail."</i>
    /// </summary>
    public static string UnavailableReason => _unavailableReason;

    /// <summary>
    /// ⭐⭐⭐ <b>Ask FIRST, and SKIP — never fail — when the answer is no.</b>
    ///
    /// <para>⛔ A dev box or a CI leg with no display must not go red for lacking a GPU. ⚠ But the
    /// converse is the real hazard: ⛔⛔ <b>a rail that always skips is not a rail</b>, which is why the
    /// gate report must carry <b>RAN and SKIPPED counts</b> and <i>"all skipped"</i> is a FINDING.</para>
    ///
    /// <para>📐 <b>Probed by actually opening a window</b>, not by reading <c>DISPLAY</c> — ⚠ a set
    /// <c>DISPLAY</c> pointing at a dead X server is exactly the case that would produce a crash rather
    /// than a skip.</para>
    /// </summary>
    public static bool IsAvailable()
    {
        if (_available is { } cached) return cached;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            _unavailableReason =
                "no DISPLAY — run under `xvfb-run -a -s \"-screen 0 1280x800x24\" dotnet test …`";
            _available = false;
            return false;
        }

        // ⭐ CE-1039 — the probe opens the window the sessions will use (kept on Linux), under the gate like a session.
        Gate.Wait();
        try
        {
            Raylib_cs.Raylib.SetTraceLogLevel(Raylib_cs.TraceLogLevel.Error);
            OpenOrBind(DefaultWidth, DefaultHeight);
            Leave();
            _available = true;
            return true;
        }
        catch (Exception ex)
        {
            // ⚠ A missing native libraylib is a DllNotFoundException, not a return value; a window that never became ready
            //   is the InvalidOperationException OpenOrBind throws.
            _unavailableReason = ex is InvalidOperationException
                ? "DISPLAY is set but Raylib could not create a window (no GL?)"
                : $"{ex.GetType().Name}: {ex.Message}";
            _available = false;
            return false;
        }
        finally { Gate.Release(); }
    }

    // ── CE-1039: ONE window per process ───────────────────────────────────────

    /// <summary>The size every rail asks for (<see cref="Begin"/>'s default).</summary>
    public const int DefaultWidth = 1280, DefaultHeight = 800;

    /// <summary>
    /// ⭐⭐ <b><c>CE-1039</c> — the window is KEPT for the life of the test process (Linux / GLX), not opened and closed per rail.</b>
    /// 📐 Measured from a core dump: the test host that aborted "between tests" died with SIGSEGV in
    /// <c>InitWindow → rlglInit → rlLoadTexture</c> calling a NULL GL function — a fresh window whose context came up unusable
    /// (GLAD loaded nothing), which under Xvfb/Mesa happened about once per few hundred window creations, i.e. once in four or
    /// five suite runs at ~25 windows a run. ⇒ one creation per process instead. A GL context is current on ONE thread and
    /// xUnit moves rails across pool threads, so a session BINDS the kept context on its own thread and RELEASES it when it ends
    /// (<see cref="GlxContext"/>). Elsewhere (no GLX: Windows) each session still opens and closes its own window.
    /// </summary>
    private static GlxContext.Binding? _keptWindow;
    private static int _windowWidth, _windowHeight;

    /// <summary>True when the window is being kept between sessions (rails).</summary>
    public static bool KeepsWindow => _keptWindow is not null;

    /// <summary>How many windows this process has created (rails).</summary>
    public static int WindowsCreated { get; private set; }

    /// <summary>Opens the window, or binds the kept one, on THIS thread at <paramref name="width"/> × <paramref name="height"/>.
    /// ⛔ Call under <see cref="Gate"/>.</summary>
    internal static void OpenOrBind(int width, int height)
    {
        if (_keptWindow is { } kept)
        {
            if (!GlxContext.Bind(kept))
                throw new InvalidOperationException("the kept window's GL context could not be bound on this thread");
            if (width == _windowWidth && height == _windowHeight) return;
            Raylib_cs.Raylib.CloseWindow();   // another size: a new window (rare — the rails all ask for the default)
            _keptWindow = null;
        }

        Raylib_cs.Raylib.InitWindow(width, height, "hrot-uiframe-rail");
        WindowsCreated++;
        if (!Raylib_cs.Raylib.IsWindowReady())
        {
            Raylib_cs.Raylib.CloseWindow();
            throw new InvalidOperationException("Raylib window did not become ready.");
        }
        _windowWidth = width;
        _windowHeight = height;
        if (OperatingSystem.IsLinux()) _keptWindow = GlxContext.Current();
    }

    /// <summary>Ends a session's use of the window: releases the kept one's context, or closes the window.
    /// ⛔ Call under <see cref="Gate"/>, on the thread that opened or bound it.</summary>
    internal static void Leave()
    {
        if (_keptWindow is { } kept) GlxContext.Release(kept);
        else Raylib_cs.Raylib.CloseWindow();
    }

    /// <summary>
    /// ⭐⭐ Opens a window and an ImGui context. ⛔ Call <see cref="IsAvailable"/> first and skip if false
    /// — this throws rather than pretending, because a silently-degraded harness is the failure mode the
    /// whole batch exists to end.
    /// </summary>
    /// <param name="width">⭐ Wide enough that the app's real layout is not itself the constraint.</param>
    public static UiFrameSession Begin(int width = DefaultWidth, int height = DefaultHeight)
    {
        if (!IsAvailable())
            throw new InvalidOperationException(
                $"UiFrameHarness is not available: {_unavailableReason}. Skip the rail, do not fail it.");

        Gate.Wait();
        try                { return new UiFrameSession(width, height, Gate); }
        catch              { Gate.Release(); throw; }
    }
}
