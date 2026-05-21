using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class Stage1_ParseTests
{
    private static CompileOptions DefaultOptions(IReadOnlyList<BlueprintSignature>? siblings = null) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());

    [Fact]
    public void Parse_ValidJson_ProducesAssetWithCorrectName()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var json = BlueprintJsonServices.Serialize(asset);
        var sink = new DiagnosticSink();

        var result = Stage1_Parse.Run(json, sink);

        Assert.NotNull(result);
        Assert.Equal("LibraryMath", result!.Name);
        Assert.False(sink.HasErrors);
    }

    [Fact]
    [CoversDiagnosticCode("BP0002")]
    public void Parse_MalformedJson_EmitsBP0002()
    {
        var sink = new DiagnosticSink();

        var result = Stage1_Parse.Run("{ bad json", sink);

        Assert.Null(result);
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP0002_JsonParseError);
    }

    [Fact]
    [CoversDiagnosticCode("BP0001")]
    public void Parse_NullToken_EmitsBP0001()
    {
        var sink = new DiagnosticSink();

        var result = Stage1_Parse.Run("null", sink);

        Assert.Null(result);
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP0001_NullAsset);
    }

    [Theory]
    [InlineData(TestData.SampleAssets.LibraryMath)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    [InlineData(TestData.SampleAssets.HealthRegen)]
    [InlineData(TestData.SampleAssets.DoorActor)]
    public void Parse_AllDispatchKinds_RoundTrip(string sampleName)
    {
        var original = TestData.LoadAsset(sampleName);
        var json1 = BlueprintJsonServices.Serialize(original);
        var sink = new DiagnosticSink();

        var reparsed = Stage1_Parse.Run(json1, sink);
        Assert.NotNull(reparsed);

        var json2 = BlueprintJsonServices.Serialize(reparsed!);
        Assert.Equal(json1, json2);
    }

    [Fact]
    [CoversDiagnosticCode("BP0010")]
    public void Parse_EmptyAssetId_EmitsBP0010()
    {
        var json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "00000000-0000-0000-0000-000000000000",
              "Name": "SomeName",
              "Dispatch": "Library",
              "Parameters": [], "WorkingState": [], "Variables": [],
              "EventDispatchers": [], "CustomEvents": [], "CallablePeers": [], "Graphs": []
            }
            """;
        var sink = new DiagnosticSink();

        Stage1_Parse.Run(json, sink);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP0010_EmptyAssetId);
    }

    [Fact]
    [CoversDiagnosticCode("BP0011")]
    public void Parse_EmptyName_EmitsBP0011()
    {
        var json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "11111111-0000-0000-0000-000000000001",
              "Name": "",
              "Dispatch": "Library",
              "Parameters": [], "WorkingState": [], "Variables": [],
              "EventDispatchers": [], "CustomEvents": [], "CallablePeers": [], "Graphs": []
            }
            """;
        var sink = new DiagnosticSink();

        Stage1_Parse.Run(json, sink);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP0011_EmptyName);
    }
}
