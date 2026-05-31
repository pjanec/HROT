using Microsoft.CodeAnalysis;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// <see cref="IOutputConsole"/> implementation that writes to the system console.
/// Used by blueprint editor services (QuickReloadService, FullRebuildService) in
/// production when no dedicated UI log panel is available.
/// </summary>
public sealed class SystemConsoleOutputConsole : IOutputConsole
{
    public void LogInfo(string message)    => Console.WriteLine($"[BP] INFO: {message}");
    public void LogWarning(string message) => Console.WriteLine($"[BP] WARN: {message}");
    public void LogError(string message)   => Console.WriteLine($"[BP] ERR:  {message}");
    public void LogDebug(string message)   => Console.WriteLine($"[BP] DBG:  {message}");

    public void LogDiagnostic(Diagnostic diagnostic)
        => Console.WriteLine($"[BP] {diagnostic.Severity}: {diagnostic.GetMessage()}");
}
