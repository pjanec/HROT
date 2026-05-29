using System;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Common.Scenario;
using Xunit;

namespace Hrot.Blueprints.Compiler.Tests;

/// <summary>
/// Tests for JM-P2-004: BlueprintJsonServices envelope rollout.
/// Verifies that Serialize stamps $meta and Deserialize handles both Phase 2 and legacy JSON.
/// </summary>
public class BlueprintJsonServicesTests
{
    private static BlueprintAsset MakeAsset() => new BlueprintAsset
    {
        AssetId = new Guid("11111111-2222-3333-4444-555555555555"),
        Name    = "TestAsset"
    };

    /// <summary>
    /// JM-P2-004-T01: Serialize produces a JSON document with a valid $meta envelope
    /// containing the Blueprint doc type and schema version 1.
    /// </summary>
    [Fact]
    public void BlueprintJsonServices_Serialize_ProducesMetaEnvelope()
    {
        var asset = MakeAsset();
        var json  = BlueprintJsonServices.Serialize(asset);

        var dom = JsonNode.Parse(json)!.AsObject();
        Assert.True(JsonEnvelope.HasEnvelope(dom));

        var meta = JsonEnvelope.Read(dom);
        Assert.Equal(HrotDocumentTypes.Blueprint, meta.DocType);
        Assert.Equal(1, meta.SchemaVersion);
    }

    /// <summary>
    /// JM-P2-004-T02: A Phase 2 round-trip (Serialize then Deserialize) preserves key fields.
    /// </summary>
    [Fact]
    public void BlueprintJsonServices_Deserialize_Phase2_RoundTrips()
    {
        var asset  = MakeAsset();
        var json   = BlueprintJsonServices.Serialize(asset);
        var result = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(result);
        Assert.Equal(asset.AssetId, result!.AssetId);
        Assert.Equal(asset.Name, result.Name);
    }

    /// <summary>
    /// JM-P2-004-T03: Legacy JSON without $meta is deserialized without error.
    /// System.Text.Json silently ignores unknown properties (no JsonUnmappedMemberHandling.Disallow),
    /// so the absence of $meta in legacy files is handled transparently.
    /// </summary>
    [Fact]
    public void BlueprintJsonServices_Deserialize_LegacyJson_Works()
    {
        // Minimal legacy-format JSON: no $meta envelope.
        const string legacyJson =
            "{\"AssetId\":\"11111111-2222-3333-4444-555555555555\",\"Name\":\"LegacyAsset\"}";

        var result = BlueprintJsonServices.Deserialize(legacyJson);

        Assert.NotNull(result);
        Assert.Equal(new Guid("11111111-2222-3333-4444-555555555555"), result!.AssetId);
        Assert.Equal("LegacyAsset", result.Name);
    }
}
