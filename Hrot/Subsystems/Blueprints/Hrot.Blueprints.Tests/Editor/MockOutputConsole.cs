using Microsoft.CodeAnalysis;
using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

internal sealed class MockOutputConsole : IOutputConsole
{
    public List<string> InfoMessages    { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<string> ErrorMessages   { get; } = new();
    public List<string> DebugMessages   { get; } = new();
    public List<Diagnostic> DiagMessages { get; } = new();

    public void LogInfo(string message)              => InfoMessages.Add(message);
    public void LogWarning(string message)           => WarningMessages.Add(message);
    public void LogError(string message)             => ErrorMessages.Add(message);
    public void LogDebug(string message)             => DebugMessages.Add(message);
    public void LogDiagnostic(Diagnostic diagnostic) => DiagMessages.Add(diagnostic);
}
