using Hrot.Blueprints.Core.Assets;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Core;

/// <summary>
/// Compiles BlueprintAsset objects to C# source code.
/// Minimal stub for Phase 1; full implementation in Phase 3 (Compiler DD).
/// </summary>
public sealed class BlueprintCompiler
{
    /// <summary>
    /// Compile a single asset to C# source.
    /// Stub throws NotImplementedException -- full compiler is Phase 3.
    /// </summary>
    public string Compile(BlueprintAsset asset, CompilerMode mode)
        => throw new NotImplementedException(
            "BlueprintCompiler is not yet implemented (Phase 3). " +
            "Do not call CompileAndLoad in Phase 1 tests.");
}
