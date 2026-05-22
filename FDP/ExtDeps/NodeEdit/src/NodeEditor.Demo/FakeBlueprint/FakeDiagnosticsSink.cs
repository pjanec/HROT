using NodeEditor.Core.Interfaces;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>No-op diagnostics sink that writes to console.</summary>
public sealed class FakeDiagnosticsSink : IDiagnosticsSink
{
    public void Log(DiagnosticSeverity severity, string message, Exception? exception = null)
    {
        if (severity >= DiagnosticSeverity.Warning)
            Console.WriteLine($"[{severity}] {message}{(exception is not null ? " — " + exception.Message : "")}");
    }
}
