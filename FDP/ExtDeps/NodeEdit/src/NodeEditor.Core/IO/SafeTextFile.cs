namespace NodeEditor.Core.IO;

/// <summary>
/// ⭐ CE-1441 (audit #16) — THE one way a per-user preference file is read and written: text in, text out, <b>never throws</b>.
/// A missing or unreadable file reads as <see langword="null"/>; a write creates the folder, goes to a temp file and is moved
/// over the target (so a crash mid-write never leaves half a file), and reports <see langword="false"/> when it cannot — a
/// preference must never stop a host. Lives here, the lowest assembly every settings class (<c>RecentStore</c> in
/// NodeEditor.UI, <c>AudioSettings</c> / <c>MapLayerPreferences</c> / <c>UnitDebugSettingsMemory</c> in Hrot.Presentation) can reach;
/// the user-folder PATH is resolved separately by <c>Hrot.UI.Common.UserFiles</c> (NodeEditor cannot see <c>LayoutPaths</c>).
/// </summary>
public static class SafeTextFile
{
    /// <summary>The file's text; <see langword="null"/> when the path is empty, the file is missing, or it cannot be read.</summary>
    public static string? TryRead(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Writes <paramref name="text"/> (temp file + move; the folder is created); false when it cannot. Never throws.</summary>
    public static bool TryWrite(string? path, string text)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }
}
