using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Json;

/// <summary>
/// PU-104 tests for HsmJsonServices (mirrors BTreeJsonServicesTests structure).
/// </summary>
public sealed class HsmJsonServicesTests
{
    private static HsmAssetDto MakeMinimalDto(string name = "TestMachine") => new()
    {
        AssetId            = new Guid("aabbccdd-2222-3333-4444-555555555555"),
        Name               = name,
        TargetNamespace    = "Hrot.AI.Behaviors.Machines",
        BlackboardTypeName = "TestMachine_Blackboard",
        Canvas = new HsmCanvasDto { PanX = 3f, PanY = 7f, Zoom = 0.9f },
    };

    private static HsmAssetDto MakeRichDto()
    {
        var dto = MakeMinimalDto("RichMachine");

        var idle = new StateNodeDto
        {
            StableId  = new Guid("aa010000-0000-0000-0000-000000000001"),
            Name      = "Idle",
            IsInitial = true,
            X         = 100f, Y = 100f,
            Comment   = "guard is at rest",
        };
        var scanning = new StateNodeDto
        {
            StableId = new Guid("bb010000-0000-0000-0000-000000000001"),
            Name     = "Scanning",
            X        = 400f, Y = 100f,
        };
        dto.States.Add(idle);
        dto.States.Add(scanning);

        dto.Transitions.Add(new TransitionNodeDto
        {
            VisualId       = new Guid("cc010000-0000-0000-0000-000000000001"),
            SourceStableId = idle.StableId,
            TargetStableId = scanning.StableId,
            EventName      = "Alert",
            Waypoints      = { new WaypointDto { X = 250f, Y = 80f } },
            Comment        = "threat detected",
        });
        dto.Events.Add(new EventDefinitionDto { Name = "Alert", PayloadSize = 0 });
        dto.Events.Add(new EventDefinitionDto { Name = "Clear",  PayloadSize = 0 });

        dto.Blackboard.Managed  = false;
        dto.Blackboard.TypeName = "TestMachine_Blackboard";
        dto.Blackboard.Variables.Add(new HsmBlackboardVariableDto
        {
            Name = "Health",
            Type = new HsmBlackboardTypeRefDto { TypeId = "System.Single", IsArray = false },
            Comment = "current HP",
        });

        dto.Suppressions.Conflict.Add(new HsmConflictSuppressionDto
            { VariableName = "HP", WriterPairKey = "x.vs.y" });

        return dto;
    }

    // ── $meta envelope ────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_MetaIsFirstProperty()
    {
        var json = HsmJsonServices.Serialize(MakeMinimalDto());
        json.Should().StartWith("{\"$meta\":",
            because: "$meta must be the first property per design §5.1");
    }

    [Fact]
    public void Serialize_MetaDocTypeIsHrotHsm()
    {
        var json = HsmJsonServices.Serialize(MakeMinimalDto());
        json.Should().Contain("\"Hrot.Hsm\"",
            because: "docType must be 'Hrot.Hsm' per design §5.1");
    }

    [Fact]
    public void Serialize_MetaSchemaVersionIsOne()
    {
        var json = HsmJsonServices.Serialize(MakeMinimalDto());
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("$meta")
            .GetProperty("schemaVersion").GetInt32()
            .Should().Be(1);
    }

    // ── Round-trip structural equality ────────────────────────────────────────

    [Fact]
    public void Deserialize_Serialize_RoundTrip_MinimalDto()
    {
        var dto      = MakeMinimalDto();
        var json     = HsmJsonServices.Serialize(dto);
        var restored = HsmJsonServices.Deserialize(json);

        restored.Should().NotBeNull();
        restored!.AssetId.Should().Be(dto.AssetId);
        restored.Name.Should().Be(dto.Name);
        restored.Canvas.Zoom.Should().BeApproximately(dto.Canvas.Zoom, 0.001f);
    }

    [Fact]
    public void Deserialize_Serialize_RoundTrip_RichDto()
    {
        var dto      = MakeRichDto();
        var json     = HsmJsonServices.Serialize(dto);
        var restored = HsmJsonServices.Deserialize(json);

        restored.Should().NotBeNull();
        restored!.States.Should().HaveCount(2);
        restored.Transitions.Should().HaveCount(1);
        restored.Events.Should().HaveCount(2);
        restored.Blackboard.Variables.Should().HaveCount(1);
        restored.Suppressions.Conflict.Should().HaveCount(1);

        // Waypoint must survive
        restored.Transitions[0].Waypoints.Should().HaveCount(1);
        restored.Transitions[0].Waypoints[0].X.Should().BeApproximately(250f, 0.001f);
    }

    [Fact]
    public void Deserialize_ToleratesUnknownProperties()
    {
        var json = """{"unknownX":42,"AssetId":"aabbccdd-2222-3333-4444-555555555555","Name":"LegacyMachine","States":[]}""";
        var dto  = HsmJsonServices.Deserialize(json);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("LegacyMachine");
    }

    [Fact]
    public void Deserialize_ToleratesMissingMeta()
    {
        var json = """{"AssetId":"aabbccdd-2222-3333-4444-555555555555","Name":"OldMachine","States":[]}""";
        var dto  = HsmJsonServices.Deserialize(json);

        dto.Should().NotBeNull("missing $meta must be tolerated");
        dto!.Name.Should().Be("OldMachine");
    }

    // ── Header-lazy discovery ─────────────────────────────────────────────────

    [Fact]
    public void ReadHeader_ReturnsAssetIdAndName()
    {
        var dto    = MakeMinimalDto("MyMachine");
        var json   = HsmJsonServices.Serialize(dto);
        var header = HsmJsonServices.ReadHeader(json);

        header.Should().NotBeNull();
        header!.Value.AssetId.Should().Be(dto.AssetId);
        header.Value.Name.Should().Be("MyMachine");
    }

    [Fact]
    public void ReadHeader_ReturnsNull_ForMalformedJson()
    {
        HsmJsonServices.ReadHeader("{{{{not json").Should().BeNull();
    }

    [Fact]
    public void DiscoverHeaders_SkipsMalformedFile_SiblingStillFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hsm_discover_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var validDto  = MakeMinimalDto("ValidMachine");
            var validJson = HsmJsonServices.Serialize(validDto);
            File.WriteAllText(Path.Combine(dir, "valid.hsm.json"), validJson);
            File.WriteAllText(Path.Combine(dir, "broken.hsm.json"), "NOT JSON ****");

            var discovered = HsmJsonServices.DiscoverHeaders(dir).ToList();

            discovered.Should().HaveCount(1, "malformed file must be silently skipped");
            discovered[0].Name.Should().Be("ValidMachine");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DiscoverHeaders_EnumeratesOnlyHsmJsonFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hsm_discover_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dto  = MakeMinimalDto("MyMachine");
            var json = HsmJsonServices.Serialize(dto);
            File.WriteAllText(Path.Combine(dir, "machine.hsm.json"), json);
            File.WriteAllText(Path.Combine(dir, "ignored.btree.json"), json);
            File.WriteAllText(Path.Combine(dir, "ignored.txt"), "text");

            var discovered = HsmJsonServices.DiscoverHeaders(dir).ToList();

            discovered.Should().HaveCount(1, "only *.hsm.json must be discovered");
            discovered[0].Name.Should().Be("MyMachine");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
