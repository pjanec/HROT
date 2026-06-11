using System;
using System.Collections.Generic;
using System.IO;

namespace Hrot.Editor;

/// <summary>
/// Static helper that enumerates scenario relative paths by scanning the
/// filesystem for <c>scenario.json</c> marker files.
/// </summary>
public static class ScenarioEnumeration
{
    /// <summary>
    /// Recursively finds every directory under <paramref name="scenariosRoot"/>
    /// that contains a <c>scenario.json</c> file and returns each directory's
    /// path relative to <paramref name="scenariosRoot"/>, with <c>/</c>
    /// separators and stable ordinal sort order.
    /// </summary>
    /// <param name="scenariosRoot">The root scenario directory.</param>
    /// <returns>
    /// Relative paths (e.g. <c>["Combat/Ambush", "Combat/Patrol", "alpha"]</c>).
    /// An empty list when the root does not exist.
    /// </returns>
    public static IReadOnlyList<string> EnumerateRelPaths(string scenariosRoot)
    {
        if (string.IsNullOrWhiteSpace(scenariosRoot))
            return Array.Empty<string>();

        if (!Directory.Exists(scenariosRoot))
            return Array.Empty<string>();

        var results = new List<string>();
        EnumerateRecursive(scenariosRoot, string.Empty, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void EnumerateRecursive(
        string currentDir,
        string relativePath,
        List<string> results)
    {
        // Check the current directory for a scenario.json marker.
        string markerPath = Path.Combine(currentDir, "scenario.json");
        if (File.Exists(markerPath) && relativePath.Length > 0)
        {
            // Normalize to forward slashes.
            results.Add(relativePath.Replace('\\', '/'));
        }

        // Recurse into subdirectories.
        foreach (string subDir in Directory.EnumerateDirectories(currentDir))
        {
            string subDirName = Path.GetFileName(subDir);
            string childRelative = relativePath.Length == 0
                ? subDirName
                : relativePath + "/" + subDirName;
            EnumerateRecursive(subDir, childRelative, results);
        }
    }
}
