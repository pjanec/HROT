using System;
using System.IO;
using System.Linq;
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
// ⭐⭐⭐ QA-008 (second assembly, 2026-09-18) — this class calls ComponentTypeRegistry.Clear(),
//    a PROCESS-GLOBAL mutation. Without this attribute it runs in parallel with every other
//    collection and deletes their component registrations mid-test — the DEBT-AIB-030 flake,
//    measured here as SIX distinct victims across four runs, all green under --filter.
//    📄 ComponentTypeRegistryMutatorCollection.cs carries the measurement.
[Collection(Hrot.Presentation.Tests.ComponentTypeRegistryMutatorCollection.Name)]
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
        var handler    = new HrotScenarioSaveHandler(serializer, world: repo, nodeId: NodeId);

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
        var handler    = new HrotScenarioSaveHandler(serializer, world: repo, nodeId: NodeId);

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
        var handler    = new HrotScenarioSaveHandler(serializer, world: repo, nodeId: NodeId);

        Assert.True(handler.CanHandle(NodeOpType.SerializeLocal));
        Assert.False(handler.CanHandle(NodeOpType.PrepareState));
    }

    // ── ⭐ CE-4210 MA-4 — the macro header survives the save, and the slice lands beside the canonical file ──────────────────────

    private static ScenarioHeader MacroHeader() => new("Hrot.Scenario")
    {
        Kind = ScenarioHeader.MacroKind,
        Params = new[] { new ScenarioParamDecl("side", "ForceId", JsonValue.Create("Hostile"), "which force") },
        Anchor = new MacroAnchor(new WorldPoint(10f, 20f, 0f), new Bearing(90f)),
        MaxShift = 12.5f,
        Relocatable = false,
    };

    [Fact]
    public void CE4210_AMacroOpenedInTheEditor_IsSavedWithItsHeader_AndItsSliceBesideTheMacro()
    {
        var repo = BuildWorld();
        ScenarioDescription.Stamp(repo, "What: a macro.", "macros/test/x", "0123456789abcdef", null, ScenarioDescription.HeaderJsonOf(MacroHeader()));   // what the load stamps
        string name = "macros/" + _scenarioName;
        var handler = new HrotScenarioSaveHandler(new ScenarioSerializerBuilder("Hrot.Scenario").Build(), world: repo, nodeId: NodeId);
        var intent = new ExecuteNodeOpIntent { Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid(), DomainPayload = new ScenarioSaveHandlerPayload(name) };
        string sliceDir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), name);
        try
        {
            var manifest = Assert.IsType<FileManifestResult[]>(handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult());

            var header = ScenarioHeaderJson.Read(JsonNode.Parse(File.ReadAllText(Path.Combine(sliceDir, "scenario.json")))!.AsObject())!;
            Assert.Equal("macro", header.Kind);
            Assert.Equal("side", header.Params!.Single().Name);
            Assert.Equal("ForceId", header.Params!.Single().TypeId);
            Assert.Equal(90f, header.Anchor!.Value.Heading.Degrees);
            Assert.Equal(12.5f, header.MaxShift);
            Assert.False(header.Relocatable);
            Assert.Equal("What: a macro.", header.Description);   // the description rides with it

            // the slice goes where the MERGE reads and cleans: beside macros/<name>/scenario.json, not under scenarios/macros/…
            Assert.Equal(Path.Combine("macros", _scenarioName, ".slices", $"node_{NodeId}.json"), manifest[0].RelativeDest);
            Assert.Equal(Path.GetRelativePath("/n", ContentLayout.ResolveScenarioDirectory("/n", name)) + Path.DirectorySeparatorChar + ".slices" + Path.DirectorySeparatorChar + $"node_{NodeId}.json", manifest[0].RelativeDest);
        }
        finally { try { Directory.Delete(Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), "macros"), true); } catch { } }
    }

    [Fact]
    public void CE4210_APlainScenario_IsSavedWithNoMacroFields_AndItsSliceStaysUnderScenarios()
    {
        var repo = BuildWorld();
        ScenarioDescription.Stamp(repo, null, _scenarioName, "h", null, ScenarioDescription.HeaderJsonOf(new ScenarioHeader("Hrot.Scenario")));   // a scenario: nothing to hold
        var handler = new HrotScenarioSaveHandler(new ScenarioSerializerBuilder("Hrot.Scenario").Build(), world: repo, nodeId: NodeId);
        var intent = new ExecuteNodeOpIntent { Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid(), DomainPayload = new ScenarioSaveHandlerPayload(_scenarioName) };

        var manifest = Assert.IsType<FileManifestResult[]>(handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult());

        var header = ScenarioHeaderJson.Read(JsonNode.Parse(File.ReadAllText(ExpectedSliceFile))!.AsObject());   // null: a plain scenario with no terrain or text has no header node at all
        Assert.True(header is null || !ScenarioHeaderJson.HasAny(header), "a plain scenario carries none of the macro fields");
        Assert.Equal(Path.Combine("scenarios", _scenarioName, ".slices", $"node_{NodeId}.json"), manifest[0].RelativeDest);
        Assert.False(ScenarioHeaderJson.HasAny(ScenarioSaveCore_HeaderOf(repo)));   // the ONE helper: nothing held ⇒ nothing written
    }

    private static ScenarioHeader ScenarioSaveCore_HeaderOf(EntityRepository world) => Hrot.Map.Common.Scenario.ScenarioSaveCore.HeaderOf(world, "Hrot.Scenario");
}
