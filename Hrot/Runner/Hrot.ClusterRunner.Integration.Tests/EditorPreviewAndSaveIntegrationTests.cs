using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-U003 â€” IT-5: Headless Editor Preview / Rewind integration test.
///
/// <para>Proves the full memory-snapshot lifecycle:</para>
/// <list type="number">
///   <item>Spawn an entity and move it to (100, 0, 0).</item>
///   <item><c>LoadingPreview</c> â€” snapshot captured.</item>
///   <item>Move entity to (999, 0, 0) â€” dirty preview state.</item>
///   <item><c>UnloadingPreview</c> â€” entity rewound to (100, 0, 0).</item>
///   <item><c>SaveScenario â†’ NewScenario â†’ LoadScenario</c> â€” file round-trip.</item>
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

        // â”€â”€ Step 1: Start fresh â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        harness.Editor.NewScenario();
        harness.PumpFrames(1);

        // â”€â”€ Step 2: Spawn entity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Step 3: Find the spawned entity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Assert.True(
            harness.EntityMap.TryGetEntity(TestNetworkId, out var spawnedEntity),
            "EntityMap must contain the spawned entity");

        // â”€â”€ Step 4: Move entity to (100, 0, 0) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Step 5: Capture snapshot (LoadingPreview) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var handler = new PreviewClusterOpHandler(harness.Repo);
        handler.Commit(
            new NodeOpCommand { PayloadJson = "{\"TargetState\": 20}" },
            null);

        // â”€â”€ Step 6: Move entity to (999, 0, 0) in preview â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        harness.Bus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = TestNetworkId,
            ComponentsToUpdate = new List<object>
            {
                new SimTransform { Position = new Vector3(999f, 0f, 0f) },
            },
        });
        harness.PumpFrames(5);

        // â”€â”€ Step 7: Assert preview state visible â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var tfDuringPreview = harness.Repo.GetComponent<SimTransform>(spawnedEntity);
        Assert.Equal(999f, tfDuringPreview.Position.X, precision: 2);

        // â”€â”€ Step 8: Rewind (UnloadingPreview) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        handler.Commit(
            new NodeOpCommand { PayloadJson = "{\"TargetState\": 22}" },
            null);

        // â”€â”€ Step 9: Assert state restored to snapshot â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var tfAfterRewind = harness.Repo.GetComponent<SimTransform>(spawnedEntity);
        Assert.Equal(100f, tfAfterRewind.Position.X, precision: 2);

        // â”€â”€ Step 10: Save scenario â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        harness.Editor.SaveScenario(_tempFile);
        Assert.True(File.Exists(_tempFile), "Saved scenario file must exist");
        Assert.True(new FileInfo(_tempFile).Length > 0, "Saved scenario file must not be empty");

        // â”€â”€ Step 11: NewScenario â€” assert world is empty â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        harness.Editor.NewScenario();
        harness.PumpFrames(2);
        Assert.Equal(0, harness.Repo.EntityCount);

        // â”€â”€ Step 12: LoadScenario â€” assert entity restored â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
