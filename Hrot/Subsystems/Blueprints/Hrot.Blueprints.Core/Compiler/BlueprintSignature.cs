using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler;

public sealed record BlueprintSignature(
    string Path,
    Guid AssetId,
    string Name,
    string SanitizedName,
    int BlueprintId,
    BlueprintDispatchKind Dispatch,
    IReadOnlyList<string> ExportedFunctionNames,
    IReadOnlyList<AiPrimitiveHosting> Hostings,
    IReadOnlyList<Guid> DeclaredCallablePeers);
