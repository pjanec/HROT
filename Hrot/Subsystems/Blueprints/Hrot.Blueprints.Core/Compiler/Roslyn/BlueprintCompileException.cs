namespace Hrot.Blueprints.Core.Compiler.Roslyn;

public sealed class BlueprintCompileException : Exception
{
    public IReadOnlyList<Diagnostics.Diagnostic> CompilerDiagnostics { get; }

    public BlueprintCompileException(
        string message,
        IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
        : base(message)
    {
        CompilerDiagnostics = diagnostics;
    }
}
