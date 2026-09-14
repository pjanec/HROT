using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.ScenarioEditor.Handlers;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// ⭐⭐⭐ CE-275 ③ — the ONE declarative scenario save handler. Proves it (a) writes the node's world through
/// the shared <see cref="ScenarioFileService.SaveScenario"/> with the ownership gate applied, and (b) ignores
/// a non-scenario <see cref="NodeOpType.SerializeLocal"/> payload so it coexists with <c>ReferenceArchiveHandler</c>.
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
/// </summary>
public sealed class HrotScenarioSaveHandlerTests : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(221)]
    private struct SaveMarker { public float X; }

    private readonly string _root;

    public HrotScenarioSaveHandlerTests()
    {
        ComponentTypeRegistry.Clear();
        _root = Path.Combine(Path.GetTempPath(), "hrot-scnsave-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        ComponentTypeRegistry.Clear();
    }

    private static EntityRepository BuildWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SaveMarker>();
        repo.RegisterComponent<NetworkAuthority>();
        return repo;
    }

    [Fact]
    public void PrepareAsync_WritesOwnedSlice_ThroughTheSharedFileService_InPlace()
    {
        var repo = BuildWorld();

        // Owned by this node (1) → saved.
        var owned = repo.CreateEntity();
        repo.SetComponent(owned, new SaveMarker { X = 1f });
        repo.AddComponent(owned, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

        // Owned by a FOREIGN node (2) → excluded by the gate.
        var foreign = repo.CreateEntity();
        repo.SetComponent(foreign, new SaveMarker { X = 2f });
        repo.AddComponent(foreign, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var handler    = new HrotScenarioSaveHandler(serializer, zoneService: null, tkbDb: null, repo, () => _root, nodeId: 1);

        var intent = new ExecuteNodeOpIntent
        {
            Operation     = NodeOpType.SerializeLocal,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new ScenarioSaveHandlerPayload("area/my_scenario"),
        };

        var result = handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

        // Written to its final shared location, so no manifest / no pull.
        Assert.Null(result);

        // File written under the resolved subfolder (a NAME, never a chosen filesystem path).
        var file = Path.Combine(_root, "area", "my_scenario", "scenario.json");
        Assert.True(File.Exists(file), $"expected scenario file at {file}");

        // The gate flowed through: the owned entity is present, the foreign one is not.
        var dom      = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        var entities = dom["Entities"]!.AsObject();
        Assert.Single(entities);
    }

    [Fact]
    public void PrepareAsync_IgnoresNonScenarioPayload_SoItCoexistsWithTheArchiveHandler()
    {
        var repo        = BuildWorld();
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var handler    = new HrotScenarioSaveHandler(serializer, zoneService: null, tkbDb: null, repo, () => _root, nodeId: 1);

        var intent = new ExecuteNodeOpIntent
        {
            Operation     = NodeOpType.SerializeLocal,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new ArchiveHandlerPayload(Guid.NewGuid()),   // the .fdp checkpoint payload
        };

        var result = handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Null(result);                               // not ours — no-op
        Assert.False(Directory.Exists(_root));             // wrote nothing
    }

    [Fact]
    public void CanHandle_OnlySerializeLocal()
    {
        var repo        = BuildWorld();
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        var handler    = new HrotScenarioSaveHandler(serializer, zoneService: null, tkbDb: null, repo, () => _root, nodeId: 1);

        Assert.True(handler.CanHandle(NodeOpType.SerializeLocal));
        Assert.False(handler.CanHandle(NodeOpType.PrepareState));
    }
}
