using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// Shared base for every system test: the client, and the poll-with-timeout helpers that keep an
/// asynchronous simulation from producing flaky assertions (design D8, task H3).
///
/// <para><b>The rule these helpers encode:</b> the sim advances over frames, so anything caused by
/// running the world is only true EVENTUALLY. Asserting straight after a <c>play</c> tests the
/// scheduler's luck, not the system. Every such assertion goes through
/// <see cref="WaitUntilAsync"/>, which fails with what it last saw rather than a bare timeout.</para>
/// </summary>
[Collection(EditorCollection.Name)]
public abstract class SystemTestBase
{
    protected EditorProcessFixture Fixture { get; }
    protected McpClient Mcp => Fixture.Client;
    protected ITestOutputHelper Output { get; }

    protected SystemTestBase(EditorProcessFixture fixture, ITestOutputHelper output)
    {
        Fixture = fixture;
        Output = output;
    }

    /// <summary>Default patience for anything that needs the world to tick.</summary>
    protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Polls <paramref name="probe"/> until <paramref name="predicate"/> holds, then returns the
    /// value that satisfied it. On timeout it throws naming <paramref name="description"/> and the
    /// LAST observed value — a timeout that does not say what it saw is nearly useless when the
    /// case fails in CI and not on a desk.
    /// </summary>
    protected async Task<T> WaitUntilAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> predicate,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);
        var sw = Stopwatch.StartNew();

        T last = default!;
        bool sawAny = false;

        while (sw.Elapsed < limit)
        {
            last = await probe().ConfigureAwait(false);
            sawAny = true;
            if (predicate(last)) return last;
            await Task.Delay(interval).ConfigureAwait(false);
        }

        var seen = sawAny ? Describe(last) : "nothing (the probe never returned)";
        throw new TimeoutException($"Timed out after {limit.TotalSeconds:0}s waiting for {description}. Last seen: {seen}");
    }

    /// <summary>Waits for the simulation clock to pass <paramref name="target"/>.</summary>
    protected Task<ApiResult> WaitForSimTimeAsync(double target, TimeSpan? timeout = null)
        => WaitUntilAsync(
            () => Mcp.GetStatusAsync(),
            r => r.Ok && r.Double("simTime") > target,
            $"simTime to pass {target:0.###}",
            timeout);

    /// <summary>
    /// Waits for a breakpoint to report a hit. Returns the <c>/breakpoints/hits</c> payload, whose
    /// <c>lastHit</c> carries the breakpoint id and the entity that tripped it.
    /// </summary>
    protected Task<ApiResult> WaitForBreakpointHitAsync(string? breakpointId = null, TimeSpan? timeout = null)
        => WaitUntilAsync(
            () => Mcp.GetBreakpointHitsAsync(),
            r =>
            {
                if (!r.Ok) return false;
                var hit = r.Field("lastHit");
                if (hit is null || hit is JsonValue) return false;
                if (breakpointId is null) return true;
                return string.Equals(hit["breakpointId"]?.GetValue<string>(), breakpointId, StringComparison.OrdinalIgnoreCase);
            },
            breakpointId is null ? "any breakpoint to be hit" : $"breakpoint {breakpointId} to be hit",
            timeout);

    /// <summary>
    /// Brings the shared editor to "scenario loaded, in preview, paused" — the starting state of
    /// nearly every case — and does it <b>at most once</b> per editor.
    ///
    /// <para><b>Why idempotent rather than load-per-test.</b> Reloading rebuilds the world with a
    /// fresh set of network ids, so a case that lists entities while another case's reload is
    /// settling can hold an id the entity map has already dropped — a 404 that says nothing about
    /// the system. Loading once removes that race entirely and saves a second or two per case.</para>
    /// </summary>
    protected async Task LoadAndPreviewAsync(string scenario, bool startPaused = true)
    {
        var status = (await Mcp.GetStatusAsync().ConfigureAwait(false)).EnsureOk();

        if (!string.Equals(status.String("scenario"), scenario, StringComparison.Ordinal))
            (await Mcp.LoadScenarioEditAsync(scenario).ConfigureAwait(false)).EnsureOk();

        (await Mcp.EnterPreviewIfNeededAsync(startPaused).ConfigureAwait(false)).EnsureOk();
    }

    /// <summary>
    /// Returns the world to a known state between cases. The collection shares ONE editor, so a
    /// case that leaves the clock running is a case that breaks the next one.
    ///
    /// <para>⛔ <b>It deliberately does NOT exit preview.</b> That began as isolation from
    /// <c>HN-001</c> (leaving preview aborted the editor), which is now FIXED — but the reason to
    /// keep it stands on its own: the shared editor is loaded once per fixture, and dropping out of
    /// preview between cases would rebuild the world with fresh network ids and invalidate entity
    /// addresses other cases just resolved.</para>
    ///
    /// <para>⭐ Leaving preview is exercised where it belongs — <c>PreviewLifecycleRails</c>, on its
    /// own editor.</para>
    /// </summary>
    protected async Task ResetToIdleAsync()
    {
        var status = await Mcp.GetReplayStatusAsync().ConfigureAwait(false);
        if (status.Ok && status.Bool("replayActive"))
            await Mcp.UnloadReplayAsync().ConfigureAwait(false);

        await ClearBreakpointsAsync().ConfigureAwait(false);

        // ⛔⛔ Clearing the breakpoints is NOT enough, and this cost a real diagnosis: a breakpoint that
        //    HIT leaves the debugger REWOUND, deleting it does not resume, and the staged-write drain
        //    is gated on not being rewound — so every later case's live write was queued and silently
        //    never applied. ⭐ The effect outlives the breakpoint, so the reset must resume too.
        //    (The product-side finding is MX-009; this line is the harness's own isolation.)
        await Mcp.ContinueFromBreakpointAsync().ConfigureAwait(false);

        var sim = await Mcp.GetSimStateAsync().ConfigureAwait(false);
        if (sim.Ok && !sim.Bool("isPaused"))
            await Mcp.PauseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Removes every registered breakpoint.
    ///
    /// <para>⚠ <b>A breakpoint outlives the case that set it, and its effects outlive the
    /// breakpoint.</b> A hit PAUSES the simulation, and that pause survives removing the breakpoint —
    /// enough to stop a later case's scenario load from ever reaching <c>OperatingEdit</c>. Measured,
    /// not theorised: it cost two cases before it was cleaned up here.</para>
    /// </summary>
    protected async Task ClearBreakpointsAsync()
    {
        var listed = await Mcp.ListBreakpointsAsync().ConfigureAwait(false);
        if (!listed.Ok) return;

        foreach (var entry in listed.Array())
        {
            var id = entry?["id"]?.GetValue<string>() ?? entry?["Id"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(id))
                await Mcp.RemoveBreakpointAsync(id!).ConfigureAwait(false);
        }
    }

    /// <summary>Picks a curated scenario that this build actually seeds, preferring <paramref name="wanted"/>.</summary>
    protected async Task<string> AnyCuratedScenarioAsync(string wanted)
    {
        var scenarios = (await Mcp.ListScenariosAsync().ConfigureAwait(false)).EnsureOk().Array();
        var names = scenarios.Select(n => n?.GetValue<string>()).Where(n => n is not null).ToList();
        Assert.NotEmpty(names);
        return names.Contains(wanted) ? wanted : names[0]!;
    }

    private static string Describe<T>(T value) => value switch
    {
        null => "null",
        ApiResult r => r.Ok ? $"ok, data={Trim(r.Data?.ToJsonString())}" : $"failed ({r.StatusCode}): {r.Error}",
        JsonNode n => Trim(n.ToJsonString()),
        _ => value.ToString() ?? "(no text)",
    };

    private static string Trim(string? s)
        => s is null ? "null" : s.Length <= 300 ? s : s[..300] + "…";
}
