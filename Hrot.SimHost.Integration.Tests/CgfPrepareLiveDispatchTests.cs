using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.CGF.Modules.Orchestration.Handlers;
using Hrot.Common.Orchestration;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using Xunit;

namespace Hrot.SimHost.Integration.Tests;

/// <summary>
/// Verifies CGF <see cref="ClusterSlave"/> dispatch for <c>PrepareLive</c> (operationId=9)
/// after the BATCH-19 A.1 fix: <see cref="ReferenceScenarioLoadHandler"/> must be invoked for
/// scenario <c>PrepareLive</c> payloads (with <c>ScenarioId</c>), and the error-log path
/// must be exercised for branch-style payloads (ExerciseId, no ScenarioId).
///
/// <para>These tests use the internal <c>ClusterSlave()</c> test constructor and
/// <c>EnqueueCommandForTest</c> — no DDS required.</para>
/// </summary>
public sealed class CgfPrepareLiveDispatchTests : IDisposable
{
    private readonly string _tempRoot;

    public CgfPrepareLiveDispatchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgf_dispatch_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a <c>PrepareLive</c> (operationId=9) command with a <c>ScenarioId</c> in the
    /// payload is dispatched through the CGF <see cref="ClusterSlave"/>,
    /// <see cref="ReferenceScenarioLoadHandler.PrepareCallCountForTest"/> must be incremented
    /// and <see cref="FailLoudRecordReplayStub"/> must <b>not</b> intercept the command.
    ///
    /// <para>
    /// This is the regression guard for BATCH-19 A.1: before the fix,
    /// <see cref="FailLoudRecordReplayStub.CanHandle"/> returned <c>true</c> for
    /// <c>PrepareLive</c>, so <see cref="ReferenceScenarioLoadHandler.PrepareAsync"/> was never
    /// called on the CGF node.
    /// </para>
    /// </summary>
    [Fact]
    public void PrepareLive_WithScenarioId_RoutesToScenarioLoadClusterStateHandler()
    {
        var serializer = new ScenarioSerializerBuilder("Hrot.CGF").Build();
        var handler    = new ReferenceScenarioLoadHandler(serializer, new LocalDiskStorageProvider(_tempRoot));
        var stub       = new FailLoudRecordReplayStub("CGF-test");

        using var slave = new ClusterSlave();
        slave.RegisterHandler(new HrotHandlerAdapter(stub));  // same registration order as CgfApplication
        slave.RegisterHandler(handler);

        var scenarioId = "test_scenario_01";
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = FDP.Toolkit.Orchestration.NodeOpType.PrepareLive,
            DomainPayload = scenarioId,
        });

        slave.Tick();

        // ReferenceScenarioLoadHandler must have been called (directory not found → Info log + return).
        Assert.Equal(1, handler.PrepareCallCountForTest);
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a <c>PrepareLive</c> command with only a <c>ExerciseId</c> (branch payload) is
    /// dispatched, <see cref="ReferenceScenarioLoadHandler"/> is still the handler that
    /// receives the command — its missing-ScenarioId guard logs and returns null.
    ///
    /// <para>Ensures the stub's absence from <c>PrepareLive</c> does not silently swallow
    /// the command; instead the handler receives and processes it.</para>
    /// </summary>
    [Fact]
    public void PrepareLive_WithExerciseIdOnly_RoutesToScenarioLoadClusterStateHandler()
    {
        var serializer = new ScenarioSerializerBuilder("Hrot.CGF").Build();
        var handler    = new ReferenceScenarioLoadHandler(serializer, new LocalDiskStorageProvider(_tempRoot));
        var stub       = new FailLoudRecordReplayStub("CGF-test");

        using var slave = new ClusterSlave();
        slave.RegisterHandler(new HrotHandlerAdapter(stub));
        slave.RegisterHandler(handler);

        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = FDP.Toolkit.Orchestration.NodeOpType.PrepareLive,
            DomainPayload = null,  // no scenario ID → handler skips gracefully
        });

        slave.Tick();

        // ReferenceScenarioLoadHandler must have been invoked for the branch payload too.
        Assert.Equal(1, handler.PrepareCallCountForTest);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a <c>PrepareLive</c> is dispatched alongside a scenario JSON file whose
    /// <c>SubsystemType</c> matches <c>"Hrot.CGF"</c>, the handler peeks the file, logs
    /// "matched", and does not throw.
    /// </summary>
    [Fact]
    public async Task PrepareLive_MatchingScenarioFile_HandlerPeeksSuccessfully()
    {
        var scenarioId  = "cgf_match_" + Guid.NewGuid().ToString("N");
        var scenarioDir = Path.Combine(_tempRoot, scenarioId);
        Directory.CreateDirectory(scenarioDir);

        // Write a minimal CGF scenario JSON with matching SubsystemType header.
        var json = "{\"Header\":{\"SubsystemType\":\"Hrot.CGF\",\"SchemaVersion\":1},\"Entities\":{}}";
        await File.WriteAllTextAsync(Path.Combine(scenarioDir, "Hrot.CGF.json"), json);

        var serializer = new ScenarioSerializerBuilder("Hrot.CGF").Build();
        var handler    = new ReferenceScenarioLoadHandler(serializer, new LocalDiskStorageProvider(_tempRoot));

        var cmd = new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = FDP.Toolkit.Orchestration.NodeOpType.PrepareLive,
            DomainPayload = scenarioId,
        };

        // Call PrepareAsync directly — verify it completes without exception and returns null.
        var result = await handler.PrepareAsync(cmd, CancellationToken.None);
        Assert.Null(result); // null == success

        // PrepareCallCountForTest must be 1.
        Assert.Equal(1, handler.PrepareCallCountForTest);
    }
}
