using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Services;
using Hrot.ClusterRunner.Tests.Mocks;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Services;

/// <summary>
/// GZH-013: Tests for <see cref="ConsoleCommandService"/>.
/// </summary>
public class GZH013_Tests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SubsystemOrchestrator CreateHeadlessOrchestrator(params ISubsystem[] subsystems)
        => new SubsystemOrchestrator(subsystems, new RunnerOptions { Headless = true });

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GZH013_1_KnownCommand_DispatchesAction()
    {
        var reader = new StringReader("open\n");
        using var svc = new ConsoleCommandService(reader);

        var dispatched = new List<Action<SubsystemOrchestrator>>();
        svc.OnCommandDispatched += a => dispatched.Add(a);

        svc.Start();

        // Wait up to 500 ms for the background thread to process the line.
        var sw = Stopwatch.StartNew();
        while (dispatched.Count == 0 && sw.ElapsedMilliseconds < 500)
            Thread.Sleep(10);

        Assert.Equal(1, dispatched.Count);
    }

    [Fact]
    public void GZH013_2_UnknownCommand_DoesNotDispatch()
    {
        var reader = new StringReader("nonexistent\n");
        using var svc = new ConsoleCommandService(reader);

        int dispatchCount = 0;
        svc.OnCommandDispatched += _ => dispatchCount++;

        svc.Start();
        Thread.Sleep(200);

        Assert.Equal(0, dispatchCount);
    }

    [Fact]
    public void GZH013_3_Dispose_CompletesWithin500ms()
    {
        // Use a reader that returns null immediately so the thread exits on its own,
        // and verify Dispose() itself is fast.
        var reader = new StringReader(string.Empty);
        var svc = new ConsoleCommandService(reader);
        svc.Start();

        var sw = Stopwatch.StartNew();
        svc.Dispose();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Dispose took {sw.ElapsedMilliseconds} ms, expected < 500 ms");
    }

    [Fact]
    public void GZH013_4_ExitCommand_StopsOrchestrator()
    {
        var reader = new StringReader("exit\n");
        using var svc = new ConsoleCommandService(reader);

        var mock = new MockSubsystem("TestSub");
        var orch = CreateHeadlessOrchestrator(mock);
        orch.Initialize();

        svc.OnCommandDispatched += orch.EnqueueConsoleAction;
        svc.Start();

        // Wait for the background thread to dispatch.
        Thread.Sleep(200);

        // Drain on the "main" thread -- this executes orch.Stop().
        orch.DrainConsoleActions();

        // After Stop(), RunFrames(0) should complete immediately and _running == false.
        // Verify by running the orchestrator for zero frames -- the Run() loop would spin
        // forever if _running were still true; instead we just check DrainConsoleActions
        // caused orch.Stop() to be called by confirming we reach this line.
        // A more direct check: wrap orchestrator.Run() in a task with a short timeout.
        bool completed = false;
        var runTask = System.Threading.Tasks.Task.Run(() =>
        {
            orch.Run(); // should return immediately since _running == false
            completed = true;
        });
        completed = runTask.Wait(500);

        Assert.True(completed, "orchestrator.Run() did not complete within 500 ms after Stop()");
    }
}
