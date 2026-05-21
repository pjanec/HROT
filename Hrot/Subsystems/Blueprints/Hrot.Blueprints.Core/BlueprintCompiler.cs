using Hrot.Blueprints.Core.Assets;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Core;

/// <summary>
/// Backward-compatibility wrapper used by test fixtures and Phase 1 callers.
/// Full compiler infrastructure lives in Hrot.Blueprints.Core.Compiler (Phase 3).
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
