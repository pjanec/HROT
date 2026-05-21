namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public static Diagnostic Error(string code, string message)
        => new(DiagnosticSeverity.Error, code, message);
    public static Diagnostic Warning(string code, string message)
        => new(DiagnosticSeverity.Warning, code, message);
    public static Diagnostic Info(string code, string message)
        => new(DiagnosticSeverity.Info, code, message);
}
