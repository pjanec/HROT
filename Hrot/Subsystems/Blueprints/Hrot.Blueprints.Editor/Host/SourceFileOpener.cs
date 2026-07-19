using System.Diagnostics;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Editor punch-list #6 — opens a source file in Visual Studio (best-effort, Windows).
/// Prefers <c>devenv /edit</c> so the file opens in the already-running VS instance that has the
/// repo solution loaded; falls back to shell-opening the file with its default handler. All
/// failures are swallowed — the inspector still shows the resolved <c>file:line</c> text either way.
/// </summary>
internal static class SourceFileOpener
{
    /// <summary>
    /// Attempts to open <paramref name="file"/> at <paramref name="line"/> (1-based; 0 = no line).
    /// Returns true once a launch is started. Order:
    /// <list type="number">
    ///   <item>VS Code — <c>code -g "file:line"</c> — the only common editor that jumps to a line
    ///     from the CLI (Visual Studio's devenv cannot; that needs DTE COM automation).</item>
    ///   <item>Visual Studio — <c>devenv /edit "file"</c> — opens the file in the running instance
    ///     (no line jump).</item>
    ///   <item>Default shell handler for the file.</item>
    /// </list>
    /// </summary>
    public static bool Open(string file, int line = 0)
    {
        if (string.IsNullOrEmpty(file)) return false;

        // 1. VS Code: jumps to the exact line.
        if (line > 0 && TryStart("code", $"-g \"{file}:{line}\"")) return true;

        // 2. Visual Studio: opens the file in the running devenv (App Paths–resolved), no line jump.
        if (TryStart("devenv", $"/edit \"{file}\"")) return true;

        // 3. Default handler.
        return TryStart(file, arguments: null);
    }

    private static bool TryStart(string fileName, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = fileName,
                Arguments       = arguments ?? string.Empty,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
