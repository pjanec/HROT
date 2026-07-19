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
    /// Attempts to open <paramref name="file"/> at <paramref name="line"/> (1-based; 0 = no line),
    /// preferring Visual Studio. Returns true once a launch/command succeeds. Order:
    /// <list type="number">
    ///   <item>Running <b>Visual Studio</b> via DTE/ROT — opens + jumps to the line in the instance
    ///     that has the file's solution (see <see cref="VisualStudioDteOpener"/>).</item>
    ///   <item><c>devenv /edit "file" /command "edit.goto N"</c> — launches/attaches VS and jumps
    ///     (covers the "no VS running yet" case).</item>
    ///   <item>VS Code fallback — <c>code -g "file:line"</c>.</item>
    ///   <item>Default shell handler for the file.</item>
    /// </list>
    /// </summary>
    public static bool Open(string file, int line = 0)
    {
        if (string.IsNullOrEmpty(file)) return false;

        // 1. Running Visual Studio instance (robust: targets the window with our solution).
        if (VisualStudioDteOpener.TryOpenAtLine(file, line)) return true;

        // 2. Visual Studio via CLI — /edit reuses a running instance; edit.goto jumps to the line.
        var devenvArgs = line > 0
            ? $"/edit \"{file}\" /command \"edit.goto {line}\""
            : $"/edit \"{file}\"";
        if (TryStart("devenv", devenvArgs)) return true;

        // 3. VS Code fallback (jumps to the line).
        if (line > 0 && TryStart("code", $"-g \"{file}:{line}\"")) return true;

        // 4. Default handler.
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
