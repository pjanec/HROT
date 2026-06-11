using System;
using System.Text.Json;
using Fbt;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.BTree;

/// <summary>
/// B-3 round-trip tests: DefaultValueJson on BlackboardVariableEntry is persisted and
/// restored correctly for both BTree and HSM assets (model→DTO→model + byte-stability).
/// </summary>
public sealed class DefaultValueJsonRoundTripTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeAsset MakeBTreeAsset()
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        return BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");
    }

    private static HsmAsset MakeHsmAsset()
    {
        var b = new HsmBuilder("T");
        b.State("Idle").Initial().Final();
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flat);
        var meta = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "T", "", false, "");
    }

    // ── BTree: DefaultValueJson round-trip ────────────────────────────────────

    [Fact]
    public void DefaultValueJson_NonNull_RoundTrips_BTree()
    {
        const string json = "{\"Value\":42}";
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(int), null, DefaultValueJson: json),
        });

        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.DefaultValueJson.Should().Be(json,
            "DefaultValueJson must survive BTree model→DTO→model round-trip");
    }

    [Fact]
    public void DefaultValueJson_Null_RoundTrips_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(float), null, DefaultValueJson: null),
        });

        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.DefaultValueJson.Should().BeNull("null DefaultValueJson must survive round-trip");
    }

    [Fact]
    public void DefaultValueJson_Null_IsOmittedFromJson_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(int), null, DefaultValueJson: null),
        });

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        var json = JsonSerializer.Serialize(dto);

        json.Should().NotContain("DefaultValueJson",
            "null DefaultValueJson must be omitted from JSON for byte-stability/backwards-compat");
    }

    [Fact]
    public void DefaultValueJson_NonNull_IsPresentInJson_BTree()
    {
        const string dvJson = "{\"Value\":7}";
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(int), null, DefaultValueJson: dvJson),
        });

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        var json = JsonSerializer.Serialize(dto);

        json.Should().Contain("DefaultValueJson",
            "non-null DefaultValueJson must appear in JSON");
    }

    [Fact]
    public void DefaultValueJson_Default_IsNull_BTree()
    {
        var entry = new BlackboardVariableEntry("x", typeof(bool), null);
        entry.DefaultValueJson.Should().BeNull("default value must be null");
    }

    [Fact]
    public void DefaultValueJson_Backcompat_MissingFromJson_DefaultsNull_BTree()
    {
        const string legacyJson = """
        {
            "AssetId": "00000000-0000-0000-0000-000000000001",
            "Name": "Legacy",
            "TargetNamespace": "Ns",
            "BlackboardTypeName": "BB",
            "ContextTypeName": "Ctx",
            "Blackboard": {
                "Managed": true,
                "TypeName": "BB",
                "Variables": [
                    {
                        "Name": "legacyVar",
                        "Type": { "TypeId": "System.Int32", "IsArray": false }
                    }
                ]
            }
        }
        """;

        var dto   = JsonSerializer.Deserialize<BehaviorTreeAssetDto>(legacyJson)!;
        var model = BehaviorTreeAssetMapper.FromDto(dto);

        var v = model.BlackboardVariables.Should().ContainSingle().Subject;
        v.DefaultValueJson.Should().BeNull(
            "missing DefaultValueJson in legacy JSON must default to null");
    }

    // ── BTree: UpdateVariableDefaultValueJson ─────────────────────────────────

    [Fact]
    public void UpdateVariableDefaultValueJson_SetsValue_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(float), null),
        });

        asset.UpdateVariableDefaultValueJson("myVar", "{\"Value\":3.14}");

        asset.BlackboardVariables[0].DefaultValueJson.Should().Be("{\"Value\":3.14}");
    }

    [Fact]
    public void UpdateVariableDefaultValueJson_ClearsValue_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(int), null, DefaultValueJson: "{\"Value\":1}"),
        });

        asset.UpdateVariableDefaultValueJson("myVar", null);

        asset.BlackboardVariables[0].DefaultValueJson.Should().BeNull();
    }

    [Fact]
    public void UpdateVariableDefaultValueJson_Noop_WhenVarNotFound_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("real", typeof(int), null),
        });

        var act = () => asset.UpdateVariableDefaultValueJson("ghost", "{\"Value\":0}");

        act.Should().NotThrow("no-op for missing variable, no throw");
        asset.BlackboardVariables[0].DefaultValueJson.Should().BeNull("untouched variable keeps null");
    }

    // ── HSM: DefaultValueJson round-trip ─────────────────────────────────────

    [Fact]
    public void DefaultValueJson_NonNull_RoundTrips_Hsm()
    {
        const string dvJson = "{\"Health\":100}";
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("health", typeof(int), null, DefaultValueJson: dvJson),
        });

        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.DefaultValueJson.Should().Be(dvJson,
            "DefaultValueJson must survive HSM model→DTO→model round-trip");
    }

    [Fact]
    public void DefaultValueJson_Null_IsOmittedFromJson_Hsm()
    {
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("myVar", typeof(int), null, DefaultValueJson: null),
        });

        var dto  = HsmAssetMapper.ToDto(asset);
        var json = JsonSerializer.Serialize(dto);

        json.Should().NotContain("DefaultValueJson",
            "null DefaultValueJson must be omitted from JSON for byte-stability");
    }

    [Fact]
    public void DefaultValueJson_Backcompat_MissingFromJson_DefaultsNull_Hsm()
    {
        const string legacyJson = """
        {
            "AssetId": "00000000-0000-0000-0000-000000000002",
            "Name": "LegacyHsm",
            "TargetNamespace": "Ns",
            "BlackboardTypeName": "BB",
            "Blackboard": {
                "Managed": true,
                "TypeName": "BB",
                "Variables": [
                    {
                        "Name": "legacyVar",
                        "Type": { "TypeId": "System.Int32", "IsArray": false }
                    }
                ]
            }
        }
        """;

        var dto   = JsonSerializer.Deserialize<HsmAssetDto>(legacyJson)!;
        var model = HsmAssetMapper.FromDto(dto);

        var v = model.BlackboardVariables.Should().ContainSingle().Subject;
        v.DefaultValueJson.Should().BeNull(
            "missing DefaultValueJson in legacy HSM JSON must default to null");
    }

    [Fact]
    public void UpdateVariableDefaultValueJson_SetsValue_Hsm()
    {
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("speed", typeof(float), null),
        });

        asset.UpdateVariableDefaultValueJson("speed", "{\"Value\":5.0}");

        asset.BlackboardVariables[0].DefaultValueJson.Should().Be("{\"Value\":5.0}");
    }

    // ── Multiple vars: untouched keeps null ──────────────────────────────────

    [Fact]
    public void DefaultValueJson_OnlyAffectedVarChanged_OthersUnchanged_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("a", typeof(int), null),
            new BlackboardVariableEntry("b", typeof(float), null),
        });

        asset.UpdateVariableDefaultValueJson("a", "{\"Value\":42}");

        asset.BlackboardVariables.Should().HaveCount(2);
        asset.BlackboardVariables[0].DefaultValueJson.Should().Be("{\"Value\":42}");
        asset.BlackboardVariables[1].DefaultValueJson.Should().BeNull("untouched variable must stay null");
    }
}
