using System;
using System.Text.Json;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.ClusterRunner.Services;
using CycloneDDS.Runtime;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="ClusterUiCache"/> (CGF1-S0506).
///
/// Each test creates a real DDS participant, writes a single sample, calls Update(),
/// and asserts the cache reflects the written state.
/// </summary>
[Collection("ClusterUiCacheTests")]
public sealed class ClusterUiCacheTests : IDisposable
{
    // Domain 27 is reserved for ClusterUiCache unit tests.
    private const int TestDomain = 27;

    private readonly DdsParticipant _participant;
    private readonly ClusterUiCache _uiCache;

    public ClusterUiCacheTests()
    {
        _participant = new DdsParticipant(TestDomain);
        _uiCache     = new ClusterUiCache(_participant);
    }

    public void Dispose()
    {
        _uiCache.Dispose();
        _participant.Dispose();
    }

    /// <summary>
    /// CGF1-S0506 SC2: Writing a SystemStateTopic sample must be reflected in
    /// <c>CurrentState</c> and <c>IsBootstrapped</c> after <c>Update()</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ReflectsSystemStateTopic()
    {
        using var writer = new DdsWriter<SystemStateTopic>(_participant);
        writer.Write(new SystemStateTopic { CurrentState = ClusterState.LoadingLive });

        Thread.Sleep(150); // DDS propagation

        _uiCache.Update();

        Assert.Equal(ClusterState.LoadingLive, _uiCache.CurrentState);
        Assert.True(_uiCache.IsBootstrapped,
            "IsBootstrapped must be true for any state other than Standby.");
    }

    /// <summary>
    /// CGF1-S0506 SC3: Writing a NodeOpCommand with PrepareState must appear in TxHistory
    /// and HasInFlightTransaction must become true.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_Sniffs2PcTraffic()
    {
        var txId = Guid.NewGuid();
        using var writer = new DdsWriter<NodeOpCommand>(_participant);
        writer.Write(new NodeOpCommand
        {
            TargetNodeId   = 1,
            TransactionId  = txId,
            Operation      = NodeOpType.PrepareState,
            PayloadJson    = $"{{\"TargetState\":{(int)ClusterState.LoadingLive}}}",
        });

        Thread.Sleep(150); // DDS propagation

        _uiCache.Update();

        Assert.Equal(1, _uiCache.TxHistory.Count);
        Assert.True(_uiCache.HasInFlightTransaction,
            "HasInFlightTransaction must be true after PrepareState NodeOpCommand.");
    }

    /// <summary>
    /// CGF1-S0506 SC-Inventory: Writing an AssetInventoryTopic sample must be reflected
    /// in <c>AvailableScenarios</c> after <c>Update()</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_UpdatesInventoryFromTopic()
    {
        using var writer = new DdsWriter<AssetInventoryTopic>(_participant);
        writer.Write(new AssetInventoryTopic
        {
            NodeId             = 0,
            LocalScenariosJson = "[\"scene1\"]",
            LocalExercisesJson    = "[]",
            ArchivedExercisesJson       = "[]",
            UnarchivedLocalExercisesJson = "[]",
        });

        Thread.Sleep(150); // DDS propagation

        _uiCache.Update();

        Assert.Equal(1, _uiCache.AvailableScenarios.Length);
        Assert.Equal("scene1", _uiCache.AvailableScenarios[0]);
    }

    /// <summary>
    /// CGF1-S0506 SC-TimeMode: Writing a SwitchTimeModeWireDto with Deterministic mode
    /// must set <c>IsPaused</c> to <c>true</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_UpdatesIsPausedFromTimeMode()
    {
        using var writer = new DdsWriter<SwitchTimeModeWireDto>(_participant);
        writer.Write(new SwitchTimeModeWireDto
        {
            TargetModeInt    = (int)TimeMode.Deterministic,
            BarrierWallTicks = 0L,
            FixedDelta       = 1f / 60f,
        });

        Thread.Sleep(150); // DDS propagation

        _uiCache.Update();

        Assert.True(_uiCache.IsPaused,
            "IsPaused must be true when SwitchTimeModeWireDto.TargetModeInt == Deterministic.");
    }

    /// <summary>
    /// CGF1-S0506: After a PrepareState command is snooped and then a SysOpStatus
    /// with Success code is received, the transaction is removed from in-flight.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_ClosesInFlightTxOnSysOpStatusSuccess()
    {
        var requestId = Guid.NewGuid();
        var txId      = Guid.NewGuid();

        using var cmdWriter    = new DdsWriter<NodeOpCommand>(_participant);
        using var statusWriter = new DdsWriter<ClusterOpStatus>(_participant);

        // First: sow a PrepareState to create an in-flight tx
        cmdWriter.Write(new NodeOpCommand
        {
            TargetNodeId  = 1,
            TransactionId = txId,
            Operation     = NodeOpType.PrepareState,
            PayloadJson   = "{}",
        });

        Thread.Sleep(150);
        _uiCache.Update();
        Assert.True(_uiCache.HasInFlightTransaction);

        // Then: write a SysOpStatus with Success (closes the in-flight)
        statusWriter.Write(new ClusterOpStatus
        {
            RequestId  = txId,   // SysOpStatus.RequestId matches TransactionId in cache lookup
            StatusCode = 0,      // OrchestrationStatusCode.Success
            ResultJson = string.Empty,
        });

        Thread.Sleep(150);
        _uiCache.Update();

        Assert.False(_uiCache.HasInFlightTransaction,
            "HasInFlightTransaction must be false after SysOpStatus.Success.");
        Assert.True(_uiCache.TxHistory[0].Completed,
            "tx.Completed must be true after Success status code.");
    }

    /// <summary>
    /// Writing a TimePulseDescriptor sample must update <c>MasterSimTime</c>,
    /// <c>MasterWallTicks</c>, and <c>MasterTimeScale</c> after <c>Update()</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterUiCache_UpdatesTimeScaleFromTimePulse()
    {
        using var writer = new DdsWriter<TimePulseDescriptor>(_participant);
        writer.Write(new TimePulseDescriptor
        {
            MasterWallTicks  = 123456789L,
            SimTimeSnapshot  = 42.5,
            TimeScale        = 2.0f,
            SequenceId       = 1L,
        });

        Thread.Sleep(150); // DDS propagation

        _uiCache.Update();

        Assert.Equal(42.5,  _uiCache.MasterSimTime,  precision: 3);
        Assert.Equal(2.0f,  _uiCache.MasterTimeScale);
        Assert.Equal(123456789L, _uiCache.MasterWallTicks);
    }
}

[CollectionDefinition("ClusterUiCacheTests", DisableParallelization = true)]
public class ClusterUiCacheTestCollection { }
