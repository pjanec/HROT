using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for <see cref="StorageProcessManager"/> (DEBT-01 from BATCH-01 review).
/// Verifies that the manager only initiates NAS pulls when valid payloads are received.
/// CE-278: the SC1 orchestrator-entry-prepend test (the SaveScenario=2 pull path via
/// GlobalContextManifestReadyEvent) is retired with the op.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class StorageProcessManagerTests
{
    /// <summary>
    /// SC2 -- Null payload: no NAS pull.
    /// When ResultPayload is null, no files should be created in NAS dir.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ProcessManager_NullPayload_NoNasPull()
    {
        string? tempNasDir = null;

        try
        {
            tempNasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempNasDir);

            var bus     = new FdpEventBus();
            var gateway = new StorageGatewayModule();
            var manager = new StorageProcessManager(bus, gateway, tempNasDir);

            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = Guid.NewGuid(),
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = null,
            });
            bus.SwapBuffers();
            manager.Tick();

            // Assert: NAS dir is empty.
            var files = Directory.GetFiles(tempNasDir);
            Assert.Empty(files);
        }
        finally
        {
            if (tempNasDir != null && Directory.Exists(tempNasDir))
                Directory.Delete(tempNasDir, recursive: true);
        }
    }

    /// <summary>
    /// ⭐ CE-4210 MA-4 — a macro's slices, merged: the node wrote its slice beside the macro (<c>macros/x/.slices</c>); the merge writes
    /// <c>macros/x/scenario.json</c> with the macro header kept, and cleans THAT <c>.slices</c> up. Nothing appears under <c>scenarios/</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4210_AMacrosSlices_AreMerged_BesideTheMacro_WithItsHeader_AndCleanedUp()
    {
        string nas = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            string slicesDir = Path.Combine(nas, "macros", "x", ".slices");
            Directory.CreateDirectory(slicesDir);
            var dom = new System.Text.Json.Nodes.JsonObject
            {
                ["Header"] = new System.Text.Json.Nodes.JsonObject { ["Kind"] = "macro", ["Anchor"] = new System.Text.Json.Nodes.JsonObject { ["Position"] = new System.Text.Json.Nodes.JsonArray(1, 2, 0), ["Heading"] = 45.0 } },
                ["Entities"] = new System.Text.Json.Nodes.JsonObject(),
            };
            Fdp.Core.Serialization.Migrations.JsonEnvelope.Write(dom, new Fdp.Core.Serialization.Migrations.DocumentMeta("Hrot.Scenario", 2));
            File.WriteAllText(Path.Combine(slicesDir, "node_1.json"), dom.ToJsonString());

            var manager = new StorageProcessManager(new FdpEventBus(), new StorageGatewayModule(), nas);
            manager.MergeScenarioSlices("macros/x", new List<FileManifestEntry>
            {
                new() { RelativeDest = Path.Combine("macros", "x", ".slices", "node_1.json"), DocType = "Hrot.Scenario" },
            });

            var merged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(nas, "macros", "x", "scenario.json")))!.AsObject();
            var header = Fdp.Toolkit.Scenario.ScenarioHeaderJson.Read(merged)!;
            Assert.Equal("macro", header.Kind);
            Assert.Equal(45f, header.Anchor!.Value.Heading.Degrees);
            Assert.False(Directory.Exists(slicesDir), "the staging slices beside the macro are cleaned up");
            Assert.False(Directory.Exists(Path.Combine(nas, "scenarios")), "nothing under scenarios/");
        }
        finally { if (Directory.Exists(nas)) Directory.Delete(nas, recursive: true); }
    }

    /// <summary>
    /// ⭐⭐ CE-4099 (AQ108 §4c A5/A6) — an AUTOSAVE's slices merge into <c>scenarios/x/.autosave/autosave.json</c> (never <c>scenario.json</c>) with
    /// <c>autosave.meta.json {at, savedHash}</c> — <c>savedHash</c> the hash of the scenario.json it was based on; its staging is cleaned up; and a REAL
    /// save that completes deletes the whole <c>.autosave/</c>. 🔴 Red-proof (compiles): <c>MergeAutosaveSlices</c> writing <c>scenario.json</c> ⇒ the saved
    /// file changes ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4099_AnAutosave_MergesIntoTheAutosaveFolder_WithItsMeta_AndASaveDeletesIt()
    {
        string nas = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            string scenarioDir = Path.Combine(nas, "scenarios", "x");
            string slicesDir = Path.Combine(scenarioDir, ".autosave", ".slices");
            Directory.CreateDirectory(slicesDir);
            const string saved = "{\"Header\":{},\"Entities\":{}}";
            File.WriteAllText(Path.Combine(scenarioDir, "scenario.json"), saved);
            var dom = new System.Text.Json.Nodes.JsonObject { ["Header"] = new System.Text.Json.Nodes.JsonObject(), ["Entities"] = new System.Text.Json.Nodes.JsonObject
            {
                ["aaaa0000-0000-4000-8000-000000000001"] = new System.Text.Json.Nodes.JsonObject { ["EntityInfo"] = new System.Text.Json.Nodes.JsonObject { ["Name"] = "edited" } },
            } };
            Fdp.Core.Serialization.Migrations.JsonEnvelope.Write(dom, new Fdp.Core.Serialization.Migrations.DocumentMeta("Hrot.Scenario", 2));
            File.WriteAllText(Path.Combine(slicesDir, "node_1.json"), dom.ToJsonString());

            var manager = new StorageProcessManager(new FdpEventBus(), new StorageGatewayModule(), nas)
            { UtcNow = () => new DateTime(2026, 11, 8, 14, 41, 0, DateTimeKind.Utc) };
            manager.MergeAutosaveSlices("x", new List<FileManifestEntry>
            {
                new() { RelativeDest = Path.Combine("scenarios", "x", ".autosave", ".slices", "node_1.json"), DocType = "Hrot.Scenario" },
            });

            Assert.Equal(saved, File.ReadAllText(Path.Combine(scenarioDir, "scenario.json")));            // the saved file is untouched
            Assert.Contains("edited", File.ReadAllText(Path.Combine(scenarioDir, ".autosave", "autosave.json")));
            var meta = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scenarioDir, ".autosave", "autosave.meta.json")))!;
            Assert.StartsWith("2026-11-08T14:41:00", meta["at"]!.GetValue<string>());
            Assert.Equal(Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(saved), meta["savedHash"]!.GetValue<string>());
            Assert.False(Directory.Exists(slicesDir), "the autosave's staging is cleaned up");

            manager.DeleteAutosave("x");                                                                    // A6 — what a completed save calls
            Assert.False(Directory.Exists(Path.Combine(scenarioDir, ".autosave")));
            Assert.True(File.Exists(Path.Combine(scenarioDir, "scenario.json")));
        }
        finally { if (Directory.Exists(nas)) Directory.Delete(nas, recursive: true); }
    }

    /// <summary>
    /// SC3 -- Empty manifest: no NAS pull.
    /// When ResultPayload is an empty list, no files should be created.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ProcessManager_EmptyManifest_NoNasPull()
    {
        string? tempNasDir = null;

        try
        {
            tempNasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempNasDir);

            var bus     = new FdpEventBus();
            var gateway = new StorageGatewayModule();
            var manager = new StorageProcessManager(bus, gateway, tempNasDir);

            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId     = Guid.NewGuid(),
                StatusCode    = OrchestrationStatusCode.Success,
                ResultPayload = new List<FileManifestEntry>(),
            });
            bus.SwapBuffers();
            manager.Tick();

            // Assert: NAS dir is empty.
            var files = Directory.GetFiles(tempNasDir);
            Assert.Empty(files);
        }
        finally
        {
            if (tempNasDir != null && Directory.Exists(tempNasDir))
                Directory.Delete(tempNasDir, recursive: true);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4360 (the SAVE half of "done when") — the merge of a scenario FILED in a sub-folder writes its <c>scenario.json</c> where it lives (<c>scenarios/demos/air/x</c>), cleans THAT
    /// <c>.slices</c>, and creates nothing at <c>scenarios/x</c>. 🔴 Mutation: the merge composing <c>scenarios/&lt;name&gt;</c> by hand ⇒ the filed file is untouched and a top-level copy appears.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4360_AFiledScenariosSlices_AreMerged_WhereItLives()
    {
        string nas = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            string filed = Path.Combine(nas, "scenarios", "demos", "air", "x");
            Directory.CreateDirectory(Path.Combine(filed, ".slices"));
            File.WriteAllText(Path.Combine(filed, "scenario.json"), "{\"Header\":{},\"Entities\":{}}");
            var dom = new System.Text.Json.Nodes.JsonObject { ["Header"] = new System.Text.Json.Nodes.JsonObject(), ["Entities"] = new System.Text.Json.Nodes.JsonObject() };
            Fdp.Core.Serialization.Migrations.JsonEnvelope.Write(dom, new Fdp.Core.Serialization.Migrations.DocumentMeta("Hrot.Scenario", 2));
            File.WriteAllText(Path.Combine(filed, ".slices", "node_1.json"), dom.ToJsonString());

            var manager = new StorageProcessManager(new FdpEventBus(), new StorageGatewayModule(), nas);
            manager.MergeScenarioSlices("x", new List<FileManifestEntry>   // the slice's RelativeDest as the node composes it: the real folder under the shared NAS
            {
                new() { RelativeDest = Path.Combine(ContentLayout.ScenarioRelativeFolder(nas, "x"), ".slices", "node_1.json"), DocType = "Hrot.Scenario" },
            });

            Assert.Equal(Path.Combine("scenarios", "demos", "air", "x"), ContentLayout.ScenarioRelativeFolder(nas, "x"));
            Assert.False(Directory.Exists(Path.Combine(filed, ".slices")), "the staging slices beside the filed scenario are cleaned up");
            Assert.False(Directory.Exists(Path.Combine(nas, "scenarios", "x")), "nothing at the top level");
            Assert.Contains("Entities", File.ReadAllText(Path.Combine(filed, "scenario.json")));
        }
        finally { if (Directory.Exists(nas)) Directory.Delete(nas, recursive: true); }
    }

    /// <summary>
    /// ⭐ O-4 part 2 (CE-4415, §18c) — the master's merge compiles the scenario's terrain blocks: after the slices of a scenario holding a <c>TerrainBlock</c> are merged, <c>&lt;scenarioDir&gt;/terrain-overlay/overlay.json</c>
    /// exists on the NAS with the block's key and houses; a scenario whose block names an unknown recipe still SAVES (scenario.json written) — the failure is logged, not thrown — and leaves no overlay.
    /// 🔴 Red-proof: drop the <c>CompileTerrainOverlay</c> call ⇒ no overlay folder.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void O4_TheMerge_CompilesTheTerrainOverlay_AndAFailedCompileDoesNotFailTheSave()
    {
        const string hut = "{\"footprint\":[[0,0],[10,0],[10,8],[0,8]],\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[10,0],\"openings\":[{\"kind\":\"door\",\"at\":4.4,\"width\":1.2,\"doorId\":\"front\"}]},{\"from\":[10,0],\"to\":[10,8]},{\"from\":[10,8],\"to\":[0,8]},{\"from\":[0,8],\"to\":[0,0]}]}]}";
        string nas = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            string terrain = Path.Combine(ContentLayout.TerrainAssets(nas), "base-t");
            Directory.CreateDirectory(terrain); Directory.CreateDirectory(ContentLayout.BuildingAssets(nas)); Directory.CreateDirectory(ContentLayout.TerrainRecipeAssets(nas));
            File.WriteAllText(Path.Combine(terrain, "terrain.json"), "{\"schemaVersion\":2,\"world\":\"w.geojson\"}");
            File.WriteAllText(Path.Combine(terrain, "w.geojson"), "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,300,300],\"groundZ\":0},\"features\":[]}");
            File.WriteAllText(Path.Combine(ContentLayout.BuildingAssets(nas), "hut.building.json"), hut);
            File.WriteAllText(Path.Combine(ContentLayout.TerrainRecipeAssets(nas), "village.recipe.json"), "{\"id\":\"village\",\"houses\":{\"templates\":[\"hut\"],\"count\":[4,4],\"radius\":40,\"minSpacing\":4}}");

            foreach (var (scenario, recipe) in new[] { ("x", "village"), ("y", "missing") })
            {
                string scenarioDir = Path.Combine(nas, "scenarios", scenario);
                string slicesDir = Path.Combine(scenarioDir, ".slices");
                Directory.CreateDirectory(slicesDir);
                var dom = System.Text.Json.Nodes.JsonNode.Parse("{\"Header\":{\"TerrainName\":\"base-t\"},\"Entities\":{\"b1000000-0000-4000-8000-000000000001\":{\"TerrainBlock\":{\"Recipe\":\"" + recipe
                    + "\",\"Seed\":3,\"HeadingDeg\":0,\"Fit\":15},\"SimTransform\":{\"Position\":[150,150,0]}}}}")!.AsObject();
                Fdp.Core.Serialization.Migrations.JsonEnvelope.Write(dom, new Fdp.Core.Serialization.Migrations.DocumentMeta("Hrot.Scenario", 2));
                File.WriteAllText(Path.Combine(slicesDir, "node_1.json"), dom.ToJsonString());

                var manager = new StorageProcessManager(new FdpEventBus(), new StorageGatewayModule(), nas);
                manager.MergeScenarioSlices(scenario, new List<FileManifestEntry>
                {
                    new() { RelativeDest = Path.Combine("scenarios", scenario, ".slices", "node_1.json"), DocType = "Hrot.Scenario" },
                });
                Assert.True(File.Exists(Path.Combine(scenarioDir, "scenario.json")), $"{scenario}: the scenario itself saved");
            }

            var header = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(nas, "scenarios", "x", "terrain-overlay", "overlay.json")))!;
            Assert.Equal("base-t", header["base"]!["name"]!.GetValue<string>());
            Assert.Equal(4, header["blocks"]![0]!["houses"]!.GetValue<int>());
            Assert.True(File.Exists(Path.Combine(nas, "scenarios", "x", "terrain-overlay", "overlay.world.geojson")));
            Assert.False(Directory.Exists(Path.Combine(nas, "scenarios", "y", "terrain-overlay")), "a failed compile leaves no overlay");
        }
        finally { if (Directory.Exists(nas)) Directory.Delete(nas, recursive: true); }
    }
}
