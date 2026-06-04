using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Persistence;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Json;

/// <summary>
/// PU-105 byte-stability and determinism tests.
///
/// Success conditions per design §6.4:
/// - Serialize → Deserialize → Serialize is byte-identical for every fixture.
/// - Serializing the same DTO twice produces byte-identical output (determinism).
/// </summary>
public sealed class ByteStabilityTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    // ── BTree fixtures ────────────────────────────────────────────────────────

    private static IEnumerable<BehaviorTreeAssetDto> GetAllBTreeDtoFixtures()
    {
        // Reflection-loaded: SampleScout
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        foreach (var asset in contributor.Enumerate().OfType<Hrot.BTree.Editor.Model.BehaviorTreeAsset>())
            yield return BehaviorTreeAssetMapper.ToDto(asset);

        // Hand-built minimal fixture
        yield return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("aabbccdd-1111-2222-3333-444444444444"),
            Name    = "MinimalTree",
            TargetNamespace = "Test",
            Canvas = new CanvasDto { PanX = 0, PanY = 0, Zoom = 1 },
        };

        // Hand-built with action node
        var richDto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("aabbccdd-3333-4444-5555-666666666666"),
            Name    = "RichTree",
            TargetNamespace = "Test",
        };
        richDto.Nodes.Add(new BTreeRootNodeDto
        {
            VisualId = new Guid("10000000-0000-0000-0000-000000000001"),
            EditorMetadata = new NodeEditorMetadataDto { X = 0, Y = 0 },
        });
        richDto.Nodes.Add(new BTreeActionNodeDto
        {
            VisualId = new Guid("30000000-0000-0000-0000-000000000001"),
            EditorMetadata = new NodeEditorMetadataDto { X = 200, Y = 100 },
            Action = new BTreeActionPayloadDto
            {
                MethodFqn     = "Test.TestAction",
                DelegateShape = BTreeDelegateShapeDto.FourParamFull,
            },
        });
        richDto.Blackboard.Variables.Add(new BlackboardVariableDto
        {
            Name    = "AmmoCount",
            Type    = new BlackboardTypeRefDto { TypeId = "System.Int32" },
            Comment = "bullets",
        });
        yield return richDto;
    }

    // ── HSM fixtures ──────────────────────────────────────────────────────────

    private static IEnumerable<HsmAssetDto> GetAllHsmDtoFixtures()
    {
        // Reflection-loaded: SampleGuard
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        foreach (var asset in contributor.Enumerate().OfType<Hrot.Hsm.Editor.Model.HsmAsset>())
            yield return HsmAssetMapper.ToDto(asset);

        // Hand-built minimal
        yield return new HsmAssetDto
        {
            AssetId = new Guid("aabbccdd-2222-3333-4444-555555555555"),
            Name    = "MinimalMachine",
            Canvas  = new HsmCanvasDto { PanX = 0, PanY = 0, Zoom = 1 },
        };

        // Hand-built with states + transition + waypoint
        var richDto = new HsmAssetDto
        {
            AssetId = new Guid("aabbccdd-4444-5555-6666-777777777777"),
            Name    = "RichMachine",
        };
        richDto.States.Add(new StateNodeDto
        {
            StableId  = new Guid("aa010000-0000-0000-0000-000000000001"),
            Name      = "Idle",
            IsInitial = true, X = 100, Y = 100,
        });
        richDto.States.Add(new StateNodeDto
        {
            StableId = new Guid("bb010000-0000-0000-0000-000000000001"),
            Name     = "Active",
            X        = 400, Y = 100,
        });
        richDto.Transitions.Add(new TransitionNodeDto
        {
            VisualId       = new Guid("cc010000-0000-0000-0000-000000000001"),
            SourceStableId = new Guid("aa010000-0000-0000-0000-000000000001"),
            TargetStableId = new Guid("bb010000-0000-0000-0000-000000000001"),
            EventName      = "Activate",
            Waypoints      = { new WaypointDto { X = 250, Y = 80 } },
        });
        richDto.Events.Add(new EventDefinitionDto { Name = "Activate" });
        yield return richDto;
    }

    // ── BTree byte-stability tests ────────────────────────────────────────────

    [Fact]
    public void BTree_Serialize_Deserialize_Serialize_IsByteIdentical()
    {
        foreach (var dto in GetAllBTreeDtoFixtures())
        {
            var json1 = BTreeJsonServices.Serialize(dto);
            var restored = BTreeJsonServices.Deserialize(json1);
            restored.Should().NotBeNull($"Deserialize must succeed for fixture '{dto.Name}'");
            var json2 = BTreeJsonServices.Serialize(restored!);

            json2.Should().Be(json1,
                because: $"Serialize→Deserialize→Serialize must be byte-identical for '{dto.Name}'");
        }
    }

    [Fact]
    public void BTree_Serialize_CalledTwice_IsByteIdentical()
    {
        foreach (var dto in GetAllBTreeDtoFixtures())
        {
            var json1 = BTreeJsonServices.Serialize(dto);
            var json2 = BTreeJsonServices.Serialize(dto);

            json2.Should().Be(json1,
                because: $"Two serializes of the same DTO must be byte-identical (determinism) for '{dto.Name}'");
        }
    }

    // ── HSM byte-stability tests ──────────────────────────────────────────────

    [Fact]
    public void Hsm_Serialize_Deserialize_Serialize_IsByteIdentical()
    {
        foreach (var dto in GetAllHsmDtoFixtures())
        {
            var json1    = HsmJsonServices.Serialize(dto);
            var restored = HsmJsonServices.Deserialize(json1);
            restored.Should().NotBeNull($"Deserialize must succeed for fixture '{dto.Name}'");
            var json2 = HsmJsonServices.Serialize(restored!);

            json2.Should().Be(json1,
                because: $"Serialize→Deserialize→Serialize must be byte-identical for '{dto.Name}'");
        }
    }

    [Fact]
    public void Hsm_Serialize_CalledTwice_IsByteIdentical()
    {
        foreach (var dto in GetAllHsmDtoFixtures())
        {
            var json1 = HsmJsonServices.Serialize(dto);
            var json2 = HsmJsonServices.Serialize(dto);

            json2.Should().Be(json1,
                because: $"Two serializes of the same DTO must be byte-identical (determinism) for '{dto.Name}'");
        }
    }

    // ── BTree full-cycle: model → DTO → JSON → DTO → model → DTO → JSON ──────

    [Fact]
    public void BTree_FullCycle_SampleScout_IsByteIdentical()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().OfType<Hrot.BTree.Editor.Model.BehaviorTreeAsset>()
            .First(a => a.Name == "SampleScout");

        var dto1  = BehaviorTreeAssetMapper.ToDto(asset);
        var json1 = BTreeJsonServices.Serialize(dto1);
        var dto2  = BTreeJsonServices.Deserialize(json1)!;
        var json2 = BTreeJsonServices.Serialize(dto2);

        json2.Should().Be(json1, "full cycle must be byte-identical for SampleScout");
    }

    // ── HSM full-cycle: model → DTO → JSON → DTO → model → DTO → JSON ────────

    [Fact]
    public void Hsm_FullCycle_SampleGuard_IsByteIdentical()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().OfType<Hrot.Hsm.Editor.Model.HsmAsset>()
            .First(a => a.Name == "SampleGuard");

        var dto1  = HsmAssetMapper.ToDto(asset);
        var json1 = HsmJsonServices.Serialize(dto1);
        var dto2  = HsmJsonServices.Deserialize(json1)!;
        var json2 = HsmJsonServices.Serialize(dto2);

        json2.Should().Be(json1, "full cycle must be byte-identical for SampleGuard");
    }
}
