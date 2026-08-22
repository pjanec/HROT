using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// Rails for system defects this harness FOUND and that are not yet fixed.
///
/// <para><b>Why they exist as tests rather than only as a note.</b> A defect recorded only in a
/// batch report is invisible by the next batch. A rail here names it, carries the exact reproduction,
/// and becomes a live assertion the moment someone fixes the defect — so the fix cannot land
/// unnoticed and cannot silently regress afterwards.</para>
///
/// <para><b>Why they are skipped rather than red.</b> A permanently-red lane stops being read, and
/// these are PRE-EXISTING product defects, not regressions from this batch — a red here would
/// wrongly say the harness broke something. ⚠ The skip is therefore a FINDING, not a fix: each one
/// is reported with its repro, and the skip reason names it so nobody mistakes it for a gap in
/// coverage. Delete the <c>Skip</c> when the defect is fixed.</para>
///
/// <para>These run in their OWN collection — each gets a fresh editor, because the defect below
/// KILLS the editor process and would take every other case with it.</para>
/// </summary>
[Collection(KnownDefectCollection.Name)]
[Trait("Category", "SystemSmoke")]
[Trait("Category", "KnownDefect")]
public sealed class KnownDefectRails
{
    private readonly EditorProcessFixture _fixture;
    private readonly ITestOutputHelper _output;

    public KnownDefectRails(EditorProcessFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private McpClient Mcp => _fixture.Client;

    /// <summary>
    /// <b>HN-001 — exiting preview aborts the editor process.</b>
    ///
    /// <para><b>Reproduction</b> (three HTTP calls, no test code needed):
    /// <c>POST /scenario/load {"name":"hill-attack","waitForReady":true}</c> →
    /// <c>POST /preview/enter {"startPaused":true}</c> → <c>POST /preview/exit</c> ⇒ the process
    /// exits 134 (SIGABRT). Reproduced identically on all three curated scenarios
    /// (<c>hill-attack</c>, <c>test-fire</c>, <c>test-move</c>), so it is not scenario-specific.</para>
    ///
    /// <para><b>What the editor prints:</b>
    /// <c>[Preview] UnloadingPreview: live repo rewound to snapshot.</c> then
    /// <c>FATAL: Entity(1, v1) GetManagedComponentRO&lt;InitialTargetsIntent&gt; returned null, but
    /// Has=True</c>, then an unhandled <c>InvalidOperationException</c> out of
    /// <c>GenesisMaterializationSystem.MaterializeTargets</c> on the next kernel tick.</para>
    ///
    /// <para><b>Mechanism.</b> The preview rewind restores the managed component's PRESENCE (the
    /// query still yields the entity, <c>HasManagedComponent</c> is true) without restoring its
    /// managed PAYLOAD (<c>GetManagedComponentRO</c> returns null). The next tick's genesis pass
    /// queries <c>WithManaged&lt;InitialTargetsIntent&gt;()</c> and dereferences the null.</para>
    ///
    /// <para><b>Blast radius.</b> Bigger than preview: <c>POST /recording/stop</c> ends with
    /// <c>FinishRecordingStop</c> → <c>ExitPreviewMode</c>, so the whole record→replay round trip
    /// dies the same way (see <see cref="Record_then_replay_round_trips_frames"/>).</para>
    ///
    /// <para>⚠ <b>Likely a regression, and the date matters to whoever fixes it:</b>
    /// <c>docs/MCP_Integration.md</c> records the record→replay cycle being driven end to end
    /// successfully on 2026-08-22 — writing a 48-frame <c>.fdp</c> through <c>/recording/stop</c>,
    /// which takes exactly this path. So this very likely broke AFTER that verification.</para>
    /// </summary>
    [Fact(Skip = "HN-001: POST /preview/exit aborts the editor (SIGABRT) — pre-existing product " +
                 "defect found by this harness. Remove this Skip when the preview rewind restores " +
                 "managed-component payloads. See the XML doc for the 3-call repro.")]
    public async Task Exiting_preview_does_not_abort_the_editor()
    {
        (await Mcp.LoadScenarioAsync("hill-attack")).EnsureOk();
        (await Mcp.EnterPreviewIfNeededAsync(startPaused: true)).EnsureOk();

        (await Mcp.ExitPreviewAsync()).EnsureOk();

        // The assertion is simply that the editor is still there afterwards.
        await Task.Delay(TimeSpan.FromSeconds(2));
        var status = await Mcp.GetStatusAsync();
        Assert.True(status.Ok, $"the editor did not survive leaving preview: {_fixture.ExitDiagnostics()}");
        Assert.False(status.Bool("inPreview"));
    }

    /// <summary>
    /// The record→replay round trip from the design's H4 list (record → stop → load → step 48
    /// frames). Blocked by <c>HN-001</c>: <c>/recording/stop</c> exits preview and takes the editor
    /// down with it. Written out in full so it runs the day the defect is fixed.
    /// </summary>
    [Fact(Skip = "Blocked by HN-001: /recording/stop exits preview, which aborts the editor. " +
                 "Remove this Skip together with the one on Exiting_preview_does_not_abort_the_editor.")]
    public async Task Record_then_replay_round_trips_frames()
    {
        (await Mcp.LoadScenarioAsync("hill-attack")).EnsureOk();

        // /recording/start enters preview itself and REJECTS a session already previewing.
        var start = (await Mcp.StartRecordingAsync("preview")).EnsureOk();
        Assert.False(string.IsNullOrWhiteSpace(start.String("fdpPath")));

        (await Mcp.PlayAsync()).EnsureOk();
        await Task.Delay(TimeSpan.FromSeconds(2));   // capture some frames

        var stop = (await Mcp.StopRecordingAsync()).EnsureOk();
        var recorded = stop.String("fdpPath");
        Assert.False(string.IsNullOrWhiteSpace(recorded));

        var loaded = (await Mcp.LoadReplayAsync(recorded!)).EnsureOk();
        Assert.True(loaded.Int("totalFrames") > 0);

        (await Mcp.ReplayStepAsync("forward")).EnsureOk();
        var status = (await Mcp.GetReplayStatusAsync()).EnsureOk();
        Assert.True(status.Bool("replayActive"));

        _output.WriteLine($"recorded {loaded.Int("totalFrames")} frames to {recorded}");
        (await Mcp.UnloadReplayAsync()).EnsureOk();
    }
}

/// <summary>
/// A collection of its own, so a case that kills the editor cannot take the capability suite with
/// it. Its fixture is a separate editor instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class KnownDefectCollection : ICollectionFixture<EditorProcessFixture>
{
    public const string Name = "editor-process-known-defects";
}
