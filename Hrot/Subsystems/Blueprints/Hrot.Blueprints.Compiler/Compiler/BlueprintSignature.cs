using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// One parameter (input or output) of an exported blueprint function.
/// </summary>
public sealed record BlueprintParamSig(string Name, string TypeId);

/// <summary>
/// Signature of one exported blueprint function graph: its name plus
/// the positional input and output parameter list.
/// </summary>
public sealed record BlueprintFunctionSig(
    string Name,
    IReadOnlyList<BlueprintParamSig> Inputs,
    IReadOnlyList<BlueprintParamSig> Outputs);

public sealed record BlueprintSignature(
    string Path,
    Guid AssetId,
    string Name,
    string SanitizedName,
    int BlueprintId,
    BlueprintDispatchKind Dispatch,
    IReadOnlyList<BlueprintFunctionSig> ExportedFunctions,
    IReadOnlyList<AiPrimitiveHosting> Hostings,
    IReadOnlyList<Guid> DeclaredCallablePeers)
{
    /// <summary>
    /// Computed from <see cref="ExportedFunctions"/>; preserves the original names-only contract
    /// for callers (Stage2_Validate peer-ref check, tests) without a breaking change.
    /// </summary>
    public IReadOnlyList<string> ExportedFunctionNames
        => ExportedFunctions.Select(f => f.Name).ToArray();
}
