using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.CGF.Modules.Orchestration;
using Bagira.CGF.Modules.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using Xunit;

namespace Bagira.SimHost.Integration.Tests;

/// <summary>
/// Verifies CGF <see cref="DrillSlave"/> dispatch for <see cref="NodeOpType.PrepareLive"/>
/// after the BATCH-19 A.1 fix: <see cref="ScenarioLoadDsmHandler"/> must be invoked for
/// scenario <c>PrepareLive</c> payloads (with <c>ScenarioId</c>), and the error-log path
/// must be exercised for branch-style payloads (DrillId, no ScenarioId).
///
/// <para>These tests use the internal <c>DrillSlave()</c> test constructor and
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
    /// When a <see cref="NodeOpType.PrepareLive"/> command with a <c>ScenarioId</c> in the
    /// payload is dispatched through the CGF <see cref="DrillSlave"/>,
    /// <see cref="ScenarioLoadDsmHandler.PrepareCallCountForTest"/> must be incremented and
    /// <see cref="FailLoudRecordReplayStub"/> must <b>not</b> intercept the command.
    ///
    /// <para>
    /// This is the regression guard for BATCH-19 A.1: before the fix,
    /// <see cref="FailLoudRecordReplayStub.CanHandle"/> returned <c>true</c> for
    /// <c>PrepareLive</c>, so <see cref="ScenarioLoadDsmHandler.PrepareAsync"/> was never
    /// called on the CGF node.
    /// </para>
    /// </summary>
    [Fact]
    public void PrepareLive_WithScenarioId_RoutesToScenarioLoadDsmHandler()
    {
        var serializer = new ScenarioSerializerBuilder("Bagira.CGF").Build();
        var handler    = new ScenarioLoadDsmHandler(serializer, _tempRoot);
        var stub       = new FailLoudRecordReplayStub("CGF-test");

        using var slave = new DrillSlave();
        slave.RegisterHandler(stub);    // same registration order as CgfApplication
        slave.RegisterHandler(handler);

        var scenarioId = "test_scenario_01";
        slave.EnqueueCommandForTest(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareLive,
            PayloadJson   = $"{{\"ScenarioId\":\"{scenarioId}\"}}",
        });

        slave.Tick();

        // ScenarioLoadDsmHandler must have been called (directory not found → Info log + return).
        Assert.Equal(1, handler.PrepareCallCountForTest);
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a <see cref="NodeOpType.PrepareLive"/> command with only a <c>DrillId</c> (branch
    /// payload) is dispatched, <see cref="ScenarioLoadDsmHandler"/> is still the handler that
    /// receives the command — its <c>HasDrillId</c> guard logs Error and returns.
    ///
    /// <para>Ensures the stub's absence from <c>PrepareLive</c> does not silently swallow
    /// the command; instead the error is surfaced by <see cref="ScenarioLoadDsmHandler"/>.</para>
    /// </summary>
    [Fact]
    public void PrepareLive_WithDrillIdOnly_RoutesToScenarioLoadDsmHandler()
    {
        var serializer = new ScenarioSerializerBuilder("Bagira.CGF").Build();
        var handler    = new ScenarioLoadDsmHandler(serializer, _tempRoot);
        var stub       = new FailLoudRecordReplayStub("CGF-test");

        using var slave = new DrillSlave();
        slave.RegisterHandler(stub);
        slave.RegisterHandler(handler);

        slave.EnqueueCommandForTest(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareLive,
            PayloadJson   = $"{{\"DrillId\":\"{Guid.NewGuid():D}\"}}",
        });

        slave.Tick();

        // ScenarioLoadDsmHandler must have been invoked for the branch payload too.
        Assert.Equal(1, handler.PrepareCallCountForTest);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a <see cref="NodeOpType.PrepareLive"/> is dispatched alongside a scenario JSON
    /// file whose <c>SubsystemType</c> matches <c>"Bagira.CGF"</c>, the handler peeks the
    /// file, logs "matched", and does not throw.
    /// </summary>
    [Fact]
    public async Task PrepareLive_MatchingScenarioFile_HandlerPeeksSuccessfully()
    {
        var scenarioId  = "cgf_match_" + Guid.NewGuid().ToString("N");
        var scenarioDir = Path.Combine(_tempRoot, scenarioId);
        Directory.CreateDirectory(scenarioDir);

        // Write a minimal CGF scenario JSON with matching SubsystemType header.
        var json = "{\"Header\":{\"SubsystemType\":\"Bagira.CGF\",\"SchemaVersion\":1},\"Entities\":{}}";
        await File.WriteAllTextAsync(Path.Combine(scenarioDir, "Bagira.CGF.json"), json);

        var serializer = new ScenarioSerializerBuilder("Bagira.CGF").Build();
        var handler    = new ScenarioLoadDsmHandler(serializer, _tempRoot);

        var cmd = new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareLive,
            PayloadJson   = $"{{\"ScenarioId\":\"{scenarioId}\"}}",
        };

        // Call PrepareAsync directly — verify it completes without exception and returns null.
        var result = await handler.PrepareAsync(cmd, CancellationToken.None);
        Assert.Null(result); // null == success

        // PrepareCallCountForTest must be 1.
        Assert.Equal(1, handler.PrepareCallCountForTest);
    }
}
