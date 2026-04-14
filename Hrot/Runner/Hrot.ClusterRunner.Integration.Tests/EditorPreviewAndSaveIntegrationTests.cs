using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Hrot.Common.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.ModuleHost_Core.Network.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-U003 — IT-5: Headless Editor Preview / Rewind integration test.
///
/// <para>Proves the full memory-snapshot lifecycle:</para>
/// <list type="number">
///   <item>Spawn an entity and move it to (100, 0, 0).</item>
///   <item><c>LoadingPreview</c> — snapshot captured.</item>
///   <item>Move entity to (999, 0, 0) — dirty preview state.</item>
///   <item><c>UnloadingPreview</c> — entity rewound to (100, 0, 0).</item>
///   <item><c>SaveScenario → NewScenario → LoadScenario</c> — file round-trip.</item>
/// </list>
///
/// <para>No DDS or network calls are made. Test runs entirely in memory.</para>
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class EditorPreviewAndSaveIntegrationTests : IDisposable
{
    private const long   TestNetworkId  = 42L;
    private const long   TestTkbType    = 1L;   // Matches TkbTemplate("TestUnit", 1L) in EditorHarness
    private const int    PumpTimeoutMs  = 5_000;

    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    [Fact]
    public void EditorPreview_SnapshotsAndRestoresState()
    {
        using var harness = new EditorHarness();

        // ── Step 1: Start fresh ───────────────────────────────────────────────
        harness.Editor.NewScenario();
        harness.PumpFrames(1);

        // ── Step 2: Spawn entity ──────────────────────────────────────────────
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });
        Assert.True(
            harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs),
            "Entity should appear within 5 s");

        // ── Step 3: Find the spawned entity ───────────────────────────────────
        Assert.True(
            harness.EntityMap.TryGetEntity(TestNetworkId, out var spawnedEntity),
            "EntityMap must contain the spawned entity");

        // ── Step 4: Move entity to (100, 0, 0) ───────────────────────────────
        harness.Bus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = TestNetworkId,
            ComponentsToUpdate = new List<object>
            {
                new SimTransform { Position = new Vector3(100f, 0f, 0f) },
            },
        });
        harness.PumpFrames(5);

        // Verify move applied
        var tfBeforePreview = harness.Repo.GetComponent<SimTransform>(spawnedEntity);
        Assert.Equal(100f, tfBeforePreview.Position.X, precision: 2);

        // ── Step 5: Capture snapshot (LoadingPreview) ─────────────────────────
        var handler = new PreviewClusterOpHandler(harness.Repo);
        handler.Commit(
            new NodeOpCommand { PayloadJson = "{\"TargetState\": 20}" },
            null);

        // ── Step 6: Move entity to (999, 0, 0) in preview ────────────────────
        harness.Bus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = TestNetworkId,
            ComponentsToUpdate = new List<object>
            {
                new SimTransform { Position = new Vector3(999f, 0f, 0f) },
            },
        });
        harness.PumpFrames(5);

        // ── Step 7: Assert preview state visible ──────────────────────────────
        var tfDuringPreview = harness.Repo.GetComponent<SimTransform>(spawnedEntity);
        Assert.Equal(999f, tfDuringPreview.Position.X, precision: 2);

        // ── Step 8: Rewind (UnloadingPreview) ────────────────────────────────
        handler.Commit(
            new NodeOpCommand { PayloadJson = "{\"TargetState\": 22}" },
            null);

        // ── Step 9: Assert state restored to snapshot ─────────────────────────
        var tfAfterRewind = harness.Repo.GetComponent<SimTransform>(spawnedEntity);
        Assert.Equal(100f, tfAfterRewind.Position.X, precision: 2);

        // ── Step 10: Save scenario ────────────────────────────────────────────
        harness.Editor.SaveScenario(_tempFile);
        Assert.True(File.Exists(_tempFile), "Saved scenario file must exist");
        Assert.True(new FileInfo(_tempFile).Length > 0, "Saved scenario file must not be empty");

        // ── Step 11: NewScenario — assert world is empty ──────────────────────
        harness.Editor.NewScenario();
        harness.PumpFrames(2);
        Assert.Equal(0, harness.Repo.EntityCount);

        // ── Step 12: LoadScenario — assert entity restored ────────────────────
        harness.Editor.LoadScenario(_tempFile);
        harness.PumpFrames(5);

        Assert.Equal(1, harness.Repo.EntityCount);

        // Find the restored entity by querying the repo for SimTransform
        bool positionRestored = false;
        foreach (var entity in harness.Repo.Query().With<SimTransform>().Build())
        {
            var tf = harness.Repo.GetComponent<SimTransform>(entity);
            if (MathF.Abs(tf.Position.X - 100f) < 0.5f)
            {
                positionRestored = true;
                break;
            }
        }
        Assert.True(positionRestored, "Entity position should be 100f after LoadScenario");
    }
}
