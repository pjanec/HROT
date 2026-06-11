using System;
using System.Collections.Generic;
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
/// B-2 round-trip tests: IsAutoManaged on blackboard variables is persisted and
/// restored correctly for both BTree and HSM assets.
/// </summary>
public sealed class IsAutoManagedRoundTripTests
{
    // ── BTree helpers ─────────────────────────────────────────────────────────

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

    // ── HSM helpers ───────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────────
    // BTree tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsAutoManaged_True_RoundTrips_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("_auto_var", typeof(float), null, IsAutoManaged: true),
        });

        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.IsAutoManaged.Should().BeTrue("IsAutoManaged=true must survive BTree model→DTO→model");
    }

    [Fact]
    public void IsAutoManaged_False_RoundTrips_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("handVar", typeof(int), null, IsAutoManaged: false),
        });

        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.IsAutoManaged.Should().BeFalse("IsAutoManaged=false (default) must survive BTree round-trip");
    }

    [Fact]
    public void IsAutoManaged_DefaultFalse_BTree()
    {
        // Constructing without the optional param defaults to false.
        var entry = new BlackboardVariableEntry("x", typeof(bool), null);
        entry.IsAutoManaged.Should().BeFalse("default value must be false");
    }

    [Fact]
    public void IsAutoManaged_True_IsOmittedFromJson_WhenFalse_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("handVar", typeof(int), null, IsAutoManaged: false),
        });

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        var json = JsonSerializer.Serialize(dto);

        json.Should().NotContain("IsAutoManaged",
            "false value must be omitted from JSON for backwards compatibility");
    }

    [Fact]
    public void IsAutoManaged_True_IsPresentInJson_BTree()
    {
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("_auto_var", typeof(float), null, IsAutoManaged: true),
        });

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        var json = JsonSerializer.Serialize(dto);

        json.Should().Contain("\"IsAutoManaged\":true",
            "true value must appear in JSON");
    }

    [Fact]
    public void IsAutoManaged_Backcompat_MissingFromJson_DefaultsFalse_BTree()
    {
        // Simulate reading a JSON file that predates IsAutoManaged — the property is absent.
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

        var dto     = JsonSerializer.Deserialize<BehaviorTreeAssetDto>(legacyJson)!;
        var model   = BehaviorTreeAssetMapper.FromDto(dto);

        var v = model.BlackboardVariables.Should().ContainSingle().Subject;
        v.IsAutoManaged.Should().BeFalse("missing IsAutoManaged in JSON must default to false");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HSM tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsAutoManaged_True_RoundTrips_Hsm()
    {
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("_auto_var", typeof(float), null, IsAutoManaged: true),
        });

        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.IsAutoManaged.Should().BeTrue("IsAutoManaged=true must survive HSM model→DTO→model");
    }

    [Fact]
    public void IsAutoManaged_False_RoundTrips_Hsm()
    {
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("handVar", typeof(int), null, IsAutoManaged: false),
        });

        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.IsAutoManaged.Should().BeFalse();
    }

    [Fact]
    public void IsAutoManaged_True_IsOmittedFromJson_WhenFalse_Hsm()
    {
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("handVar", typeof(int), null, IsAutoManaged: false),
        });

        var dto  = HsmAssetMapper.ToDto(asset);
        var json = JsonSerializer.Serialize(dto);

        json.Should().NotContain("IsAutoManaged",
            "false value must be omitted from JSON for backwards compatibility");
    }

    [Fact]
    public void IsAutoManaged_Backcompat_MissingFromJson_DefaultsFalse_Hsm()
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
        v.IsAutoManaged.Should().BeFalse("missing IsAutoManaged in JSON must default to false");
    }
}
