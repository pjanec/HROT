namespace NodeEditor.Core.Interfaces;

/// <summary>Optional sink for editor logs and telemetry.</summary>
public interface IDiagnosticsSink
{
    void Log(DiagnosticSeverity severity, string message, Exception? exception = null);
}

public enum DiagnosticSeverity
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
}
