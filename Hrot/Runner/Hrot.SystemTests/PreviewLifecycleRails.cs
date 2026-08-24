using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// Rails for the preview lifecycle and the record→replay round trip.
///
/// <para><b>Where these came from.</b> Both were written as SKIPPED rails carrying the reproduction
/// of <c>HN-001</c> — the defect this harness found on its first green run: <c>POST /preview/exit</c>
/// aborted the editor process (SIGABRT). They are live assertions now that the defect is fixed; they
/// stay so the fix cannot silently regress.</para>
///
/// <para><b>HN-001, for the record.</b> The preview rewind
/// (<c>PreviewClusterOpHandler.UnloadingPreviewCommit</c> → <c>EntityRepository.SyncFrom(…,
/// includeTransient: true)</c>) restored a managed component's PRESENCE bit without its managed
/// PAYLOAD, and the next tick's <c>GenesisMaterializationSystem.MaterializeTargets</c> dereferenced
/// the null. Root cause: <c>ManagedComponentTable.ClearRaw</c> — the command-buffer removal path —
/// nulled the payload without bumping the chunk version, so <c>SyncDirtyChunks</c> skipped the chunk
/// on the way back while the entity index (whose versions <c>ApplyComponentFilter</c> bumps on every
/// sync) always restored the mask. Pinned at the unit level by
/// <c>Fdp.Tests.PreviewRewindManagedComponentTests</c>.</para>
///
/// <para>These run in their OWN collection — a fresh editor — because a regression here KILLS the
/// editor process and would take every other case with it.</para>
/// </summary>
[Collection(PreviewLifecycleCollection.Name)]
[Trait("Category", "SystemSmoke")]
[Trait("Category", "PreviewLifecycle")]
public sealed class PreviewLifecycleRails
{
    private readonly EditorProcessFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PreviewLifecycleRails(EditorProcessFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private McpClient Mcp => _fixture.Client;

    /// <summary>
    /// <b>HN-001.</b> Three HTTP calls, no test code needed to reproduce the original crash:
    /// <c>POST /scenario/load {"name":"hill-attack","waitForReady":true}</c> →
    /// <c>POST /preview/enter {"startPaused":true}</c> → <c>POST /preview/exit</c>. Before the fix
    /// the process exited 134 (SIGABRT), identically on all three curated scenarios.
    /// </summary>
    [Fact]
    public async Task Exiting_preview_does_not_abort_the_editor()
    {
        (await Mcp.LoadScenarioEditAsync("hill-attack")).EnsureOk();
        (await Mcp.EnterPreviewIfNeededAsync(startPaused: true)).EnsureOk();

        (await Mcp.ExitPreviewAsync()).EnsureOk();

        // The assertion is simply that the editor is still there afterwards.
        await Task.Delay(TimeSpan.FromSeconds(2));
        var status = await Mcp.GetStatusAsync();
        Assert.True(status.Ok, $"the editor did not survive leaving preview: {_fixture.ExitDiagnostics()}");
        Assert.False(status.Bool("inPreview"));
    }

    /// <summary>
    /// The record→replay round trip from the design's H4 list (record → stop → load → step frames).
    /// It was blocked by <c>HN-001</c>: <c>/recording/stop</c> ends with <c>ExitPreviewMode</c>, so it
    /// died on the same rewind.
    /// </summary>
    [Fact]
    public async Task Record_then_replay_round_trips_frames()
    {
        (await Mcp.LoadScenarioEditAsync("hill-attack")).EnsureOk();

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
public sealed class PreviewLifecycleCollection : ICollectionFixture<EditorProcessFixture>
{
    public const string Name = "editor-process-preview-lifecycle";
}
