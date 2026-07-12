using System;
using System.Text.Json;
using Fbt;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

// Use BTreeJsonServices / HsmJsonServices for all serialization checks in this file
// (they register JsonStringEnumConverter so enums round-trip as strings, not integers).
// Plain JsonSerializer.Serialize is NOT used for those checks.

namespace Hrot.AiEditor.Persistence.Tests.BTree;

/// <summary>
/// S3-1 round-trip tests: Role and Scope on blackboard variables are persisted and
/// restored correctly for both BTree and HSM assets.
/// Mirrors IsAutoManagedRoundTripTests.cs exactly (same structure, same BTree+HSM coverage).
/// </summary>
public sealed class RoleScopeRoundTripTests
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
    // BTree — BlackboardVariable_RoleScope_RoundTrips
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_BTree_StateAndBehavior()
    {
        // Author a State/Behavior variable, save+reload; attributes must be preserved.
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("stateVar", typeof(float), null,
                Role: BlackboardVariableRole.State,
                Scope: WorkingStateScope.Behavior),
        });

        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.Role.Should().Be(BlackboardVariableRole.State,
            "Role=State must survive BTree model→DTO→model round-trip");
        v.Scope.Should().Be(WorkingStateScope.Behavior,
            "Scope=Behavior must survive BTree model→DTO→model round-trip");
    }

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_BTree_Defaults_OmittedFromJson()
    {
        // A variable with default Role=Input, Scope=Node must omit both fields from JSON.
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("inputVar", typeof(int), null),
        });

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        // Use BTreeJsonServices so JsonStringEnumConverter is active (same path as production).
        var json = BTreeJsonServices.Serialize(dto);

        json.Should().NotContain("\"Role\"",
            "default Role=Input must be omitted from JSON for backwards compatibility");
        json.Should().NotContain("\"Scope\"",
            "default Scope=Node must be omitted from JSON for backwards compatibility");
    }

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_BTree_LegacyJson_DefaultsToInputNode()
    {
        // A legacy asset with neither Role nor Scope must deserialize as Input/Node.
        const string legacyJson = """
        {
            "AssetId": "00000000-0000-0000-0000-000000000010",
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
        v.Role.Should().Be(BlackboardVariableRole.Input,
            "missing Role in JSON must default to Input");
        v.Scope.Should().Be(WorkingStateScope.Node,
            "missing Scope in JSON must default to Node");
    }

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_BTree_StateEntity_InJson()
    {
        // A State/Entity variable must appear in JSON with the correct string values.
        var asset = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("entityStateVar", typeof(float), null,
                Role: BlackboardVariableRole.State,
                Scope: WorkingStateScope.Entity),
        });

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        // Use BTreeJsonServices so JsonStringEnumConverter is active (same path as production).
        var json = BTreeJsonServices.Serialize(dto);

        json.Should().Contain("\"Role\":\"State\"",
            "Role=State must appear in JSON as a string (JsonStringEnumConverter)");
        json.Should().Contain("\"Scope\":\"Entity\"",
            "Scope=Entity must appear in JSON as a string (JsonStringEnumConverter)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HSM — BlackboardVariable_RoleScope_RoundTrips
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_Hsm_StateAndBehavior()
    {
        // Author a State/Behavior variable, save+reload; attributes must be preserved.
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("stateVar", typeof(float), null,
                Role: BlackboardVariableRole.State,
                Scope: WorkingStateScope.Behavior),
        });

        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(asset));

        var v = restored.BlackboardVariables.Should().ContainSingle().Subject;
        v.Role.Should().Be(BlackboardVariableRole.State,
            "Role=State must survive HSM model→DTO→model round-trip");
        v.Scope.Should().Be(WorkingStateScope.Behavior,
            "Scope=Behavior must survive HSM model→DTO→model round-trip");
    }

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_Hsm_Defaults_OmittedFromJson()
    {
        // A variable with default Role=Input, Scope=Node must omit both fields from JSON.
        var asset = MakeHsmAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("inputVar", typeof(int), null),
        });

        var dto  = HsmAssetMapper.ToDto(asset);
        // Use HsmJsonServices so JsonStringEnumConverter is active (same path as production).
        var json = HsmJsonServices.Serialize(dto);

        json.Should().NotContain("\"Role\"",
            "default Role=Input must be omitted from JSON for backwards compatibility");
        json.Should().NotContain("\"Scope\"",
            "default Scope=Node must be omitted from JSON for backwards compatibility");
    }

    [Fact]
    public void BlackboardVariable_RoleScope_RoundTrips_Hsm_LegacyJson_DefaultsToInputNode()
    {
        // A legacy HSM asset with neither Role nor Scope must deserialize as Input/Node.
        const string legacyJson = """
        {
            "AssetId": "00000000-0000-0000-0000-000000000020",
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
        v.Role.Should().Be(BlackboardVariableRole.Input,
            "missing Role in JSON must default to Input");
        v.Scope.Should().Be(WorkingStateScope.Node,
            "missing Scope in JSON must default to Node");
    }
}
