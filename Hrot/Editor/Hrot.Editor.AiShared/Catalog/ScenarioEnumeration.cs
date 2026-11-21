using System;
using System.Collections.Generic;
using System.IO;

namespace Hrot.Editor.AiShared.Catalog;

/// <summary>
/// Static helper that enumerates scenario relative paths by scanning the
/// filesystem for <c>scenario.json</c> marker files.
///
/// <para>⭐⭐ <b><c>CE-053</c> — MOVED to <c>Hrot.Editor.AiShared</c></b> with
/// <see cref="ScenarioCatalogContributor"/>, which is its only production consumer's sibling. 📐 It is a
/// pure <c>System.IO</c> scan with no host dependency; it sat in <c>Hrot.Editor/Browser</c> only because
/// that is where the editor happened to need it, so CGF could not enumerate scenarios at all.</para>
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

    /// <summary>
    /// ⭐ CE-4360 (docs/DESIGN_Content_Layout.md §6, Q-S2) — the scenario NAMES under <paramref name="scenariosRoot"/>: each scenario's LEAF folder name, whatever
    /// sub-folder it is filed in (<c>scenarios/demos/air/ua-air-assault</c> is <c>ua-air-assault</c>); a leaf shared by two scenarios is offered by its relative
    /// path. ⭐ What the editor's and CGF's scenario lists use; <see cref="EnumerateRelPaths"/> stays for a reader that wants the folder paths.
    /// </summary>
    public static IReadOnlyList<string> EnumerateNames(string scenariosRoot)
        => string.IsNullOrWhiteSpace(scenariosRoot) ? Array.Empty<string>() : Fdp.Toolkit.Orchestration.ScenarioFolderIndex.Names(scenariosRoot);

    private static void EnumerateRecursive(
        string currentDir,
        string relativePath,
        List<string> results)
    {
        // Check the current directory for a scenario.json marker. Enumerate the directory
        // once and match the marker name case-insensitively: File.Exists with a literal
        // name would silently miss e.g. "Scenario.json" authored on Windows (PlatformDefault
        // casing is case-sensitive on Linux).
        bool hasMarker = false;
        foreach (string entry in Directory.EnumerateFiles(currentDir))
        {
            if (string.Equals(Path.GetFileName(entry), Fdp.Toolkit.Orchestration.ContentLayout.ScenarioFileName, StringComparison.OrdinalIgnoreCase))
            {
                hasMarker = true;
                break;
            }
        }
        if (hasMarker && relativePath.Length > 0)
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
