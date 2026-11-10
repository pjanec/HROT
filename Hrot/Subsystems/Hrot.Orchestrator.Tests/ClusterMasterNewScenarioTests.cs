using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐ New scenario N2 (📄 docs/UX/DESIGN_New_Scenario_And_Recipe.md §2.2, R-4135) — a BLANK scenario on a terrain is a header-only file the MASTER
/// writes where its loads resolve names (<see cref="StorageProcessManager.WriteBlankScenario"/>), run BEFORE the frame's load transitions so the
/// create and the load of its name sent together are ordered by construction.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterNewScenarioTests
{
    private static (ClusterMaster Master, FdpEventBus Bus) IdleMaster()
    {
        var bus = new FdpEventBus();
        var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory());
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = (NodeId)42, LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks, SubsystemName = "SimHost",
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        return (master, bus);
    }

    private static ExecuteStorageOpIntent Blank(string name, string? terrain) => new()
    {
        RequestId = Guid.NewGuid(), Operation = StorageOpType.CreateBlankScenario, ScenarioName = name,
        HeaderJson = terrain == null ? null : new JsonObject { ["terrainName"] = terrain }.ToJsonString(),
    };

    private static ClusterOpCompletedEvent? Outcome(FdpEventBus bus, Guid id)
        => bus.ReadManaged<ClusterOpCompletedEvent>().Where(e => e.RequestId == id).Cast<ClusterOpCompletedEvent?>().FirstOrDefault();

    /// <summary>⭐ The create and the load of its name, published in ONE frame: the master writes the file before it starts the load.</summary>
    [Fact(Timeout = 10_000)]
    public void N2_ACreateAndTheLoadOfItsName_InOneFrame_WriteTheFileBeforeTheLoadStarts()
    {
        var (master, bus) = IdleMaster();
        bool? loadStartedWhenWritten = null;
        master.BlankScenarioWriter = (name, terrain) =>
        {
            loadStartedWhenWritten = master.HasInFlightTransaction || master.LastLoadRequestId != Guid.Empty;
            return null;
        };

        var create = Blank("Berlin/Checkpoint", "basic-desert");
        var load = ScenarioLoadSequence.EditLoadIntent("Berlin/Checkpoint");
        bus.PublishManaged(load);     // ⚠ the load FIRST on the bus: the order of publication must not matter
        bus.PublishManaged(create);
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

        Assert.Equal(false, loadStartedWhenWritten);
        Assert.Equal(load.TransactionId, master.LastLoadRequestId);   // the load did start, in the same tick
        Assert.Equal(OrchestrationStatusCode.Success, Outcome(bus, create.RequestId)!.Value.StatusCode);
        master.Dispose();
    }

    /// <summary>The refusals: no writer on this master, no terrain, no name, and the writer's own refusal — each answered with its reason.</summary>
    [Fact(Timeout = 10_000)]
    public void N2_ARefusedCreate_AnswersRejectedWithTheReason()
    {
        var (master, bus) = IdleMaster();
        void Expect(ExecuteStorageOpIntent i, string why)
        {
            bus.PublishManaged(i);
            bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
            var o = Outcome(bus, i.RequestId);
            Assert.Equal(OrchestrationStatusCode.Rejected, o!.Value.StatusCode);
            Assert.Contains(why, (string)o.Value.ResultPayload!);
        }

        Expect(Blank("A", "basic-desert"), "cannot create");
        master.BlankScenarioWriter = (_, _) => "a scenario named 'A' already exists";
        Expect(Blank("A", null), "a terrain is required");
        Expect(Blank("", "basic-desert"), "name is required");
        Expect(Blank("A", "basic-desert"), "already exists");

        // ⭐ the networked request path decodes the same op (wire value 27) and answers it the same way
        var id = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = id, OperationType = ClusterOpType.CreateBlankScenario,
            PayloadJson = Hrot.Network.Orchestration.SaveScenarioRequestJson.Encode("B", null, "{\"terrainName\":\"basic-desert\"}"),
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Contains("already exists", (string)Outcome(bus, id)!.Value.ResultPayload!);
        master.Dispose();
    }

    /// <summary>⭐ The writer: a header naming the terrain and no entities, at the folder a load of the name resolves; never over an existing
    /// scenario; an unknown terrain refused with the ones the master offers.</summary>
    [Fact]
    public void N2_TheBlankFile_NamesItsTerrain_HasNoEntities_AndIsNeverOverwritten()
    {
        var nas = Path.Combine(Path.GetTempPath(), "n2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var terrain = Path.Combine(ContentLayout.TerrainAssets(nas), "highway-rest");
            Directory.CreateDirectory(terrain);
            File.WriteAllText(Path.Combine(terrain, Fdp.Toolkit.Terrain.TerrainCatalog.DefinitionFileName), "{}");
            var storage = new StorageProcessManager(new FdpEventBus(), new StorageGatewayModule(), nas);

            Assert.Null(storage.WriteBlankScenario("Berlin/Checkpoint", "highway-rest"));
            var file = Path.Combine(ContentLayout.ResolveScenarioDirectory(nas, "Berlin/Checkpoint"), ContentLayout.ScenarioFileName);
            var doc = JsonNode.Parse(File.ReadAllText(file))!;
            Assert.Equal("highway-rest", doc["header"]!["terrainName"]!.GetValue<string>());
            Assert.Equal("Hrot.Scenario", doc["$meta"]!["docType"]!.GetValue<string>());
            Assert.Empty(doc["entities"]!.AsObject());   // CE-4363: the shape ScenarioSerializer loads (an array never loaded)

            File.AppendAllText(file, " ");
            var before = File.ReadAllText(file);
            Assert.Contains("already exists", storage.WriteBlankScenario("Berlin/Checkpoint", "highway-rest"));
            Assert.Equal(before, File.ReadAllText(file));

            var unknown = storage.WriteBlankScenario("Other", "atlantis");
            Assert.Contains("unknown terrain 'atlantis'", unknown);
            Assert.Contains("highway-rest", unknown);
            Assert.False(Directory.Exists(ContentLayout.ResolveScenarioDirectory(nas, "Other")));
            Assert.Contains("climb", storage.WriteBlankScenario("../escape", "highway-rest"));
        }
        finally { try { Directory.Delete(nas, recursive: true); } catch (IOException) { } }
    }
}
