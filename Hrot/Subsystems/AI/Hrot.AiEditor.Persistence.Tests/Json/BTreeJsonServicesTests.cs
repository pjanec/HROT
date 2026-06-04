using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Json;

/// <summary>
/// PU-104 tests for BTreeJsonServices:
/// - Deserialize(Serialize(dto)) == dto (structural)
/// - $meta is FIRST property; docType/schemaVersion correct
/// - Tolerates unknown properties and missing $meta
/// - Header-lazy discovery: skips malformed files, returns correct headers
/// </summary>
public sealed class BTreeJsonServicesTests
{
    private static BehaviorTreeAssetDto MakeMinimalDto(string name = "TestTree") => new()
    {
        AssetId         = new Guid("aabbccdd-1111-2222-3333-444444444444"),
        Name            = name,
        TargetNamespace = "Hrot.AI.Behaviors.Trees",
        BlackboardTypeName = "BrainBlackboard",
        ContextTypeName = "BTreeContext",
        Canvas = new CanvasDto { PanX = 5f, PanY = 10f, Zoom = 1.25f },
    };

    private static BehaviorTreeAssetDto MakeRichDto()
    {
        var dto = MakeMinimalDto("RichTree");
        dto.Nodes.Add(new BTreeRootNodeDto
        {
            VisualId     = new Guid("10000000-0000-0000-0000-000000000001"),
            DisplayLabel = "Root",
            EditorMetadata = new NodeEditorMetadataDto { X = 0, Y = 0 },
        });
        dto.Nodes.Add(new BTreeActionNodeDto
        {
            VisualId     = new Guid("30000000-0000-0000-0000-000000000001"),
            DisplayLabel = "DoSomething",
            EditorMetadata = new NodeEditorMetadataDto { X = 200, Y = 100, Comment = "fire action" },
            Action = new BTreeActionPayloadDto
            {
                MethodFqn     = "Hrot.AI.Brains.TestAction",
                DelegateShape = BTreeDelegateShapeDto.FourParamFull,
            },
        });
        dto.Pills.Add(new BTreePillDto
        {
            VisualId         = new Guid("60000000-0000-0000-0000-000000000001"),
            HostNodeVisualId = new Guid("30000000-0000-0000-0000-000000000001"),
            DecoratorType    = "Inverter",
            StackIndex       = 0,
        });
        dto.SubtreeSyncBindings["50000000-0000-0000-0000-000000000001"] = new List<SubtreeSyncBindingDto>
        {
            new() { FieldName = "AmmoCount", MasterVariableName = "SharedAmmo", SyncIn = true },
        };
        dto.Suppressions.Conflict.Add(new ConflictSuppressionDto
            { VariableName = "HealthPoints", WriterPairKey = "a.vs.b" });
        dto.Suppressions.Unused.Add("OldField");
        dto.Blackboard.Managed  = false;
        dto.Blackboard.TypeName = "BrainBlackboard";
        dto.Blackboard.Variables.Add(new BlackboardVariableDto
        {
            Name = "AmmoCount",
            Type = new BlackboardTypeRefDto { TypeId = "System.Int32", IsArray = false },
            Comment = "ammo remaining",
        });
        return dto;
    }

    // ── $meta envelope ────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_MetaIsFirstProperty()
    {
        var json = BTreeJsonServices.Serialize(MakeMinimalDto());

        // The JSON must start with {"$meta": ...
        json.Should().StartWith("{\"$meta\":",
            because: "$meta must be the first property per design §5.1");
    }

    [Fact]
    public void Serialize_MetaDocTypeIsHrotBTree()
    {
        var json = BTreeJsonServices.Serialize(MakeMinimalDto());

        json.Should().Contain("\"Hrot.BTree\"",
            because: "docType must be 'Hrot.BTree' per design §5.1");
    }

    [Fact]
    public void Serialize_MetaSchemaVersionIsOne()
    {
        var json = BTreeJsonServices.Serialize(MakeMinimalDto());

        // Parse and check $meta.schemaVersion == 1
        using var doc = JsonDocument.Parse(json);
        var meta = doc.RootElement.GetProperty("$meta");
        meta.GetProperty("schemaVersion").GetInt32().Should().Be(1,
            because: "schemaVersion must be 1 per design §5.1");
    }

    // ── Round-trip structural equality ────────────────────────────────────────

    [Fact]
    public void Deserialize_Serialize_RoundTrip_MinimalDto()
    {
        var dto     = MakeMinimalDto();
        var json    = BTreeJsonServices.Serialize(dto);
        var restored = BTreeJsonServices.Deserialize(json);

        restored.Should().NotBeNull();
        restored!.AssetId.Should().Be(dto.AssetId);
        restored.Name.Should().Be(dto.Name);
        restored.TargetNamespace.Should().Be(dto.TargetNamespace);
        restored.Canvas.PanX.Should().BeApproximately(dto.Canvas.PanX, 0.001f);
        restored.Canvas.Zoom.Should().BeApproximately(dto.Canvas.Zoom, 0.001f);
    }

