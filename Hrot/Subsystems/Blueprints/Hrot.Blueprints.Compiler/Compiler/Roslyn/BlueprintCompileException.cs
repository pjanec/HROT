namespace Hrot.Blueprints.Core.Compiler.Roslyn;

public sealed class BlueprintCompileException : Exception
{
    public IReadOnlyList<Diagnostics.Diagnostic> CompilerDiagnostics { get; }

    public BlueprintCompileException(
        string message,
        IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
        : base(message + "\n" + string.Join("\n", diagnostics.Select(d => $"  {d.Code}: {d.Message}")))
    {
        CompilerDiagnostics = diagnostics;
    }
}
