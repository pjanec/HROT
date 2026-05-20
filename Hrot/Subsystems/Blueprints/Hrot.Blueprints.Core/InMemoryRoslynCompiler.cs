using System.Reflection;
using System.Runtime.Loader;

namespace Hrot.Blueprints.Core;

/// <summary>
/// Compiles C# source strings in memory using Roslyn and loads the result
/// into a collectible AssemblyLoadContext.
/// Stub for Phase 1; full implementation in Phase 3 (Compiler DD).
/// </summary>
public sealed class InMemoryRoslynCompiler
{
    /// <summary>
    /// Compile source code and load into a new collectible ALC.
    /// Stub throws NotImplementedException -- full implementation is Phase 3.
    /// </summary>
    public Assembly CompileAndLoad(string sourceCode, AssemblyLoadContext alc)
        => throw new NotImplementedException(
            "InMemoryRoslynCompiler is not yet implemented (Phase 3).");
}
