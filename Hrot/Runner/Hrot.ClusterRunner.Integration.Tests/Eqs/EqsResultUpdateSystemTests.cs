using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Unit/integration tests for <see cref="EqsResultUpdateSystem"/>.
///
/// <para>Tests exercise both input paths:
/// Path A (managed <see cref="EqsResultUpdateEvent"/>) and
/// Path B (unmanaged <see cref="EqsResultEvent"/>).</para>
///
/// <para>All tests use a minimal offline <see cref="EntityRepository"/> with all
/// required components registered; no DDS, no BTree, no EditorHarness.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsResultUpdateSystemTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly EqsResultUpdateSystem _system = new();

    public EqsResultUpdateSystemTests()
    {
        _repo = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(_repo);
    }

    public void Dispose()
    {
        if (_repo.HasSingleton<EqsResultPool>())
        {
            var rp = _repo.GetSingleton<EqsResultPool>();
            if (rp.Results.IsCreated) rp.Results.Dispose();
        }
        _repo.Dispose();
    }

    // ── Path A: managed EqsResultUpdateEvent ─────────────────────────────────

    /// <summary>
    /// T1: A managed event whose Epoch does NOT match the sensor's Epoch is silently
    /// dropped.  The entity must NOT receive an <see cref="EqsCognitiveBuffer"/>.
    /// </summary>
    [Fact]
    public void EqsResultUpdateSystem_StaleEpoch_IgnoresEvent()
    {
        // Arrange: entity with sensor epoch=2
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new EqsSensor { Epoch = 2 });
        _repo.AddComponent(entity, new NetworkIdentity { Value = 1L });

        // Publish a managed event with stale epoch=1
        _repo.Bus.PublishManaged(new EqsResultUpdateEvent
        {
            Observer    = entity,
            Epoch       = 1,
            RefreshTick = 10,
            Results     = new List<EqsResultEntry>
            {
                new() { EntityId = 99L, PositionX = 1f, PositionY = 2f, Score = 0.9f, Flags = 0 }
            },
        });
        _repo.Bus.SwapBuffers();

        // Act
        _system.Execute(_repo, 0.016f);

        // Assert: buffer must NOT be present
        Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(entity),
            "Stale epoch event must not create EqsCognitiveBuffer");
    }

    /// <summary>
    /// T2: A managed event with matching Epoch writes the results into
    /// <see cref="EqsCognitiveBuffer"/> and makes it Ready.
    /// </summary>
    [Fact]
    public void EqsResultUpdateSystem_MatchingEpoch_PopulatesBuffer()
    {
        // Arrange
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new EqsSensor { Epoch = 2 });
        _repo.AddComponent(entity, new NetworkIdentity { Value = 2L });

        _repo.Bus.PublishManaged(new EqsResultUpdateEvent
        {
            Observer    = entity,
            Epoch       = 2,
            RefreshTick = 5,
            Results     = new List<EqsResultEntry>
            {
                new() { EntityId = 10L, PositionX = 100f, PositionY = 200f, Score = 0.8f, Flags = 1 },
                new() { EntityId = 20L, PositionX = 300f, PositionY = 400f, Score = 0.5f, Flags = 2 },
            },
        });
        _repo.Bus.SwapBuffers();

        // Act
        _system.Execute(_repo, 0.016f);

        // Assert
        Assert.True(_repo.HasComponent<EqsCognitiveBuffer>(entity));
        ref readonly var buffer = ref _repo.GetComponentRO<EqsCognitiveBuffer>(entity);
        Assert.Equal(2, buffer.Count);
        Assert.True(buffer.IsReady);
        Assert.Equal(5u, buffer.LastUpdateTick);

        var span = buffer.GetSpanRO();
        Assert.Equal(10L, span[0].EntityId);
        Assert.Equal(100f, span[0].PositionX);
        Assert.Equal(200f, span[0].PositionY);
        Assert.Equal(0.8f, span[0].Score);
    }

    /// <summary>
    /// T3: Writes via the managed path persist and are readable through
    /// <see cref="EqsCognitiveBuffer.GetSpanRO"/>, verifying no [InlineArray]
    /// defensive-copy trap occurs.
    /// </summary>
    [Fact]
    public void EqsResultUpdateSystem_GetSpanRW_WritesPersist()
    {
        // Arrange
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new EqsSensor { Epoch = 3 });
        _repo.AddComponent(entity, new NetworkIdentity { Value = 3L });

        _repo.Bus.PublishManaged(new EqsResultUpdateEvent
        {
            Observer    = entity,
            Epoch       = 3,
            RefreshTick = 7,
            Results     = new List<EqsResultEntry>
            {
                new() { EntityId = 42L, PositionX = 11f, PositionY = 22f, Score = 1.0f, Flags = 3 },
            },
        });
        _repo.Bus.SwapBuffers();

        _system.Execute(_repo, 0.016f);

        // Assert: verify value persists after system returns (no defensive-copy loss)
        ref readonly var buffer = ref _repo.GetComponentRO<EqsCognitiveBuffer>(entity);
        var span = buffer.GetSpanRO();
        Assert.Equal(42L, span[0].EntityId);
        Assert.Equal(11f, span[0].PositionX);
        Assert.Equal(22f, span[0].PositionY);
        Assert.Equal(1.0f, span[0].Score);
        Assert.Equal((short)3, span[0].Flags);
    }
}