    [Fact]
    public void Deserialize_Serialize_RoundTrip_RichDto()
    {
        var dto      = MakeRichDto();
        var json     = BTreeJsonServices.Serialize(dto);
        var restored = BTreeJsonServices.Deserialize(json);

        restored.Should().NotBeNull();
        restored!.Nodes.Should().HaveCount(2, "two nodes in rich fixture");
        restored.Pills.Should().HaveCount(1);
        restored.Suppressions.Conflict.Should().HaveCount(1);
        restored.Suppressions.Unused.Should().HaveCount(1);
        restored.Blackboard.Variables.Should().HaveCount(1);
        restored.SubtreeSyncBindings.Should().HaveCount(1);
    }

    [Fact]
    public void Deserialize_ToleratesUnknownProperties()
    {
        var json = """{"unknownField":"surprise","AssetId":"aabbccdd-1111-2222-3333-444444444444","Name":"Test","Nodes":[]}""";

        var dto = BTreeJsonServices.Deserialize(json);

        dto.Should().NotBeNull("unknown properties must be silently ignored");
        dto!.Name.Should().Be("Test");
    }

    [Fact]
    public void Deserialize_ToleratesMissingMeta()
    {
        // Legacy format (no $meta)
        var json = """{"AssetId":"aabbccdd-1111-2222-3333-444444444444","Name":"LegacyTree","Nodes":[]}""";

        var dto = BTreeJsonServices.Deserialize(json);

        dto.Should().NotBeNull("missing $meta must be tolerated (legacy-safe)");
        dto!.Name.Should().Be("LegacyTree");
    }

    [Fact]
    public void Deserialize_PolymorphicNodes_ActionNodePreservedByKind()
    {
        var dto = MakeRichDto();
        var json = BTreeJsonServices.Serialize(dto);

        // Action node must be deserialized as BTreeActionNodeDto (not base)
        var restored = BTreeJsonServices.Deserialize(json);
        var actionNode = restored!.Nodes.OfType<BTreeActionNodeDto>().FirstOrDefault();
        actionNode.Should().NotBeNull("action node must be restored as BTreeActionNodeDto via 'kind'");
        actionNode!.Action!.MethodFqn.Should().Be("Hrot.AI.Brains.TestAction");
    }

    // ── Header-lazy discovery ─────────────────────────────────────────────────

    [Fact]
    public void ReadHeader_ReturnsAssetIdAndName_FromValidJson()
    {
        var dto  = MakeMinimalDto("MyTree");
        var json = BTreeJsonServices.Serialize(dto);

        var header = BTreeJsonServices.ReadHeader(json);

        header.Should().NotBeNull();
        header!.Value.AssetId.Should().Be(dto.AssetId);
        header.Value.Name.Should().Be("MyTree");
    }

    [Fact]
    public void ReadHeader_ReturnsNull_ForMalformedJson()
    {
        var header = BTreeJsonServices.ReadHeader("{ this is NOT valid json {{{{");
        header.Should().BeNull("malformed JSON must yield null, never throw");
    }

    [Fact]
    public void ReadHeader_ReturnsNull_ForEmptyString()
    {
        var header = BTreeJsonServices.ReadHeader(string.Empty);
        header.Should().BeNull();
    }

    [Fact]
    public void DiscoverHeaders_SkipsMalformedFile_SiblingStillFound()
    {
        // Arrange: temp directory with one valid + one malformed *.btree.json
        var dir = Path.Combine(Path.GetTempPath(), $"btree_discover_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var validDto  = MakeMinimalDto("ValidTree");
            var validJson = BTreeJsonServices.Serialize(validDto);
            File.WriteAllText(Path.Combine(dir, "valid.btree.json"), validJson);

            // Corrupt file — must be silently skipped
            File.WriteAllText(Path.Combine(dir, "broken.btree.json"), "NOT JSON AT ALL {{{{");

            // Act
            var discovered = BTreeJsonServices.DiscoverHeaders(dir).ToList();

            // Assert: one valid result, broken file silently skipped
            discovered.Should().HaveCount(1, "malformed file must be skipped");
            discovered[0].Name.Should().Be("ValidTree");
            discovered[0].AssetId.Should().Be(validDto.AssetId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DiscoverHeaders_EnumeratesOnlyBtreeJsonFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"btree_discover_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dto  = MakeMinimalDto("MyTree");
            var json = BTreeJsonServices.Serialize(dto);
            File.WriteAllText(Path.Combine(dir, "tree.btree.json"), json);
            File.WriteAllText(Path.Combine(dir, "ignored.hsm.json"),
                BTreeJsonServices.Serialize(MakeMinimalDto("HsmFile")));
            File.WriteAllText(Path.Combine(dir, "ignored.txt"), "text");

            var discovered = BTreeJsonServices.DiscoverHeaders(dir).ToList();

            discovered.Should().HaveCount(1, "only *.btree.json must be discovered");
            discovered[0].Name.Should().Be("MyTree");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
