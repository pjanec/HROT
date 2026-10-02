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

        try
        {
            Raylib_cs.Raylib.SetTraceLogLevel(Raylib_cs.TraceLogLevel.Error);
            Raylib_cs.Raylib.InitWindow(64, 64, "uiframe-probe");
            bool ready = Raylib_cs.Raylib.IsWindowReady();
            Raylib_cs.Raylib.CloseWindow();

            _available = ready;
            if (!ready) _unavailableReason = "DISPLAY is set but Raylib could not create a window (no GL?)";
            return ready;
        }
        catch (Exception ex)
        {
            // ⚠ A missing native libraylib is a DllNotFoundException, not a return value.
            _unavailableReason = $"{ex.GetType().Name}: {ex.Message}";
            _available = false;
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ Opens a window and an ImGui context. ⛔ Call <see cref="IsAvailable"/> first and skip if false
    /// — this throws rather than pretending, because a silently-degraded harness is the failure mode the
    /// whole batch exists to end.
    /// </summary>
    /// <param name="width">⭐ Wide enough that the app's real layout is not itself the constraint.</param>
    public static UiFrameSession Begin(int width = 1280, int height = 800)
    {
        if (!IsAvailable())
            throw new InvalidOperationException(
                $"UiFrameHarness is not available: {_unavailableReason}. Skip the rail, do not fail it.");

        Gate.Wait();
        try                { return new UiFrameSession(width, height, Gate); }
        catch              { Gate.Release(); throw; }
    }
}
