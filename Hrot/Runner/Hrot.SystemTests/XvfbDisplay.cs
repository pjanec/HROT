using System.Diagnostics;

namespace Hrot.SystemTests;

/// <summary>
/// Owns one Xvfb display for the lifetime of whoever holds it.
///
/// <para><b>ST-019: extracted from <see cref="EditorProcessFixture"/> so the mode rails can reuse it.</b>
/// It was private there, and the mode rails need exactly the same thing — a display that dies when we
/// say so. Copying it would have meant two implementations of the orphan-avoidance below, which is the
/// one part of launching a headless runner that is genuinely easy to get wrong.</para>
///
/// <para>⛔ <b>NOT <c>xvfb-run</c>, and the reason is measured:</b> xvfb-run is a shell script that stops
/// its Xvfb from an EXIT trap, and <c>Process.Kill</c> sends SIGKILL — the trap never runs, so every
/// torn-down process ORPHANS an Xvfb and its <c>/tmp/.X&lt;n&gt;-lock</c>. A run leaked one display each
/// time until the lifetime was owned directly.</para>
///
/// <para>Same environment as the proven recipe in <c>docs/Editor_Headless_Xvfb.md</c> (a 1600x1000x24
/// screen and Mesa's software rasteriser); only the lifetime is ours.</para>
/// </summary>
public sealed class XvfbDisplay : IDisposable
{
    private Process? _server;

    /// <summary>The display number, e.g. <c>90</c>. Valid once constructed.</summary>
    public int Number { get; }

    /// <summary>The value to put in <c>DISPLAY</c>, e.g. <c>:90</c>.</summary>
    public string Value => $":{Number}";

    /// <summary>
    /// Starts an Xvfb on a free display and returns once it is actually accepting clients.
    ///
    /// <para>Display numbers are claimed by <c>/tmp/.X&lt;n&gt;-lock</c>, and two collections can start
    /// at nearly the same moment, so a free-looking number can be taken between the check and the
    /// spawn. Rather than lock, it simply tries the next one — cheap, and it converges.</para>
    /// </summary>
    public XvfbDisplay()
    {
        const int firstDisplay = 90;
        const int lastDisplay = 220;

        for (int candidate = firstDisplay; candidate <= lastDisplay; candidate++)
        {
            if (File.Exists($"/tmp/.X{candidate}-lock")) continue;

            var psi = new ProcessStartInfo
            {
                FileName = "Xvfb",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add($":{candidate}");
            psi.ArgumentList.Add("-screen");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("1600x1000x24");
            psi.ArgumentList.Add("-nolisten");
            psi.ArgumentList.Add("tcp");

            Process? server = null;
            try
            {
                server = Process.Start(psi);
                if (server is null) continue;

                // The X socket appearing is the signal that it is ready for clients; starting a
                // client before that produces a "cannot open display" that looks like a fault in the
                // client and is not one.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (server.HasExited) break;                       // display taken — try the next
                    if (File.Exists($"/tmp/.X11-unix/X{candidate}"))
                    {
                        _server = server;
                        Number = candidate;
                        return;
                    }
                    Thread.Sleep(50);
                }

                try { if (!server.HasExited) server.Kill(entireProcessTree: true); } catch { }
                server.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                server?.Dispose();
                throw new InvalidOperationException(
                    $"Could not start Xvfb (needed to run the runner headless): {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException(
            $"No free X display between :{firstDisplay} and :{lastDisplay} — are stale Xvfb processes running?");
    }

    /// <summary>
    /// Applies <c>DISPLAY</c> plus the software-rasteriser environment to a process about to be started.
    /// </summary>
    public void ApplyTo(ProcessStartInfo psi)
    {
        psi.Environment["DISPLAY"] = Value;
        psi.Environment["LIBGL_ALWAYS_SOFTWARE"] = "1";
        psi.Environment["GALLIUM_DRIVER"] = "llvmpipe";
    }

    public void Dispose()
    {
        // The display server outlives its client, so it is stopped explicitly. Leaking one per run
        // exhausts display numbers on a machine that runs the suite repeatedly — which is exactly
        // what a CI lane does.
        if (_server is not null)
        {
            try
            {
                if (!_server.HasExited)
                {
                    _server.Kill(entireProcessTree: true);
                    _server.WaitForExit(10_000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
            finally
            {
                _server.Dispose();
                _server = null;
            }

            // Xvfb removes its own lock on a clean stop but not always on a kill.
            try { File.Delete($"/tmp/.X{Number}-lock"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
