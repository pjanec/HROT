using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// <b>HN-003 — <c>POST /shutdown</c> must actually stop the editor.</b>
///
/// <para>It used to answer 200 and do nothing: <c>EditorSubsystem</c> handed the API host
/// <c>() => { }</c> as its shutdown action, so the only way to end a headless editor was to kill it —
/// which is also why this harness's teardown killed mid-frame and produced glibc noise. The editor
/// now forwards the host's <c>SubsystemConfig.RequestAppExit</c> (bound to
/// <c>SubsystemOrchestrator.Stop</c>), so the runner leaves its frame loop and shuts every subsystem
/// down in order.</para>
///
/// <para>⛔ This rail necessarily ENDS its editor, so it owns one: its own collection, its own
/// fixture. A shared editor would take every later case with it.</para>
/// </summary>
[Collection(ShutdownCollection.Name)]
[Trait("Category", "SystemSmoke")]
[Trait("Category", "Shutdown")]
public sealed class ShutdownRail
{
    private readonly EditorProcessFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ShutdownRail(EditorProcessFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Posting_shutdown_stops_the_editor_process()
    {
        // Alive first — otherwise "it exited" would prove nothing.
        var status = await _fixture.Client.GetStatusAsync();
        Assert.True(status.Ok, $"the editor was not answering before shutdown: {_fixture.ExitDiagnostics()}");

        (await _fixture.Client.ShutdownAsync()).EnsureOk();

        var exited = await _fixture.WaitForExitAsync(TimeSpan.FromSeconds(20));
        Assert.True(exited,
            "the editor was still running 20s after POST /shutdown — the request is inert again.");

        _output.WriteLine($"editor stopped on request: {_fixture.ExitDiagnostics()}");
    }
}

/// <summary>Its own collection: this rail's editor does not survive it.</summary>
[CollectionDefinition(Name)]
public sealed class ShutdownCollection : ICollectionFixture<EditorProcessFixture>
{
    public const string Name = "editor-process-shutdown";
}
