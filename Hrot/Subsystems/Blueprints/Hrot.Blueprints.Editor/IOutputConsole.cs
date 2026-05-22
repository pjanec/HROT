using Microsoft.CodeAnalysis;

namespace Hrot.Blueprints.Editor;

public interface IOutputConsole
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogDebug(string message);
    void LogDiagnostic(Diagnostic diagnostic);
}
