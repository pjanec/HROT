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
    /// <summary>Attempts to open <paramref name="file"/> in VS. Returns true if a launch was started.</summary>
    public static bool Open(string file)
    {
        if (string.IsNullOrEmpty(file)) return false;

        // Preferred: reuse a running devenv (resolved via the App Paths registry entry).
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "devenv",
                Arguments       = $"/edit \"{file}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            // devenv not found / not launchable — fall through to the default handler.
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = file,
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
