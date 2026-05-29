using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hrot.Blueprints.Core.Assets;
#if NET8_0_OR_GREATER
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
#endif

namespace Hrot.Blueprints.Core;

public static class BlueprintJsonServices
{
    private static readonly JsonSerializerOptions _options;

    static BlueprintJsonServices()
    {
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            WriteIndented               = false,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        _options = opts;
    }

    public static string Serialize(BlueprintAsset asset)
    {
#if NET8_0_OR_GREATER
        // Serialize the asset to a DOM, then stamp $meta as the first property.
        var dom = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();
        JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, 1));
        return dom.ToJsonString();
#else
        return JsonSerializer.Serialize(asset, _options);
#endif
    }

    // Note (JM-P2-004): Deserialize needs no change.
    // System.Text.Json silently ignores unknown properties (no JsonUnmappedMemberHandling.Disallow
    // in _options), so a Phase 2 envelope with $meta is already handled transparently.
    // Both legacy JSON (no $meta) and Phase 2 JSON ($meta first) are deserialized correctly.
    public static BlueprintAsset? Deserialize(string json)
        => JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
}
