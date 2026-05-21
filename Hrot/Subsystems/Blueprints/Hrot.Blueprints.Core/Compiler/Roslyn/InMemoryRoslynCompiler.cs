using System.Reflection;
using System.Runtime.Loader;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

/// <summary>
/// Compiles C# source strings in memory using Roslyn and loads the result
/// into a collectible AssemblyLoadContext.
/// Full implementation in TASK-CP-005.
/// </summary>
public sealed class InMemoryRoslynCompiler
{
    /// <summary>
    /// Compile source code and load into a new collectible ALC.
    /// Stub throws NotImplementedException -- full implementation is TASK-CP-005.
    /// </summary>
    public Assembly CompileAndLoad(
        string source,
        string virtualSourcePath,
        string assemblyName,
        DiagnosticSink sink)
        => throw new NotImplementedException("InMemoryRoslynCompiler not yet implemented (TASK-CP-005).");
}
