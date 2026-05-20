using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Fdp.Core.Serialization;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core;

public static class BlueprintJsonServices
{
    private static readonly JsonSerializerOptions _options;

    static BlueprintJsonServices()
    {
        // DefaultRelaxed is frozen (MakeReadOnly). Create a compatible non-frozen
        // instance so the DefaultJsonTypeInfoResolver can handle [JsonPolymorphic] /
        // [JsonDerivedType] on the Node hierarchy without conflict.
        var source = FdpJsonOptionsRegistry.DefaultRelaxed;
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = source.IncludeFields,
            PropertyNameCaseInsensitive = source.PropertyNameCaseInsensitive,
            AllowTrailingCommas         = source.AllowTrailingCommas,
            ReadCommentHandling         = source.ReadCommentHandling,
            DefaultIgnoreCondition      = source.DefaultIgnoreCondition,
            WriteIndented               = source.WriteIndented,
        };
        foreach (var converter in source.Converters)
            opts.Converters.Add(converter);
        opts.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        _options = opts;
    }

    public static string Serialize(BlueprintAsset asset)
        => JsonSerializer.Serialize(asset, _options);

    public static BlueprintAsset? Deserialize(string json)
        => JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
}
