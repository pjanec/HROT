using System;
using System.Collections.Generic;
using System.IO;

namespace Hrot.AiEditor.Persistence;

/// <summary>
/// Pure static guard implementing design §3 D5: a <c>.cs</c> file and an editor-owned
/// compound-suffix JSON file (<c>.btree.json</c>, <c>.hsm.json</c>, <c>.bp.json</c>)
/// must not share the same logical base name in the same directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Representation classes (D5):</b>
/// <list type="bullet">
///   <item><b>CS</b> — a file whose name ends with <c>.cs</c> (case-insensitive).</item>
///   <item><b>JSON</b> — a file whose name ends with one of the known compound suffixes
///       <c>.btree.json</c>, <c>.hsm.json</c>, <c>.bp.json</c> (longest-match, case-insensitive).</item>
/// </list>
/// </para>
/// <para>
/// <b>Collision rule:</b> two files in the same directory that share the same logical base
/// name (case-insensitive) but belong to <em>opposite</em> representation classes (CS↔JSON)
/// constitute a D5 collision.  Two files in the <em>same</em> class are not a collision
/// here (e.g. a <c>Foo.btree.json</c> and a <c>Foo.hsm.json</c> are different-kind JSON
/// files — handled elsewhere; a <c>Foo.cs</c> and a second <c>Foo.cs</c> is a plain
/// duplicate — also handled elsewhere).
/// </para>
/// <para>
/// This type is <c>netstandard2.0</c>-safe and has no filesystem dependencies of its own —
/// the directory listing is always supplied by the caller, making it fully testable without
/// the filesystem.
/// </para>
/// </remarks>
public static class AssetBaseNameCollisionGuard
{
    // ── Known editor-owned compound JSON suffixes (longest-match first) ──────

    private static readonly string[] s_jsonSuffixes = new[]
    {
        ".btree.json",
        ".hsm.json",
        ".bp.json",
    };

    // ── Representation class ─────────────────────────────────────────────────

    private enum RepClass { Cs, Json, Other }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the logical base name for a file name.
    /// <list type="bullet">
    ///   <item>Known compound JSON suffix (<c>.btree.json</c>/<c>.hsm.json</c>/<c>.bp.json</c>,
    ///       longest match, case-insensitive) → strips the entire compound suffix.</item>
    ///   <item><c>.cs</c> (case-insensitive) → strips <c>.cs</c>.</item>
    ///   <item>Anything else → strips the final extension (same as
    ///       <see cref="Path.GetFileNameWithoutExtension"/>).</item>
    /// </list>
    /// The original casing of the base name is preserved.
    /// </summary>
    /// <param name="fileName">A file name (not a full path; directory separators are not expected).</param>
    public static string GetLogicalBaseName(string fileName)
    {
        if (fileName is null) throw new ArgumentNullException(nameof(fileName));

        // Try each compound suffix (longest first).
        foreach (var suffix in s_jsonSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - suffix.Length);
        }

        // Plain .cs
        if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - ".cs".Length);

        // Fallback: strip the final extension.
        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>
    /// Checks whether creating/saving a file named <paramref name="targetFileName"/> in a
    /// directory that already contains <paramref name="siblingFileNames"/> would introduce a
    /// D5 base-name collision.
    /// </summary>
    /// <param name="targetFileName">
    ///   The file name (not full path) of the file we intend to create or save.
    /// </param>
    /// <param name="siblingFileNames">
    ///   The file names (not full paths) of all files currently present in the
    ///   <em>same</em> directory.  The target itself may appear in this list — it is
    ///   ignored (we only check cross-file conflicts).
    /// </param>
    /// <param name="directoryForMessage">
    ///   The directory path to include in the human-readable error message.
    ///   May be <c>null</c> or empty; omitted from the message when absent.
    /// </param>
    /// <returns>
    ///   <c>null</c> if no D5 collision exists; a human-readable error message otherwise.
    /// </returns>
    public static string? CheckCollision(
        string                  targetFileName,
        IEnumerable<string>     siblingFileNames,
        string?                 directoryForMessage = null)
    {
        if (targetFileName is null)     throw new ArgumentNullException(nameof(targetFileName));
        if (siblingFileNames is null)   throw new ArgumentNullException(nameof(siblingFileNames));

        var targetBase  = GetLogicalBaseName(targetFileName);
        var targetClass = GetRepClass(targetFileName);

        // Only CS and JSON classes can collide with each other.
        if (targetClass == RepClass.Other)
            return null;

        foreach (var sibling in siblingFileNames)
        {
            if (sibling is null) continue;

            // Skip exact self-match (the target file is already in the directory).
            if (string.Equals(sibling, targetFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var sibClass = GetRepClass(sibling);

            // Same class → not a D5 collision (two JSONs or two .cs files).
            if (sibClass == targetClass || sibClass == RepClass.Other)
                continue;

            var sibBase = GetLogicalBaseName(sibling);

            if (string.Equals(sibBase, targetBase, StringComparison.OrdinalIgnoreCase))
            {
                // Collision detected.
                var dir = string.IsNullOrEmpty(directoryForMessage)
                    ? string.Empty
                    : $" in '{directoryForMessage}'";
                return $"[D5] Base-name collision{dir}: '{targetFileName}' and '{sibling}' share " +
                       $"the logical base name '{targetBase}' but represent the same asset in " +
                       $"opposite representations (CS↔JSON). Per design §3 D5, they must not " +
                       $"coexist in the same directory.";
            }
        }

        return null;
    }

    /// <summary>
    /// Convenience overload: resolves the sibling list by scanning the target file's own
    /// directory on disk via <paramref name="listFilesInDir"/>.
    /// </summary>
    /// <param name="targetFilePath">
    ///   The absolute (or rooted) path of the file we intend to create or save.
    /// </param>
    /// <param name="listFilesInDir">
    ///   A delegate that, given a directory path, returns the <em>full paths</em> of all
    ///   files in that directory.  Pass <c>dir =&gt; Directory.EnumerateFiles(dir)</c> in
    ///   production; inject a test double for unit tests.
    /// </param>
    /// <returns>
    ///   <c>null</c> if no D5 collision exists; a human-readable error message otherwise.
    /// </returns>
    public static string? CheckCollisionOnDisk(
        string                          targetFilePath,
        Func<string, IEnumerable<string>> listFilesInDir)
    {
        if (targetFilePath is null)  throw new ArgumentNullException(nameof(targetFilePath));
        if (listFilesInDir is null)  throw new ArgumentNullException(nameof(listFilesInDir));

        var dir            = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
        var targetFileName = Path.GetFileName(targetFilePath);

        IEnumerable<string> fullPaths;
        try
        {
            fullPaths = listFilesInDir(dir);
        }
        catch
        {
            // Directory does not exist yet or is not accessible — no siblings, no collision.
            return null;
        }

        // Convert full paths to file names only so CheckCollision works on names.
        var siblingNames = new List<string>();
        foreach (var fp in fullPaths)
        {
            if (fp is null) continue;
            siblingNames.Add(Path.GetFileName(fp));
        }

        return CheckCollision(targetFileName, siblingNames, dir);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static RepClass GetRepClass(string fileName)
    {
        foreach (var suffix in s_jsonSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return RepClass.Json;
        }

        if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return RepClass.Cs;

        return RepClass.Other;
    }
}
