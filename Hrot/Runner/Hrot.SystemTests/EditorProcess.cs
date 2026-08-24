using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐⭐ <b>ONE real editor process, headless, driven over the AI-debug API — owned by whoever started it.</b>
///
/// <para>⭐⭐ <b><c>N1</c> extracted this from <see cref="EditorProcessFixture"/>, which now delegates to
/// it.</b> 📄 <c>DESIGN_Regression_Net.md</c> §7 <c>N1</c> — the determinism rail needs **two fresh
/// processes**, and the fixture is one per COLLECTION, deliberately shared and by then mutated by other
/// cases. ⛔ <b>Copying its launch would have been the second implementation of the launch-and-wait recipe</b>
/// — 📌 exactly what <c>ST-019</c> extracted <see cref="XvfbDisplay"/> to avoid, one level up. ⇒ the
/// fixture and the rail now boot editors the same way, by construction.</para>
///
/// <para>⛔ <b>The readiness signal is not "the socket is open".</b> <c>GET /status</c> is served through
/// <c>MainThreadJobQueue</c>, so a payload only comes back once the editor's main loop is actually
/// draining jobs ⇒ waiting for a PAYLOAD proves the editor is TICKING. A bare 200 would not — the host
/// answers a minimal <c>{ok:true}</c> before its service is attached.</para>
///
/// <para>⚠ <b>Teardown asks before it kills.</b> <c>POST /shutdown</c> ends the runner's frame loop so
/// subsystems tear down in order; the kill is the fallback for a wedged editor. A kill mid-frame is what
/// prints <c>free(): corrupted unsorted chunks</c> — a kill artifact, never a fault.</para>
/// </summary>
public sealed class EditorProcess : IAsyncDisposable
{
    private Process? _process;
    private XvfbDisplay? _xvfb;
    private readonly StringBuilder _output = new();
    private readonly object _outputLock = new();

    /// <summary>Where this editor is listening.</summary>
    public Uri BaseUrl { get; private set; } = new("http://localhost:0/");

    /// <summary>The driver for this editor.</summary>
    public McpClient Client { get; private set; } = null!;

    /// <summary>Per-process staging directory, handed over as <c>FDP_STAGING_ROOT</c>.</summary>
    public string StagingRoot { get; private set; } = "";

    /// <summary>Where this editor's console output is mirrored — it outlives the run on purpose.</summary>
    public string LogPath { get; private set; } = "";

    /// <summary>Everything the editor wrote to stdout/stderr.</summary>
    public string EditorOutput { get { lock (_outputLock) return _output.ToString(); } }

    /// <summary>⭐ The runner mode this process was started in — <c>"editor"</c> or <c>"all"</c>.</summary>
    public string Mode { get; private set; } = "editor";

    /// <summary>True once the process is gone. False while running or never started.</summary>
    public bool HasExited => _process is not null && _process.HasExited;

    /// <summary>
    /// How the editor died, or <see langword="null"/> while alive. Turns a useless "connection refused"
    /// into an exit code plus the editor's last words.
    /// </summary>
    public string? ExitDiagnostics()
    {
        if (_process is null) return "the editor was never started.";
        if (!_process.HasExited) return null;
        return $"the editor exited with code {_process.ExitCode}.{OutputTail()}";
    }

