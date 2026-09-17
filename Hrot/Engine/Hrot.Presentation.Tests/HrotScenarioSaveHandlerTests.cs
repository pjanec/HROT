using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.ScenarioEditor.Handlers;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// ⭐⭐⭐ CE-275 ③ / CE-277(c1) — the ONE declarative scenario save handler. Proves it (a) writes the node's
/// owned slice (ownership gate applied) to its PER-NODE staging root and reports a <see cref="FileManifestResult"/>
/// (tagged with its <c>$meta.docType</c>) so the orchestrator can pull + merge, and (b) ignores a non-scenario
/// <see cref="NodeOpType.SerializeLocal"/> payload so it coexists with <c>ReferenceArchiveHandler</c>.
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4/§4a.
/// </summary>
public sealed class HrotScenarioSaveHandlerTests : IDisposable
{
    [ComponentId(221)]
    private struct SaveMarker { public float X; }

    private const int NodeId = 1;
    private readonly string _scenarioName = "test-" + Guid.NewGuid().ToString("N");

    public HrotScenarioSaveHandlerTests() => ComponentTypeRegistry.Clear();

    public void Dispose()
    {
        // Clean the per-node staging dir this test wrote into.
        try
        {
            var dir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), _scenarioName);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
        ComponentTypeRegistry.Clear();
    }

    private static EntityRepository BuildWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SaveMarker>();
        repo.RegisterComponent<NetworkAuthority>();
        return repo;
    }

    private string ExpectedSliceFile =>
        Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), _scenarioName, "scenario.json");

    [Fact]
    public void PrepareAsync_WritesOwnedSlice_ToNodeStaging_AndReportsAManifest()
    {
        var repo = BuildWorld();

        // Owned by this node (1) → saved.
        var owned = repo.CreateEntity();
        repo.SetComponent(owned, new SaveMarker { X = 1f });
        repo.AddComponent(owned, new NetworkAuthority(primaryOwnerId: NodeId, localNodeId: NodeId));

        // Owned by a FOREIGN node (2) → excluded by the gate.
        var foreign = repo.CreateEntity();
        repo.SetComponent(foreign, new SaveMarker { X = 2f });
        repo.AddComponent(foreign, new NetworkAuthority(primaryOwnerId: 2, localNodeId: NodeId));

        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var handler    = new HrotScenarioSaveHandler(serializer, tkbDb: null, world: repo, nodeId: NodeId);

        var intent = new ExecuteNodeOpIntent
        {
            Operation     = NodeOpType.SerializeLocal,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new ScenarioSaveHandlerPayload(_scenarioName),
        };

        var result = handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

        // Reports a manifest so the orchestrator pulls the slice, tagged with our format.
        var manifest = Assert.IsType<FileManifestResult[]>(result);
        Assert.Single(manifest);
        Assert.Equal("Hrot.Scenario", manifest[0].DocType);
        Assert.Equal(ExpectedSliceFile, manifest[0].SourceUnc);
        Assert.Contains(".slices", manifest[0].RelativeDest);
        Assert.Contains($"node_{NodeId}", manifest[0].RelativeDest);

        // Written to per-node staging, and the gate flowed: owned present, foreign absent.
        Assert.True(File.Exists(ExpectedSliceFile), $"expected slice at {ExpectedSliceFile}");
        var dom      = JsonNode.Parse(File.ReadAllText(ExpectedSliceFile))!.AsObject();
        var entities = dom["Entities"]!.AsObject();
        Assert.Single(entities);
    }

    [Fact]
    public void PrepareAsync_IgnoresNonScenarioPayload_SoItCoexistsWithTheArchiveHandler()
    {
        var repo       = BuildWorld();
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var handler    = new HrotScenarioSaveHandler(serializer, tkbDb: null, world: repo, nodeId: NodeId);

        var intent = new ExecuteNodeOpIntent
        {
            Operation     = NodeOpType.SerializeLocal,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new ArchiveHandlerPayload(Guid.NewGuid()),   // the .fdp checkpoint payload
        };

        var result = handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Null(result);                                   // not ours — no-op
        Assert.False(File.Exists(ExpectedSliceFile));          // wrote nothing
    }

    [Fact]
    public void CanHandle_OnlySerializeLocal()
    {
        var repo       = BuildWorld();
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var handler    = new HrotScenarioSaveHandler(serializer, tkbDb: null, world: repo, nodeId: NodeId);

        Assert.True(handler.CanHandle(NodeOpType.SerializeLocal));
        Assert.False(handler.CanHandle(NodeOpType.PrepareState));
    }
}
