using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hrot.ScenarioEditor.Services;

/// <summary>
/// <b>The curated test-scenario set — committed in git, copied into the working NAS folder on start.</b>
///
/// <para>Mirrors the shipped-default-layout pattern (<c>Fdp.Presentation.WindowManager.LayoutPaths</c>,
/// <c>docs/UX/UX_Feature_Layout_Defaults.md</c>) for scenarios. The difference from the layout case is
/// deliberate and driven by the requirements:</para>
/// <list type="bullet">
///   <item><b>The git folder IS the manifest.</b> Whatever <c>&lt;name&gt;/scenario.json</c> folders live
///     under the repo's <c>scenarios/</c> directory ARE the curated set — nothing else lists them.</item>
///   <item><b>Overlay by name, never a mirror.</b> On start each curated name is force-copied into the
///     working root, overwriting only those names. Non-curated scenarios in the working folder are never
///     touched and nothing is ever deleted.</item>
///   <item><b>A scenario is a folder</b> (<c>&lt;name&gt;/scenario.json</c> plus any sidecars), so the copy
///     is a directory-tree copy rather than the layout's two named files.</item>
///   <item><b>Dev-only by construction.</b> The git set is located by walking up to the source tree
///     (exactly as <c>LayoutPaths.TryFindSourceLayoutDirectory</c> does). A deployed build has no source
///     tree, so <see cref="TryFindSourceScenariosDirectory"/> returns <c>null</c>: the start-up seed is a
///     no-op and the "save back to git" menu item is disabled. There is nothing to copy and no operator
///     folder to disturb.</item>
/// </list>
///
/// <para>Only the editor wires this today; the logic is host-agnostic (the working root is a parameter),
/// so CGF or any other host can call the same helper.</para>
/// </summary>
public static class CuratedScenarios
{
    /// <summary>The repo-relative directory that holds the committed curated set.</summary>
    public const string ScenariosDirectoryName = "scenarios";

    /// <summary>The marker file that makes a folder a scenario (matches the runtime enumerator).</summary>
    public const string MarkerFileName = "scenario.json";

    /// <summary>
    /// <b>The committed curated scenarios directory in the source tree</b> — <c>&lt;repo&gt;/scenarios</c> —
    /// or <c>null</c> when the app is not running from a checkout.
    ///
    /// <para>Walks up from the output directory, the same precedent
    /// <c>LayoutPaths.TryFindSourceLayoutDirectory</c> uses. It requires the directory to actually contain
    /// at least one curated scenario (a <c>scenario.json</c> somewhere below it), so an unrelated
    /// <c>scenarios</c> folder that happens to sit on the path is not mistaken for the set — and a deployed
    /// build, which has no such source tree, correctly yields <c>null</c>.</para>
    /// </summary>
    public static string? TryFindSourceScenariosDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, ScenariosDirectoryName);
            if (Directory.Exists(candidate) && CuratedRelPaths(candidate).Count > 0)
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// <c>true</c> when a source-tree curated set exists — i.e. the "save curated scenarios to git" command
    /// can run. In a deployed build this is <c>false</c>, and the caller disables the menu item.
    /// </summary>
    public static bool CanSaveToGit() => TryFindSourceScenariosDirectory() != null;

    /// <summary>
    /// <b>Copy every curated scenario from git into the working root on start, force-overwriting only those
    /// names.</b> Returns the curated relative paths copied; empty (never an error) when there is no source
    /// tree — a deployed build simply has nothing to seed.
    /// </summary>
    /// <param name="workingRoot">The runtime scenarios root (e.g. <c>EditorBootstrap.ScenariosRoot</c>).</param>
    public static IReadOnlyList<string> SeedIntoWorking(string workingRoot)
    {
        if (string.IsNullOrWhiteSpace(workingRoot)) throw new ArgumentException("A working root is required.", nameof(workingRoot));

        var source = TryFindSourceScenariosDirectory();
        if (source is null) return Array.Empty<string>();
        return SeedFrom(source, workingRoot);
    }

    /// <summary>
    /// The pure copy behind <see cref="SeedIntoWorking"/> — copies every curated name from
    /// <paramref name="sourceRoot"/> into <paramref name="workingRoot"/>, force-overwriting only those
    /// names. Exposed so the behaviour is testable without depending on the walk-up probe's result.
    /// </summary>
    public static IReadOnlyList<string> SeedFrom(string sourceRoot, string workingRoot)
    {
        var copied = new List<string>();
        foreach (var rel in CuratedRelPaths(sourceRoot))
        {
            CopyScenarioFolder(Path.Combine(sourceRoot, rel), Path.Combine(workingRoot, rel));
            copied.Add(rel);
        }
        return copied;
    }

    /// <summary>
    /// <b>Copy the working copy of every curated scenario back into the git set, force-overwriting git.</b>
    /// The set of names is defined by git — this refreshes their contents, it does not add or remove
    /// members. Returns the curated relative paths written; empty when there is no source tree (the menu
    /// item is disabled in that case) or when a curated working copy is missing.
    /// </summary>
    /// <param name="workingRoot">The runtime scenarios root the curated copies were seeded into.</param>
    public static IReadOnlyList<string> SaveWorkingToGit(string workingRoot)
    {
        if (string.IsNullOrWhiteSpace(workingRoot)) throw new ArgumentException("A working root is required.", nameof(workingRoot));

        var source = TryFindSourceScenariosDirectory();
        if (source is null) return Array.Empty<string>();
        return SaveTo(source, workingRoot);
    }

    /// <summary>
    /// The pure copy behind <see cref="SaveWorkingToGit"/> — copies the working copy of every curated name
    /// (as defined by <paramref name="sourceRoot"/>'s manifest) back into <paramref name="sourceRoot"/>,
    /// force-overwriting. A curated name with no working copy is skipped, not an error. Exposed for tests.
    /// </summary>
    public static IReadOnlyList<string> SaveTo(string sourceRoot, string workingRoot)
    {
        var written = new List<string>();
        foreach (var rel in CuratedRelPaths(sourceRoot))
        {
            var working = Path.Combine(workingRoot, rel);
            if (!File.Exists(Path.Combine(working, MarkerFileName))) continue;   // never seeded / user deleted it — skip, do not error
            CopyScenarioFolder(working, Path.Combine(sourceRoot, rel));
            written.Add(rel);
        }
        return written;
    }

    /// <summary>
    /// The curated relative paths under <paramref name="root"/> — every folder that contains a
    /// <see cref="MarkerFileName"/>, forward-slash normalized and ordinally sorted. Supports nesting
    /// (e.g. <c>Combat/Ambush</c>), matching the runtime enumerator.
    /// </summary>
    public static IReadOnlyList<string> CuratedRelPaths(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();

        var rels = new List<string>();
        foreach (var marker in Directory.EnumerateFiles(root, MarkerFileName, SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(marker);
            if (dir is null) continue;
            var rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
            if (rel != ".") rels.Add(rel);
        }
        rels.Sort(StringComparer.Ordinal);
        return rels;
    }

    /// <summary>
    /// Copy one scenario folder (all its files, recursively) from <paramref name="src"/> to
    /// <paramref name="dst"/>, force-overwriting. Creates <paramref name="dst"/> and any subdirectories.
    /// </summary>
    private static void CopyScenarioFolder(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