    /// <summary>
    /// ⭐ Boots an editor and returns once it is TICKING.
    /// </summary>
    /// <param name="label">
    /// Distinguishes this process's log file from a sibling's. ⚠ <c>N1</c> runs two at once and a shared
    /// log name would interleave them into one unreadable file.
    /// </param>
    /// <param name="bootTimeout">Overrides the environment's boot budget.</param>
    /// <param name="mode">
    /// ⭐⭐⭐ <b>Which runner mode to boot</b> — <c>"editor"</c> *(the default, one node)* or <c>"all"</c>
    /// *(the five-subsystem cluster over DDS)*.
    /// <para>⭐ Added by the conformance batch so the SAME launch-and-wait recipe serves both modes — 📌 the
    /// <c>ST-019</c>/<c>N1</c> lesson: a second copy of "start it and wait for /status" is how two paths
    /// drift apart, and conformance compares them for a living.</para>
    /// <para>⚠⚠ <b>The cluster mode must run WINDOWED (Xvfb), not headless</b> — 📐 measured: a panel
    /// publishes only when it DRAWS, and the headless runner loop never calls <c>DrawUIAll</c> ⇒ every panel
    /// dump would be empty. ⭐ Same reason the editor harness has always run under Xvfb.</para>
    /// </param>
    public static async Task<EditorProcess> StartAsync(
        string? label = null, TimeSpan? bootTimeout = null, string mode = "editor")
    {
        var ed = new EditorProcess();
        ed.Mode = mode;
        int port = AllocateFreePort();

        ed.StagingRoot = Path.Combine(Path.GetTempPath(), "hrot-systemtests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(ed.StagingRoot);

        // ⛔ Deliberately NOT under StagingRoot: that is deleted on teardown, and the log is wanted
        //   precisely when the run failed.
        var suffix = string.IsNullOrEmpty(label) ? port.ToString() : $"{port}-{label}";
        ed.LogPath = Path.Combine(Path.GetTempPath(), $"hrot-systemtests-editor-{suffix}.log");
        try { File.WriteAllText(ed.LogPath, $"# editor log, port {port}, staging {ed.StagingRoot}{Environment.NewLine}"); }
        catch (IOException) { ed.LogPath = ""; }
        Console.WriteLine($"[SystemTests] editor log: {ed.LogPath}");

        ed.BaseUrl = new Uri($"http://localhost:{port}/");
        ed.Client = new McpClient(ed.BaseUrl) { DiagnoseUnreachable = ed.ExitDiagnostics };

        ed._process = ed.Launch(port, ed.StagingRoot);
        // ⚠ The cluster boots five subsystems and enrols them over DDS, so it needs a wider budget than the
        //   editor's single node. 📐 Measured ~35-50 s to serve /status on this class of machine.
        var budget = bootTimeout
                     ?? TimeSpan.FromSeconds(SystemTestEnvironment.BootTimeoutSeconds
                                             * (mode == "editor" ? 1 : 3));
        await ed.WaitForStatusAsync(budget).ConfigureAwait(false);
        return ed;
    }

    /// <summary>
    /// Binds :0, reads what the OS handed out, and releases it. ⚠ There is a small window between release
    /// and the editor's bind — a per-process ephemeral port is what lets processes run without a shared
    /// registry (design D3).
    /// </summary>
    private static int AllocateFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private Process Launch(int port, string stagingRoot)
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
            // ⭐ Its OWN display: two editors sharing one would be a hidden coupling in exactly the rail
            //   that exists to prove independence.
            _xvfb = new XvfbDisplay();
            _xvfb.ApplyTo(psi);
        }

        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add(Mode);

        // Enables the loopback control plane; without it the editor runs with no API at all.
        psi.Environment["HROT_DEBUG_API_PORT"] = port.ToString();
        // Isolates recordings/staging per process — a shared root would let one editor's recording
        // collide with the other's, which for N1 would look like non-determinism.
        psi.Environment["FDP_STAGING_ROOT"] = stagingRoot;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        lock (_outputLock)
        {
            // Bounded in memory; the HEAD is what explains a boot failure, so the tail is what is dropped.
            if (_output.Length < 512 * 1024)
                _output.AppendLine(line);

            if (LogPath.Length > 0)
            {
                try { File.AppendAllText(LogPath, line + Environment.NewLine); }
                catch (IOException) { /* logging must never fail the run */ }
            }
        }
    }

    /// <summary>Waits for the editor to exit; false on timeout.</summary>
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

    public async ValueTask DisposeAsync()
    {
        // ⛔ The client is disposed AFTER the graceful stop — asking the editor to exit needs a live client.
        if (_process is not null)
        {
            try
            {
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
                // Already gone, or refused to die inside the grace period; dispose must not fail the run.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        Client?.Dispose();

        // XvfbDisplay owns the kill + lock cleanup (ST-019).
        _xvfb?.Dispose();
        _xvfb = null;

        if (!string.IsNullOrEmpty(StagingRoot) && Directory.Exists(StagingRoot))
        {
            try { Directory.Delete(StagingRoot, recursive: true); }
            catch (IOException) { /* a held file must not fail the run */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
