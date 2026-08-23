using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hrot.SystemTests;

/// <summary>
/// Boots one REAL editor process per test collection, headless, and exposes it as an
/// <see cref="McpClient"/> (design §3/§4, task H1).
///
/// <para><b>Why a process and not an in-process host.</b> The point of this suite is that the whole
/// stack runs the way a human or an agent drives it — kernel, subsystems, HTTP host, the same
/// startup path. An in-process harness would test a assembled-for-testing object graph instead, and
/// would need the <c>EditorHarness</c> reconciliation deferred as <c>DEBT-MCP-001</c>.</para>
///
/// <para><b>The readiness signal is deliberately not "the socket is open".</b> <c>GET /status</c> is
/// served through <c>MainThreadJobQueue</c>, so a payload only comes back once the editor's main
/// loop is actually draining jobs. Waiting for a payload therefore proves the editor is TICKING,
/// which is the property every test depends on; a bare 200 would not (the host answers a minimal
/// <c>{ok:true}</c> before its service is attached).</para>
/// </summary>
public class EditorProcessFixture : IAsyncLifetime
{
    private Process? _process;
    private Process? _xvfb;
    private int _display = -1;
    private readonly StringBuilder _output = new();
    private readonly object _outputLock = new();

    /// <summary>Where the editor is listening. Valid after <see cref="InitializeAsync"/>.</summary>
    public Uri BaseUrl { get; private set; } = new("http://localhost:0/");

    /// <summary>The driver every test uses. Valid after <see cref="InitializeAsync"/>.</summary>
    public McpClient Client { get; private set; } = null!;

    /// <summary>Per-run staging directory handed to the editor as <c>FDP_STAGING_ROOT</c>.</summary>
    public string StagingRoot { get; private set; } = "";

    /// <summary>Everything the editor wrote to stdout/stderr, for diagnosing a failure.</summary>
    public string EditorOutput { get { lock (_outputLock) return _output.ToString(); } }

    /// <summary>
    /// Where the editor's console output is mirrored. A failing case usually fails because of
    /// something the EDITOR did, and its output is the only place that says what — in CI nobody can
    /// re-run it interactively, so it is written to a file that outlives the run rather than kept
    /// in memory and lost with the process.
    /// </summary>
    public string LogPath { get; private set; } = "";

    /// <summary>
    /// Describes how the editor died, or <see langword="null"/> while it is alive. Turns the
    /// otherwise-useless "connection refused" into the exit code plus the editor's last words.
    /// </summary>
    public string? ExitDiagnostics()
    {
        if (_process is null) return "the editor was never started.";
        if (!_process.HasExited) return null;
        return $"the editor exited with code {_process.ExitCode}.{OutputTail()}";
    }

    /// <summary>True once the editor process is gone. False while it is running or never started.</summary>
    public bool HasExited => _process is not null && _process.HasExited;

