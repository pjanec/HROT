using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Compiler;

public sealed record CompileResult(
    bool Succeeded,
    string? GeneratedSource,
    string? GeneratedFileName,
    int BlueprintId,
    ulong StructureHash,
    DebugMap? DebugMap,
    IReadOnlyList<Diagnostic> Diagnostics,
    BlueprintAsset? CanonicalAsset,
    byte[]? PortablePdb,
    byte[]? PortablePe);

public sealed record ValidationOptions(bool ResolveSiblings = true);

public sealed record ValidationResult(IReadOnlyList<Diagnostic> Diagnostics);
