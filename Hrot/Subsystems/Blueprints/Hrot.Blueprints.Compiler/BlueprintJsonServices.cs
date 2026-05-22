using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hrot.Blueprints.Core.Assets;

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
        opts.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        _options = opts;
    }

    public static string Serialize(BlueprintAsset asset)
        => JsonSerializer.Serialize(asset, _options);

    public static BlueprintAsset? Deserialize(string json)
        => JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
}