    /// <summary>
    /// Waits for the editor to exit, returning false on timeout. Used by the shutdown rail and by
    /// this fixture's own graceful teardown.
    /// </summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (_process is null) return true;
        try
        {
            await _process.WaitForExitAsync(new CancellationTokenSource(timeout).Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task InitializeAsync()
    {
        if (SystemTestEnvironment.SkipReason is { } reason)
        {
            // Every case is skipped anyway; constructing nothing keeps the fixture from failing the
            // whole collection on a host that legitimately cannot run it.
            Client = new McpClient(BaseUrl);
            Console.WriteLine($"[SystemTests] fixture idle: {reason}");
            return;
        }

        int port = AllocateFreePort();
        StagingRoot = Path.Combine(Path.GetTempPath(), "hrot-systemtests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(StagingRoot);

        // Deliberately NOT under StagingRoot: that directory is deleted on teardown, and the log is
        // most wanted precisely when the run failed.
        LogPath = Path.Combine(Path.GetTempPath(), $"hrot-systemtests-editor-{port}.log");
        try { File.WriteAllText(LogPath, $"# editor log, port {port}, staging {StagingRoot}{Environment.NewLine}"); }
        catch (IOException) { LogPath = ""; }
        Console.WriteLine($"[SystemTests] editor log: {LogPath}");

        BaseUrl = new Uri($"http://localhost:{port}/");
        Client = new McpClient(BaseUrl) { DiagnoseUnreachable = ExitDiagnostics };

        _process = LaunchEditor(port, StagingRoot);
        await WaitForStatusAsync(TimeSpan.FromSeconds(SystemTestEnvironment.BootTimeoutSeconds)).ConfigureAwait(false);
    }

    /// <summary>
    /// Binds :0, reads what the OS handed out, and releases it. There is a small window between
    /// release and the editor's bind, but a per-collection ephemeral port is what lets collections
    /// run against their own editor without a shared registry (design D3).
    /// </summary>
    private static int AllocateFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private Process LaunchEditor(int port, string stagingRoot)
    {
        var editorDll = SystemTestEnvironment.EditorDll
            ?? throw new InvalidOperationException("Editor dll not resolved.");
        var workingDir = Path.GetDirectoryName(editorDll)!;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(editorDll);

        if (!SystemTestEnvironment.IsWindows)
        {
            // ⛔ NOT `xvfb-run`, and the reason is measured: xvfb-run is a shell script that stops
            // its Xvfb from an EXIT trap, and Process.Kill sends SIGKILL — the trap never runs, so
            // every torn-down editor ORPHANS an Xvfb process and its /tmp/.X<n>-lock. A run leaked
            // one display each time until this was owned directly.
            //
            // Same environment as the proven recipe in docs/Editor_Headless_Xvfb.md (a 1600x1000x24
            // screen and Mesa's software rasteriser); only the lifetime is ours now.
            _xvfb = StartXvfb(out int display);
            psi.Environment["DISPLAY"] = $":{display}";
            psi.Environment["LIBGL_ALWAYS_SOFTWARE"] = "1";
            psi.Environment["GALLIUM_DRIVER"] = "llvmpipe";
        }

        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add("editor");

        // Enables the loopback control plane; without it the editor runs with no API at all.
        psi.Environment["HROT_DEBUG_API_PORT"] = port.ToString();
        // Isolates recordings/staging per run. The default is resolved cross-platform now, but a
        // shared root would let one test's recording collide with another's.
        psi.Environment["FDP_STAGING_ROOT"] = stagingRoot;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>
    /// Starts our own Xvfb on a free display and returns once it is actually accepting clients.
    ///
    /// <para>Display numbers are claimed by <c>/tmp/.X&lt;n&gt;-lock</c>, and two collections start
    /// at nearly the same moment, so the free-looking number can be taken between the check and the
    /// spawn. Rather than lock, it simply tries the next one — cheap, and it converges.</para>
    /// </summary>
    private Process StartXvfb(out int display)
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

                // The X socket appearing is the signal that it is ready for clients; starting the
                // editor before that produces a "cannot open display" that looks like an editor
                // fault and is not one.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (server.HasExited) break;                       // display taken — try the next
                    if (File.Exists($"/tmp/.X11-unix/X{candidate}"))
                    {
                        display = _display = candidate;
                        return server;
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
                    $"Could not start Xvfb (needed to run the editor headless): {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException(
            $"No free X display between :{firstDisplay} and :{lastDisplay} — are stale Xvfb processes running?");
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        lock (_outputLock)
        {
            // Bounded in memory: a long run must not turn the editor's chatter into a memory
            // problem, and the head is what explains a boot failure, so the TAIL is dropped here.
            if (_output.Length < 512 * 1024)
                _output.AppendLine(line);

            // The file keeps everything — it is what a CI failure is diagnosed from.
            if (LogPath.Length > 0)
            {
                try { File.AppendAllText(LogPath, line + Environment.NewLine); }
                catch (IOException) { /* logging must never fail the run */ }
            }
        }
    }

    /// <summary>
    /// Polls <c>GET /status</c> until it answers with a payload. Fails — never skips — on timeout or
    /// early exit: at this point the host was judged able to run an editor, so not getting one is a
    /// defect, and the message carries the editor's own output because that is what explains it.
    /// </summary>
    private async Task WaitForStatusAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
                throw new InvalidOperationException(
                    $"The editor exited with code {_process.ExitCode} before serving /status.{OutputTail()}");

            try
            {
                var status = await Client.GetStatusAsync().ConfigureAwait(false);
                // `ok` with a payload means the service is attached AND the main loop is draining
                // the job queue — i.e. the editor is actually ticking, not merely listening.
                if (status.Ok && status.Data is not null)
                    return;
            }
            catch (McpRequestException ex)
            {
                last = ex; // not up yet — expected for the first seconds
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The editor did not serve /status within {timeout.TotalSeconds:0}s at {BaseUrl}. " +
            $"Last transport error: {last?.Message ?? "none"}.{OutputTail()}");
    }

    private string OutputTail()
    {
        var text = EditorOutput;
        if (string.IsNullOrWhiteSpace(text)) return " The editor produced no output.";
        var lines = text.Split('\n');
        var tail = lines.Length <= 40 ? text : string.Join('\n', lines[^40..]);
        return $"\n──── editor output (last {Math.Min(lines.Length, 40)} lines) ────\n{tail}";
    }

    public async Task DisposeAsync()
    {
        // ⛔ The client is disposed AFTER the graceful stop below, not before it — asking the editor
        // to exit needs a live HTTP client.
        if (_process is not null)
        {
            try
            {
                // Ask first, kill second. POST /shutdown now ends the runner's frame loop, so the
                // editor tears its subsystems down in order instead of dying mid-frame — which is
                // what used to print "free(): corrupted unsorted chunks" (a kill artifact
                // documented in Editor_Headless_Xvfb.md, never a fault). The kill stays as the
                // fallback: teardown must not hang on an editor that is wedged or already broken.
                if (!_process.HasExited && Client is not null)
                {
                    try { await Client.ShutdownAsync().ConfigureAwait(false); }
                    catch { /* unreachable or already dying — the kill below settles it */ }

                    await WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }

                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token)
                                  .ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or NotSupportedException)
            {
                // Already gone, or refused to die inside the grace period; the dispose must not
                // fail the run over it.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        Client?.Dispose();

        // The display server outlives the editor, so it is stopped explicitly. Leaking one per run
        // exhausts display numbers on a machine that runs the suite repeatedly — which is exactly
        // what a CI lane does.
        if (_xvfb is not null)
        {
            try
            {
                if (!_xvfb.HasExited)
                {
                    _xvfb.Kill(entireProcessTree: true);
                    _xvfb.WaitForExit(10_000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
            finally
            {
                _xvfb.Dispose();
                _xvfb = null;
            }

            // Xvfb removes its own lock on a clean stop but not always on a kill.
            if (_display >= 0)
            {
                try { File.Delete($"/tmp/.X{_display}-lock"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        if (!string.IsNullOrEmpty(StagingRoot) && Directory.Exists(StagingRoot))
        {
            try { Directory.Delete(StagingRoot, recursive: true); }
            catch (IOException) { /* a file still held open must not fail the run */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// The collection every system test joins, so one editor boot is amortised across the whole suite
/// (design D6). Cases inside run serially — they share one live world.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EditorCollection : ICollectionFixture<EditorProcessFixture>
{
    public const string Name = "editor-process";
}
