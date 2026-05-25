using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Tests for TASK-EQS-033: <see cref="EqsCognitiveBuffer.LastUpdateTimeSeconds"/> field.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsLastUpdateTimeTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly EqsResultUpdateSystem _system = new();

    public EqsLastUpdateTimeTests()
    {
        _repo = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(_repo);
    }

    public void Dispose()
    {
        if (_repo.HasSingletonUnmanaged<EqsResultPool>())
        {
            ref var rp = ref _repo.GetSingletonUnmanaged<EqsResultPool>();
            if (rp.Results.IsCreated) rp.Results.Dispose();
        }
        _repo.Dispose();
    }

    // ── T-LUT1: Path B stamps LastUpdateTimeSeconds ───────────────────────────

    /// <summary>
    /// T-LUT1: The offline <see cref="EqsResultEvent"/> path (Path B) must stamp
    /// <see cref="EqsCognitiveBuffer.LastUpdateTimeSeconds"/> with the current
    /// <see cref="EntityRepository.SimulationTime"/>.
    /// </summary>
    [Fact]
    public void CognitiveBuffer_StampsLastUpdateTimeSeconds_PathB()
    {
        // Arrange: entity with sensor and NetworkIdentity.
        const long netId = 8300L;
        const uint epoch = 1u;

        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new EqsSensor { Epoch = epoch });
        _repo.AddComponent(entity, new NetworkIdentity { Value = netId });

        // Set simulation time before running the system.
        _repo.SetSimulationTime(5.0f);

        // Set up EqsResultPool with one pre-written candidate.
        var results = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, Allocator.Persistent);
        results[0] = new EqsResult { EntityId = 0L, PositionX = 10f, PositionY = 0f, Score = 1f };
        var pool = new EqsResultPool { Results = results, NextFreeIndex = 1 };
        _repo.SetSingletonUnmanaged(pool);

        // Publish Path B event (unmanaged EqsResultEvent).
        _repo.Bus.Publish(new EqsResultEvent
        {
            ParentNetworkId = netId,
            LocalChildIndex = 0,
            Epoch           = epoch,
            RefreshTick     = 5u,
            ResultHandle    = 0,
            EntryCount      = 1,
        });
        _repo.Bus.SwapBuffers();

        // Act.
        _system.Execute(_repo, 0.016f);

        // Assert: buffer must be ready and stamped with simulation time 5.0.
        Assert.True(_repo.HasComponent<EqsCognitiveBuffer>(entity), "EqsCognitiveBuffer must be added");
        ref readonly var buffer = ref _repo.GetComponentRO<EqsCognitiveBuffer>(entity);
        Assert.True(buffer.IsReady, "Buffer must be ready");
        Assert.Equal(5.0f, buffer.LastUpdateTimeSeconds, precision: 4);
    }

    // ── T-LUT2: Path A stamps LastUpdateTimeSeconds for empty result set ──────

    /// <summary>
    /// T-LUT2: The managed <see cref="EqsResultUpdateEvent"/> path (Path A) must stamp
    /// <see cref="EqsCognitiveBuffer.LastUpdateTimeSeconds"/> even when the result set is
    /// empty (Count == 0), and the buffer must still become <see cref="EqsCognitiveBuffer.IsReady"/>.
    /// </summary>
    [Fact]
    public void CognitiveBuffer_StampsOnEmptyUpdate_PathA()
    {
        // Arrange: entity with sensor.
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new EqsSensor { Epoch = 2u });
        _repo.AddComponent(entity, new NetworkIdentity { Value = 8301L });

        _repo.SetSimulationTime(5.5f);

        // Publish Path A managed event with an empty result list.
        _repo.Bus.PublishManaged(new EqsResultUpdateEvent
        {
            Observer    = entity,
            Epoch       = 2u,
            RefreshTick = 3u,
            Results     = new List<EqsResultEntry>(),
        });
        _repo.Bus.SwapBuffers();

        // Act.
        _system.Execute(_repo, 0.016f);

        // Assert: buffer is ready with Count == 0 and correct time stamp.
        Assert.True(_repo.HasComponent<EqsCognitiveBuffer>(entity), "EqsCognitiveBuffer must be added");
        ref readonly var buffer = ref _repo.GetComponentRO<EqsCognitiveBuffer>(entity);
        Assert.True(buffer.IsReady, "Buffer must be ready even with empty result set");
        Assert.Equal(0, buffer.Count);
        Assert.Equal(5.5f, buffer.LastUpdateTimeSeconds, precision: 4);
    }

    // ── T-LUT3: GetSpanRW still works after struct layout change ─────────────

    /// <summary>
    /// T-LUT3: Verifies that adding <see cref="EqsCognitiveBuffer.LastUpdateTimeSeconds"/>
    /// has not introduced a struct layout regression that breaks <c>GetSpanRW()</c> write
    /// persistence.  Mirrors the existing <c>EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy</c>
    /// test so both run on every CI build.
    /// </summary>
    [Fact]
    public void EqsCognitiveBuffer_GetSpanRW_StillWorksAfterLayoutChange()
    {
        // Write via span API (correct path, no defensive copy).
        var buffer = new EqsCognitiveBuffer();
        buffer.GetSpanRW()[0] = new EqsResult { EntityId = 77L, Score = 3.14f, PositionX = 5f };

        // Read back and verify value was persisted.
        ref readonly var read = ref buffer.GetSpanRO()[0];
        Assert.Equal(77L, read.EntityId);
        Assert.Equal(3.14f, read.Score, precision: 5);
        Assert.Equal(5f, read.PositionX);
    }
}
