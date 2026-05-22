using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Emit;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor.Reload;

/// <summary>
/// Projects an in-memory BlueprintAsset into a lightweight BlueprintSignature
/// without serializing to disk. Required by QuickReloadService to satisfy the
/// compiler's SiblingSignatures requirement. (Editor DD Patch 1)
/// </summary>
public static class BlueprintSignatureBuilder
{
    public static BlueprintSignature FromInMemoryAsset(BlueprintAsset asset)
    {
        int blueprintId  = BlueprintIdHash.Compute(asset.AssetId);
        string sanitized = Sanitizer.SanitizeName(asset.Name);

        return new BlueprintSignature(
            Path:                  string.Empty,
            AssetId:               asset.AssetId,
            Name:                  asset.Name,
            SanitizedName:         sanitized,
            BlueprintId:           blueprintId,
            Dispatch:              asset.Dispatch,
            ExportedFunctionNames: asset.Graphs
                .Where(g => g.Kind == GraphKind.Function)
                .Select(g => g.Name)
                .ToArray(),
            Hostings:              (IReadOnlyList<AiPrimitiveHosting>?)asset.Primitive?.Hostings
                                   ?? Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: asset.CallablePeers);
    }
}
