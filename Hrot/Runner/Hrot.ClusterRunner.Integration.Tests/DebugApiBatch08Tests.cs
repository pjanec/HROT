using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-08 Tier-1 gate — exercises Group H (checkpoint/restore + diff) endpoints
/// against the offline <see cref="EditorHarness"/>. No HTTP; runs fast.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch08Tests
{
    // ── Checkpoint / Restore ─────────────────────────────────────────────────

    [Fact]
    public void Checkpoint_EntersPreviewMode()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        Assert.False(h.Preview.IsInPreviewMode);
        var result = svc.Checkpoint();
        Assert.True(h.Preview.IsInPreviewMode);
        Assert.True(result["inPreview"]!.GetValue<bool>());
    }

    [Fact]
    public void Checkpoint_WhenAlreadyInPreview_ThrowsInvalidOperation()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Enter via /preview/enter
        svc.EnterPreview(startPaused: false);
        Assert.True(h.Preview.IsInPreviewMode);

        // /checkpoint should reject (already in preview slot)
        Assert.Throws<InvalidOperationException>(() => svc.Checkpoint());
    }

    [Fact]
    public void Checkpoint_WhenAlreadyCheckpointed_ThrowsInvalidOperation()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        svc.Checkpoint();
        Assert.Throws<InvalidOperationException>(() => svc.Checkpoint());
    }

    [Fact]
    public void RestoreCheckpoint_ExitsPreviewMode()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        svc.Checkpoint();
        Assert.True(h.Preview.IsInPreviewMode);

        var result = svc.RestoreCheckpoint();
        Assert.False(h.Preview.IsInPreviewMode);
        Assert.False(result["inPreview"]!.GetValue<bool>());
    }

    [Fact]
    public void RestoreCheckpoint_WhenNotCheckpointed_ThrowsInvalidOperation()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        Assert.Throws<InvalidOperationException>(() => svc.RestoreCheckpoint());
    }

    [Fact]
    public void Status_InPreview_ReflectsCheckpoint()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var beforeStatus = svc.GetStatus();
        Assert.False(beforeStatus["inPreview"]!.GetValue<bool>());

        svc.Checkpoint();
        var duringStatus = svc.GetStatus();
        Assert.True(duringStatus["inPreview"]!.GetValue<bool>());

        svc.RestoreCheckpoint();
        var afterStatus = svc.GetStatus();
        Assert.False(afterStatus["inPreview"]!.GetValue<bool>());
    }

    [Fact]
    public void Checkpoint_Restore_EntityReverts()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn entity via the real spawn pipeline (gives it NetworkIdentity + SimTransform)
        const long networkId = 42_000L;
        const long tkbType   = 1L;
        var spawnCmd = new SpawnEntityCommand
        {
            TkbType          = tkbType,
            NetworkId        = networkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(10f, 0f, 0f) },
        };
        h.Bus.PublishManaged(spawnCmd);
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), 5000),
            "Entity did not spawn within timeout.");

        // Read entity and verify initial position
        Assert.True(h.EntityMap.TryGetEntity(networkId, out var entity));
        var posInitial = h.Repo.GetComponent<SimTransform>(entity).Position;

        // Checkpoint
        svc.Checkpoint();

        // Advance one frame so _globalVersion increments: GetRefRW only bumps the chunk version
        // when currentVersion != chunkVersion (i.e., the version changed since last write).
        // Without this tick the chunk version stays equal to _globalVersion → SyncDirtyChunks
        // sees no change and does not restore the chunk.  This mirrors the pattern in
        // PreviewClusterOpHandlerTests.UnloadingPreview_RewindsLiveRepo (line 116: _liveRepo.Tick()).
        h.PumpFrames(1);

        // Mutate the entity position after the tick so the chunk is marked dirty
        h.Repo.SetComponent(entity, new SimTransform { Position = new Vector3(99f, 0f, 0f) });

        // Verify it's now 99 (direct read after direct write)
        var posMutated = h.Repo.GetComponent<SimTransform>(entity).Position;
        Assert.Equal(99f, posMutated.X, precision: 1);

        // Restore — PreviewClusterOpHandler.SyncFrom restores the full repo snapshot
        svc.RestoreCheckpoint();
        // Read immediately after restore (before any kernel tick that might re-apply changes).
        // The EntityMap still holds the same entity handle; SyncFrom preserves entity indices.
        Assert.True(h.EntityMap.TryGetEntity(networkId, out var entityRestored));
        var posRestored = h.Repo.GetComponent<SimTransform>(entityRestored).Position;

        // Position should revert to initial (within 2 decimal places)
        Assert.Equal(posInitial.X, posRestored.X, precision: 2);
    }

    // ── Diff (capture + compare) ──────────────────────────────────────────────

    [Fact]
    public void CaptureBaseline_ReturnsBaselineId()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.CaptureBaseline().AsObject();
        Assert.NotNull(result["baselineId"]);
        var id = result["baselineId"]!.GetValue<string>();
        Assert.True(id.StartsWith("BL#"), $"Expected 'BL#N' format, got '{id}'");
    }

    [Fact]
    public void CompareBaseline_UnchangedEntity_NoDiff()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn entity via real pipeline so it gets NetworkIdentity
        const long networkId = 43_000L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = networkId, OwnerNodeId = 0,
            InitType = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(5f, 0f, 0f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), 5000));

        // Capture baseline
        var captureResult = svc.CaptureBaseline(new[] { networkId });
        var baselineId = captureResult["baselineId"]!.GetValue<string>();

        // No mutation — compare immediately
        var diffResult = svc.CompareBaseline(baselineId, new[] { networkId });
        var entities = diffResult["entities"]!.AsArray();

        // No entity should appear in diff (unchanged within epsilon)
        Assert.Empty(entities);
    }

    [Fact]
    public void CompareBaseline_ChangedEntity_ShowsDiff()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn entity via real pipeline so it gets NetworkIdentity
        const long networkId = 44_000L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = networkId, OwnerNodeId = 0,
            InitType = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(5f, 0f, 0f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), 5000));

        Assert.True(h.EntityMap.TryGetEntity(networkId, out var entity));

        // Capture baseline BEFORE mutation
        var captureResult = svc.CaptureBaseline(new[] { networkId });
        var baselineId = captureResult["baselineId"]!.GetValue<string>();

        // Mutate position significantly (well beyond epsilon of 0.001)
        ref var tf2 = ref h.Repo.GetComponentRW<SimTransform>(entity);
        tf2.Position = new Vector3(500f, 0f, 0f);
        h.PumpFrames(1);

        // Diff
        var diffResult = svc.CompareBaseline(baselineId, new[] { networkId });
        var entities = diffResult["entities"]!.AsArray();

        // The changed entity should appear in the diff
        Assert.NotEmpty(entities);
        Assert.Equal(networkId, entities[0]!["networkId"]!.GetValue<long>());
        Assert.True(entities[0]!["changed"]!.GetValue<bool>());
    }

    [Fact]
    public void CompareBaseline_UnknownId_ThrowsArgumentException()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        Assert.Throws<ArgumentException>(() => svc.CompareBaseline("BL#99999"));
    }

    [Fact]
    public void CompareBaseline_EntityBirth_ShowsInDiff()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Capture baseline with empty world (no entities yet)
        var captureResult = svc.CaptureBaseline();
        var baselineId = captureResult["baselineId"]!.GetValue<string>();

        // Spawn a new entity AFTER capturing baseline (via real pipeline for NetworkIdentity)
        const long newNetworkId = 45_000L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = newNetworkId, OwnerNodeId = 0,
            InitType = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(1f, 2f, 3f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(newNetworkId, out _), 5000),
            "Spawned entity did not appear within timeout.");

        // Compare: the new entity should appear (entity birth — before=null, after=serialized)
        var diffResult = svc.CompareBaseline(baselineId);
        var entities = diffResult["entities"]!.AsArray();

        bool found = false;
        foreach (var e in entities)
        {
            if (e!["networkId"]?.GetValue<long>() == newNetworkId)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "New entity should appear in diff as entity birth.");
    }
}
