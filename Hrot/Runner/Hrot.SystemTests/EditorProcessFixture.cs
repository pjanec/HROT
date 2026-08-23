using System.Text;

namespace Hrot.SystemTests;

/// <summary>
/// Boots one REAL editor process per test collection, headless, and exposes it as an
/// <see cref="McpClient"/> (design §3/§4, task H1).
///
/// <para><b>Why a process and not an in-process host.</b> The point of this suite is that the whole
/// stack runs the way a human or an agent drives it — kernel, subsystems, HTTP host, the same
/// startup path. An in-process harness would test an assembled-for-testing object graph instead, and
/// would need the <c>EditorHarness</c> reconciliation deferred as <c>DEBT-MCP-001</c>.</para>
///
/// <para>⭐⭐ <b><c>N1</c> (`2026-08-23`) — THE LAUNCH MOVED TO <see cref="EditorProcess"/> and this class
/// now DELEGATES.</b> 📄 <c>DESIGN_Regression_Net.md</c> §7 <c>N1</c>: the determinism rail needs **two
/// fresh** editors, and this fixture is one per COLLECTION — shared, and by the time a rail runs, mutated
/// by other cases. ⛔ Copying the launch would have been a second implementation of launch-and-wait, which
/// is precisely what <c>ST-019</c> extracted <see cref="XvfbDisplay"/> to avoid one level down. ⭐ The
/// public surface here is UNCHANGED, so no existing case moved — the 52 green cases are the rail on the
/// extraction.</para>
///
/// <para><b>The readiness signal is deliberately not "the socket is open"</b> — see
/// <see cref="EditorProcess"/>, which owns that reasoning now.</para>
/// </summary>
public class EditorProcessFixture : IAsyncLifetime
{
    private EditorProcess? _editor;

    /// <summary>Where the editor is listening. Valid after <see cref="InitializeAsync"/>.</summary>
    public Uri BaseUrl => _editor?.BaseUrl ?? new Uri("http://localhost:0/");

    /// <summary>The driver every test uses. Valid after <see cref="InitializeAsync"/>.</summary>
    public McpClient Client { get; private set; } = null!;

    /// <summary>Per-run staging directory handed to the editor as <c>FDP_STAGING_ROOT</c>.</summary>
    public string StagingRoot => _editor?.StagingRoot ?? "";

    /// <summary>Everything the editor wrote to stdout/stderr, for diagnosing a failure.</summary>
    public string EditorOutput => _editor?.EditorOutput ?? "";

    /// <summary>
    /// Where the editor's console output is mirrored. A failing case usually fails because of
    /// something the EDITOR did, and its output is the only place that says what — in CI nobody can
    /// re-run it interactively, so it is written to a file that outlives the run rather than kept
    /// in memory and lost with the process.
    /// </summary>
    public string LogPath => _editor?.LogPath ?? "";

    /// <summary>
    /// Describes how the editor died, or <see langword="null"/> while it is alive. Turns the
    /// otherwise-useless "connection refused" into the exit code plus the editor's last words.
    /// </summary>
    public string? ExitDiagnostics()
        => _editor is null ? "the editor was never started." : _editor.ExitDiagnostics();

    /// <summary>True once the editor process is gone. False while it is running or never started.</summary>
    public bool HasExited => _editor is not null && _editor.HasExited;

    /// <summary>
    /// Waits for the editor to exit, returning false on timeout. Used by the shutdown rail and by
    /// this fixture's own graceful teardown.
    /// </summary>
    public Task<bool> WaitForExitAsync(TimeSpan timeout)
        => _editor?.WaitForExitAsync(timeout) ?? Task.FromResult(true);

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

        _editor = await EditorProcess.StartAsync().ConfigureAwait(false);
        Client = _editor.Client;
    }

    public async Task DisposeAsync()
    {
        if (_editor is not null)
        {
            await _editor.DisposeAsync().ConfigureAwait(false);
            _editor = null;
        }
        else
        {
            // The idle path built a client and no process.
            Client?.Dispose();
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
