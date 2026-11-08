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

    [ComponentId(222)]
    private struct LinkMarker { public Fdp.Toolkit.Replication.EntityLink Follow; }

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
            DomainPayload = new ArchiveHandlerPayload((Fdp.Core.ExerciseId)(Guid.NewGuid())),   // the .fdp checkpoint payload
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
    public void CE4210_MA6_ANewMacro_IsSavedWithKindAnchorAndItsParams_ThroughTheSameSavePath()
    {
        var repo = BuildWorld();
        ScenarioDescription.Stamp(repo, null, "macros/" + _scenarioName, "h", null, "");            // a seed's load
        ScenarioDescription.StampNewMacro(repo);                                                      // what New macro… does after the seed loads
        // the author declares a param in the Parameters window (it replaces the resident header)
        var held = repo.GetSingletonManaged<ScenarioDescriptionResident>();
        var withParam = ScenarioDescription.HeaderOf(repo)! with { Params = new[] { new ScenarioParamDecl("side", "ForceId") } };
        ScenarioDescription.Stamp(repo, held.Text, held.Name, held.Hash, held.Args, ScenarioDescription.HeaderJsonOf(withParam));
        string name = "macros/" + _scenarioName;
        var handler = new HrotScenarioSaveHandler(new ScenarioSerializerBuilder("Hrot.Scenario").Build(), world: repo, nodeId: NodeId);
        var intent = new ExecuteNodeOpIntent { Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid(), DomainPayload = new ScenarioSaveHandlerPayload(name) };
        string sliceDir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), name);
        try
        {
            handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();
            var header = ScenarioHeaderJson.Read(JsonNode.Parse(File.ReadAllText(Path.Combine(sliceDir, "scenario.json")))!.AsObject())!;
            Assert.Equal("macro", header.Kind);
            Assert.Equal(0f, header.Anchor!.Value.Heading.Degrees);
            Assert.Equal(default, header.Anchor.Value.Position);
            Assert.Equal("side", header.Params!.Single().Name);
        }
        finally { try { Directory.Delete(Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(NodeId), "macros"), true); } catch { } }
    }

    // ── ⭐ CE-4210 MA-9 — Save selection as macro: the payload's filter, header override and rebinding ──────────────────────────────

    private EntityRepository SelectionWorld(out Entity a, out Entity b, out Entity c, out Entity outside)
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SaveMarker>();
        repo.RegisterComponent<LinkMarker>();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<EntityGuid>();
        repo.RegisterComponent<Fdp.Toolkit.Scenario.RefKey>();
        Entity Make(int net, float x)
        {
            var e = repo.CreateEntity();
            repo.SetComponent(e, new SaveMarker { X = x });
            repo.AddComponent(e, new NetworkIdentity(net));
            repo.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            repo.AddComponent(e, new NetworkAuthority(primaryOwnerId: NodeId, localNodeId: NodeId));
            return e;
        }
        a = Make(101, 1f); b = Make(102, 2f); c = Make(103, 3f); outside = Make(104, 4f);
        repo.AddComponent(outside, new Fdp.Toolkit.Scenario.RefKey("Target"));
        repo.AddComponent(a, new LinkMarker { Follow = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(104)) });   // a -> outside (by id; no authored form)
        repo.AddComponent(b, new LinkMarker { Follow = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(101)) });   // b -> a (inside)
        return repo;
    }

    private JsonObject SaveSlice(EntityRepository repo, ScenarioSaveHandlerPayload payload)
    {
        var handler = new HrotScenarioSaveHandler(new ScenarioSerializerBuilder("Hrot.Scenario").Build(), world: repo, nodeId: NodeId);
        handler.PrepareAsync(new ExecuteNodeOpIntent { Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid(), DomainPayload = payload }, CancellationToken.None).GetAwaiter().GetResult();
        return JsonNode.Parse(File.ReadAllText(ExpectedSliceFile))!.AsObject();
    }

    [Fact]
    public void CE4210_MA9_OnlyGuids_WritesOnlyThoseEntities_AndNullOrEmptyWritesEverything()
    {
        using var repo = SelectionWorld(out var a, out var b, out var c, out var outside);
        Guid Of(Entity e) => repo.GetComponent<EntityGuid>(e).Value;

        var picked = SaveSlice(repo, new ScenarioSaveHandlerPayload(_scenarioName, new[] { Of(a), Of(c) }))["Entities"]!.AsObject();
        Assert.Equal(new[] { Of(a).ToString(), Of(c).ToString() }.OrderBy(x => x), picked.Select(kv => kv.Key).OrderBy(x => x));

        Assert.Equal(4, SaveSlice(repo, new ScenarioSaveHandlerPayload(_scenarioName))["Entities"]!.AsObject().Count);                          // null: as today
        Assert.Equal(4, SaveSlice(repo, new ScenarioSaveHandlerPayload(_scenarioName, Array.Empty<Guid>()))["Entities"]!.AsObject().Count);     // empty: as today
    }

    [Fact]
    public void CE4210_MA9_TheHeaderOverride_ReplacesTheResidentsMacroFields_AndNotTheTerrainOrNothing()
    {
        using var repo = SelectionWorld(out var a, out _, out _, out _);
        ScenarioDescription.Stamp(repo, "What: the source scenario.", "scn", "h", null, "");   // a plain scenario resident
        var over = ScenarioDescription.HeaderJsonOf(new ScenarioHeader("") { Kind = ScenarioHeader.MacroKind, Anchor = new MacroAnchor(new WorldPoint(5f, 6f, 7f), default) });

        var header = JsonNode.Parse(SaveSlice(repo, new ScenarioSaveHandlerPayload(_scenarioName, new[] { repo.GetComponent<EntityGuid>(a).Value }, over))["Header"]!.ToJsonString())!.AsObject();
        var read = ScenarioHeaderJson.Read(new JsonObject { ["Header"] = header })!;
        Assert.Equal("macro", read.Kind);
        Assert.Equal(5f, read.Anchor!.Value.Position.X);
        Assert.Null(read.Description);                                 // the source scenario's text is not the macro's

        var plain = SaveSlice(repo, new ScenarioSaveHandlerPayload(_scenarioName))["Header"]?.AsObject();   // no override: the resident, as today
        Assert.Equal("What: the source scenario.", (string?)plain?["Description"]);
        Assert.Null(plain?["Kind"]);
    }

    [Fact]
    public void CE4210_MA9_MakeParameters_RebindsTheOutsideReference_InTheSavedDomOnly()
    {
        using var repo = SelectionWorld(out var a, out var b, out _, out var outside);
        var header = ScenarioDescription.HeaderJsonOf(new ScenarioHeader("")
        {
            Kind = ScenarioHeader.MacroKind,
            Params = new[] { new ScenarioParamDecl("target", "EntityLink", JsonValue.Create("@Target")) },   // the outside entity's key is the default
        });
        Guid Of(Entity e) => repo.GetComponent<EntityGuid>(e).Value;
        var before = repo.GetComponent<LinkMarker>(a);

        var dom = SaveSlice(repo, new ScenarioSaveHandlerPayload(_scenarioName, new[] { Of(a), Of(b) }, header));

        var ents = dom["Entities"]!.AsObject();
        Assert.Equal(2, ents.Count);                                                    // only the two men, not the outside one
        Assert.Equal("^target", (string?)ents[Of(a).ToString()]!["LinkMarker"]!["Follow"]);   // outside: now the param
        Assert.Equal(101, (int?)ents[Of(b).ToString()]!["LinkMarker"]!["Follow"]);                  // inside (b -> a): untouched, still the id
        Assert.Equal("target", (string?)dom["Header"]!["Params"]![0]!["Name"]);
        Assert.Equal("@Target", (string?)dom["Header"]!["Params"]![0]!["Default"]);      // the default keeps the key

        Assert.Equal(before, repo.GetComponent<LinkMarker>(a));                          // the WORLD is unchanged: still unbound
        Assert.Equal(Fdp.Core.RefForm.None, repo.GetComponent<LinkMarker>(a).Follow.Spec.Form);
    }

    // ── ⭐ CE-4210 W-M6 — a reference inside a params BLOB is rebound like a typed leaf (SR-159), in the saved DOM only ───────────────────────────────────

    public sealed class AttackParams { public Fdp.Toolkit.Replication.EntityLink Target { get; set; } }

    private static string Blob(string g) => "{\"Target\":\"" + g + "\"}";

    private static JsonObject BlobDom(string hill, string other) => new()
    {
        ["Entities"] = new JsonObject
        {
            ["e1"] = new JsonObject
            {
                ["Behavior"] = new JsonObject { ["Name"] = "Attack", ["Params"] = Blob(hill), ["Origin"] = "Superior" },
                ["Sop"] = new JsonObject { ["Name"] = "Attack", ["Params"] = Blob(hill), ["Origin"] = "Sop" },
                ["MissionPlan"] = new JsonObject { ["PlanData"] = new JsonObject { ["tasks"] = new JsonArray(
                    new JsonObject { ["behaviorName"] = "Attack", ["behaviorParams"] = Blob(hill) },
                    new JsonObject { ["behaviorName"] = "Attack", ["behaviorParams"] = Blob(other) }) } },
                ["ParamBindings"] = new JsonObject { ["Bindings"] = new JsonArray(hill) },   // a binding, not a reference
            },
        },
    };

    [Fact]
    public void CE4210_W_M6_MakeParameters_RebindsAReferenceInsideBehaviorSopAndMissionBlobs_AndLeavesParamBindingsAlone()
    {
        const string hill = "5d0b0001-0000-4000-8000-000000000002", other = "5d0b0001-0000-4000-8000-000000000009";
        var remapper = new Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper();
        remapper.Register<AttackParams>("Attack");
        var header = new ScenarioHeader("") { Kind = ScenarioHeader.MacroKind, Params = new[] { new ScenarioParamDecl("target", "EntityLink", JsonValue.Create(hill)) } };
        var dom = BlobDom(hill, other);
        var serializer = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
        string bindingsBefore = dom["Entities"]!["e1"]!["ParamBindings"]!.ToJsonString();

        int n = Hrot.Map.Common.Scenario.ScenarioSaveCore.RebindOutsideReferences(dom, header, world: null, serializer, remapper);

        var e1 = dom["Entities"]!["e1"]!;
        Assert.Equal("^target", (string?)JsonNode.Parse((string)e1["Behavior"]!["Params"]!)!["Target"]);
        Assert.Equal("^target", (string?)JsonNode.Parse((string)e1["Sop"]!["Params"]!)!["Target"]);
        var tasks = e1["MissionPlan"]!["PlanData"]!["tasks"]!.AsArray();
        Assert.Equal("^target", (string?)JsonNode.Parse((string)tasks[0]!["behaviorParams"]!)!["Target"]);
        Assert.Equal(other, (string?)JsonNode.Parse((string)tasks[1]!["behaviorParams"]!)!["Target"]);   // a different target: not this param's
        Assert.Equal(bindingsBefore, e1["ParamBindings"]!.ToJsonString());
        Assert.Equal(3, n);   // Behavior, Sop and the mission plan

        // no remapper (a host with no behaviour contracts): the blobs are left as saved — the reason the handler must be handed one
        var plain = BlobDom(hill, other);
        Assert.Equal(0, Hrot.Map.Common.Scenario.ScenarioSaveCore.RebindOutsideReferences(plain, header, null, serializer));
        Assert.Equal(Blob(hill), (string?)plain["Entities"]!["e1"]!["Behavior"]!["Params"]);
    }

    [Fact]
    public void CE4210_W_M6_TheHandler_IsHandedTheNodesBehaviourRemapper_AndSavesThroughIt()
    {
        var remapper = new Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper();
        using var repo = SelectionWorld(out _, out _, out _, out _);
        var handler = new HrotScenarioSaveHandler(new ScenarioSerializerBuilder("Hrot.Scenario").Build(), world: repo, nodeId: NodeId, behaviorRemapper: remapper);
        var field = typeof(HrotScenarioSaveHandler).GetField("_behaviorRemapper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Same(remapper, field.GetValue(handler));   // asserted on the CONSTRUCTED object (silent-default rule)
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

    // ── ⭐⭐ CE-4099 AUTOSAVE (AQ108 §4c A2–A4, §4c.1) — the node's half ─────────────────────────────────────────────────────────────────────

    private (EntityRepository Repo, ScenarioSerializer Serializer) OwnedAndForeign()
    {
        var repo = BuildWorld();
        repo.RegisterComponent<EntityGuid>();   // stable keys: without them every save mints new ones and two saves can never be equal
        var owned = repo.CreateEntity();
        repo.SetComponent(owned, new SaveMarker { X = 1f });
        repo.AddComponent(owned, new NetworkAuthority(primaryOwnerId: NodeId, localNodeId: NodeId));
        repo.AddComponent(owned, new EntityGuid(Guid.Parse("aaaa0000-0000-4000-8000-000000000001")));
        var foreign = repo.CreateEntity();
        repo.SetComponent(foreign, new SaveMarker { X = 2f });
        repo.AddComponent(foreign, new NetworkAuthority(primaryOwnerId: 2, localNodeId: NodeId));
        repo.AddComponent(foreign, new EntityGuid(Guid.Parse("aaaa0000-0000-4000-8000-000000000002")));
        return (repo, new ScenarioSerializerBuilder("Hrot.Scenario").Build());
    }

    private ExecuteNodeOpIntent Op(bool autosave) => new()
    {
        Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid(),
        DomainPayload = new ScenarioSaveHandlerPayload(_scenarioName, Autosave: autosave),
    };

    /// <summary>
    /// ⭐⭐ CE-4099 A2/A4 — an AUTOSAVE writes this node's slice from a keyframe SANDBOX (on a worker) into <c>.autosave/</c>, pulled to the
    /// scenario's <c>.autosave/.slices</c> — and its content is the real save's, byte for byte (the gate flowed: the foreign entity is absent).
    /// 🔴 Red-proof (compiles): the handler ignoring <c>Autosave</c> ⇒ the slice lands in the real save's folder ⇒ red.
    /// </summary>
    [Fact]
    public void CE4099_AnAutosave_WritesTheSameSlice_IntoTheAutosaveFolder()
    {
        var (repo, serializer) = OwnedAndForeign();
        var handler = new HrotScenarioSaveHandler(serializer, world: repo, nodeId: NodeId);

        var saved = Assert.IsType<FileManifestResult[]>(handler.PrepareAsync(Op(autosave: false), CancellationToken.None).GetAwaiter().GetResult());
        string real = File.ReadAllText(saved[0].SourceUnc);
        var auto = Assert.IsType<FileManifestResult[]>(handler.PrepareAsync(Op(autosave: true), CancellationToken.None).GetAwaiter().GetResult());

        Assert.Single(auto);
        Assert.Contains(Path.Combine(".autosave", "scenario.json"), auto[0].SourceUnc);
        Assert.Equal(HrotScenarioSaveHandler.AutosaveSliceRelativePath(_scenarioName, (Fdp.Core.NodeId)NodeId), auto[0].RelativeDest);
        Assert.Contains(".autosave", auto[0].RelativeDest);
        Assert.Equal(real, File.ReadAllText(auto[0].SourceUnc));
    }

    /// <summary>⭐ CE-4099 A3 — an autosave does NOT re-baseline the node (its world must keep reading "changed" until a REAL save); a real save does.
    /// 🔴 Red-proof (compiles): the wrapper's autosave early return removed ⇒ a baseline after the autosave ⇒ red.</summary>
    [Fact]
    public void CE4099_AnAutosave_DoesNotRebaseline_ARealSaveDoes()
    {
        var (repo, serializer) = OwnedAndForeign();
        var check = new Fdp.Toolkit.Replay.WorldChangeCheck();
        var wrapped = new BaselineAfterSaveHandler(new HrotScenarioSaveHandler(serializer, world: repo, nodeId: NodeId), check, repo);

        wrapped.Commit(Op(autosave: true), repo);
        Assert.False(check.HasBaseline);
        wrapped.Commit(Op(autosave: false), repo);
        Assert.True(check.HasBaseline);
    }

    /// <summary>⭐ CE-4099 (the worker half) — a behaviour hot reload between the capture and the end of the extraction DISCARDS the result (the registry's
    /// dictionaries are not thread-safe). 🔴 Red-proof (compiles): the revision compare removed ⇒ no throw ⇒ red.</summary>
    [Fact]
    public void CE4099_AHotReloadDuringTheExtraction_DiscardsIt()
    {
        var (repo, serializer) = OwnedAndForeign();
        var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
        var capture = Hrot.Map.Common.Scenario.ScenarioAutosaveCore.Take(repo, "Hrot.Scenario", registry);
        Assert.NotEmpty(Hrot.Map.Common.Scenario.ScenarioAutosaveCore.ToJson(serializer, capture, registry));   // no change ⇒ written
        registry.RegisterValidator("hot", _ => Array.Empty<string>());                                        // a hot reload re-registers
        Assert.Throws<Hrot.Map.Common.Scenario.ScenarioAutosaveCore.RacedHotReloadException>(
            () => Hrot.Map.Common.Scenario.ScenarioAutosaveCore.ToJson(serializer, capture, registry));
    }
}
