namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public sealed class DiagnosticSink
{
    private readonly List<Diagnostic> _diagnostics = new();

    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    // Same as HasErrors for Slice 1; distinction becomes relevant in Slice 2 for warnings-as-errors.
    public bool HasFatalErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public IReadOnlyList<Diagnostic> All => _diagnostics;
}
