using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Fdp.ModuleHost_Core.Network.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK2-R004 — IT-1: Offline Editor integration tests.
/// Exercises spawn / edit / delete command routing via EditorHarness
/// without any DDS participant. Asserts that no DDS writer is ever called.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class OfflineEditorIntegrationTests
{
    // ── Test double: counts DDS write calls ──────────────────────────────────

    private sealed class RecordingDdsWriter : IDdsWriter<CreateEntityRequest>
    {
        public int CallCount { get; private set; }
        public void Write(CreateEntityRequest sample) => CallCount++;
        public void DisposeInstance(CreateEntityRequest key) { }
    }

    // ── Test constants ────────────────────────────────────────────────────────

    private const long  TestTkbType    = 1L;   // matches TkbTemplate registered in EditorHarness
    private const long  TestNetworkId  = 42L;  // deterministic; non-zero = no allocator call
    private const int   PumpTimeoutMs  = 5_000;

    // =========================================================================
    // IT-1a: Spawn
    // =========================================================================

    [Fact]
    public void SpawnCommand_LocalRepo_NoNetworkTraffic()
    {
        var writer = new RecordingDdsWriter();

        using var harness = new EditorHarness();

        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });

        bool appeared = harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs);

        Assert.True(appeared, "Entity should appear in the local repo within 5 s");
        Assert.Equal(1, harness.Repo.EntityCount);
        Assert.Equal(0, writer.CallCount); // no DDS translator packs installed → zero writes
    }

    // =========================================================================
    // IT-1b: Edit
    // =========================================================================

    [Fact]
    public void EditCommand_UpdatesRepoInPlace()
    {
        var writer = new RecordingDdsWriter();
        const float NorthOffsetMetres = 100f;

        using var harness = new EditorHarness();

        // 1. Spawn
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = Vector3.Zero },
        });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs));

        // 2. Edit — shift 100 m north (Y axis)
        harness.Bus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = TestNetworkId,
            ComponentsToUpdate = new List<object>
            {
                new SimTransform { Position = new Vector3(0f, NorthOffsetMetres, 0f) }
            },
        });

        // 3. Assert position updated
        bool updated = harness.PumpUntil(() =>
        {
            if (!harness.EntityMap.TryGetEntity(TestNetworkId, out var e)) return false;
            ref readonly var t = ref harness.Repo.GetComponentRO<SimTransform>(e);
            return MathF.Abs(t.Position.Y - NorthOffsetMetres) < 0.01f;
        }, PumpTimeoutMs);

        Assert.True(updated, "SimTransform.Position.Y should reflect the 100 m north offset");
        Assert.Equal(0, writer.CallCount);
    }

    // =========================================================================
    // IT-1c: Delete
    // =========================================================================

    [Fact]
    public void DeleteCommand_RemovesEntityFromRepo()
    {
        var writer = new RecordingDdsWriter();

        using var harness = new EditorHarness();

        // 1. Spawn
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs));

        // 2. Delete
        harness.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = TestNetworkId,
            Reason    = "test-delete",
        });

        // 3. Assert removal (ELM takes 2–3 frames — PumpUntil handles this)
        bool removed = harness.PumpUntil(() => harness.Repo.EntityCount == 0, PumpTimeoutMs);

        Assert.True(removed, "Entity should be removed from repo within 5 s");
        Assert.Equal(0, harness.Repo.EntityCount);
        Assert.Equal(0, writer.CallCount);
    }
}
