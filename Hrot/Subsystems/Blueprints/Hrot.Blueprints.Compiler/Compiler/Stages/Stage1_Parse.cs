using System.Text.Json;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage1_Parse
{
    public static BlueprintAsset? Run(string json, DiagnosticSink sink)
    {
        BlueprintAsset? asset;
        try
        {
            asset = BlueprintJsonServices.Deserialize(json);
        }
        catch (JsonException ex)
        {
            sink.Add(Diagnostic.Error(DiagnosticCodes.BP0002_JsonParseError,
                $"JSON parse error: {ex.Message} (path: {ex.Path}, line: {ex.LineNumber})"));
            return null;
        }

        if (asset is null)
        {
            sink.Add(Diagnostic.Error(DiagnosticCodes.BP0001_NullAsset,
                "JSON deserialized to null. File may be empty or malformed."));
            return null;
        }

        if (asset.AssetId == Guid.Empty)
            sink.Add(Diagnostic.Error(DiagnosticCodes.BP0010_EmptyAssetId,
                "Asset has empty/zero AssetId.", asset.AssetId));

        if (string.IsNullOrEmpty(asset.Name))
            sink.Add(Diagnostic.Error(DiagnosticCodes.BP0011_EmptyName,
                "Asset has empty Name.", asset.AssetId));

        return asset;
    }
}

