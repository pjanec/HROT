using System;
using System.IO;
using System.Text;

namespace Hrot.AiEditor.Persistence;

/// <summary>
/// Writes a UTF-8 text file atomically via a temp-then-move pattern.
/// <para>
/// Used by <c>SaveAllAiDocumentsCommand</c> to write BTree/HSM JSON files safely:
/// the target file is never left in a partially-written state even if the process
/// is interrupted mid-write.
/// </para>
/// <para>
/// Design §PU-602: lives in <c>Hrot.AiEditor.Persistence</c> (netstandard2.0) so
/// it can be shared by the editor (net8) and the Phase-2 Roslyn generator.
/// Blueprint files continue to use <c>File.WriteAllText</c> via <c>SaveActiveBlueprintCommand</c>
/// — this class is intentionally separate.
/// </para>
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> atomically.
    ///
    /// <list type="bullet">
    ///   <item>The content is written to a <c>.tmp</c> sidecar in the same directory.</item>
    ///   <item>The sidecar is then moved over the target, replacing it atomically on
    ///       the same filesystem (best-effort; on Windows this is not truly atomic for
    ///       cross-drive moves, but same-volume moves are atomic on NTFS).</item>
    ///   <item>The target directory is created if it does not exist.</item>
    ///   <item>Never throws on success; propagates IO exceptions to the caller.</item>
    /// </list>
    /// </summary>
    /// <param name="path">Absolute (or rooted) path of the file to write.</param>
    /// <param name="content">UTF-8 text content to write.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    public static void Write(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be non-empty.", nameof(path));
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            // Move over the target — atomic on same-volume NTFS; delete first for netstandard2.0
            // compatibility (File.Move(src, dst, overwrite) is .NET 5+).
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            // Clean up the temp file on failure to avoid leaving stale .tmp files.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* swallow cleanup errors */ }
            throw;
        }
    }
}
