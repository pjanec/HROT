using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;

namespace Hrot.ScenarioEditor.Services;

/// <summary>
/// <b>File › Save to Git — the NAS content copied BACK into the repository</b> (<c>CE-4089</c>, R-4097, docs/DESIGN_Content_Layout.md §2, L4).
/// ⭐ The other direction — the dev SEED, repository → NAS — is <see cref="ContentSeed"/> (Fdp.Toolkits; <c>CE-3340</c> ③: the ONE host start,
/// <c>SubsystemOrchestrator.Initialize</c>, calls it). This class keeps only what needs a source tree on demand.
///
/// <list type="bullet">
///   <item><b>Production: nothing.</b> The source tree is found by <see cref="ContentLayout.TryFindSourceTree"/> (the ONE repository walk-up);
///     a deployed install has none, so <see cref="CanSaveToGit"/> is false and <see cref="SaveToGit"/> a no-op.</item>
///   <item><b><see cref="SaveToGit"/>.</b> Per kind: <b>scenarios</b> keep the rule (the CURATED names only — the folders the repository already holds);
///     <b>assets</b> and <b>recipes</b> copy back every NAS file that is new or differs. ⛔ Nothing is ever deleted.</item>
///   <item><b>The git folder IS the manifest for scenarios.</b> Whatever <c>&lt;name&gt;/scenario.json</c> folders live under the repository's
///     <c>scenarios/</c> ARE the curated set; non-curated scenarios in the NAS are never touched.</item>
/// </list>
/// </summary>
public static class CuratedScenarios
{
    /// <summary>The repo-relative directory that holds the committed curated set (<see cref="ContentLayout.ScenariosDirectoryName"/>).</summary>
    public const string ScenariosDirectoryName = ContentLayout.ScenariosDirectoryName;

    /// <summary>
    /// <b>The committed scenarios directory in the source tree</b> — <c>&lt;repo&gt;/scenarios</c> — or <c>null</c> when the app is not running
    /// from a checkout. ⭐ Found through <see cref="ContentLayout.TryFindSourceTree"/>, the one repository walk-up.
    /// </summary>
    public static string? TryFindSourceScenariosDirectory()
        => ContentLayout.TryFindSourceTree() is { } tree ? ContentLayout.Scenarios(tree) : null;

    /// <summary>
    /// <c>true</c> when a source tree exists — i.e. the "Save to Git" command can run. In a deployed build this is <c>false</c>, and the caller
    /// disables the menu item.
    /// </summary>
    /// <param name="sourceSearchStartDirs">Where the repository walk-up starts; none ⇒ the working directory, then the app's base directory (tests pass an
    /// outside folder to stand for a production install).</param>
    public static bool CanSaveToGit(params string[] sourceSearchStartDirs) => ContentLayout.TryFindSourceTree(sourceSearchStartDirs) != null;

    // ══ SAVE TO GIT: NAS → source tree ════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Copy the NAS content back into the repository</b> (File › Save to Git). Per kind: <c>scenarios/</c> the curated names only (git defines
    /// the set; their contents are refreshed); <c>assets/</c> and <c>recipes/</c> every NAS file that is NEW or DIFFERS. ⛔ Never deletes.
    /// Returns the repository-relative paths written; empty when there is no source tree (the menu item is disabled then).
    /// </summary>
    /// <param name="nasRoot">The NAS root.</param>
    /// <param name="sourceSearchStartDirs">Where the repository walk-up starts; none ⇒ the working directory, then the app's base directory.</param>
    public static IReadOnlyList<string> SaveToGit(string nasRoot, params string[] sourceSearchStartDirs)
    {
        if (string.IsNullOrWhiteSpace(nasRoot)) throw new ArgumentException("A NAS root is required.", nameof(nasRoot));

        var tree = ContentLayout.TryFindSourceTree(sourceSearchStartDirs);
        return tree is null ? Array.Empty<string>() : SaveTreeTo(tree, nasRoot);
    }

    /// <summary>The pure copy behind <see cref="SaveToGit"/> — testable without the walk-up.</summary>
    public static IReadOnlyList<string> SaveTreeTo(string sourceTree, string nasRoot)
    {
        var written = new List<string>();
        foreach (var top in ContentLayout.SeededDirectories)
        {
            var git = Path.Combine(sourceTree, top);
            var nas = Path.Combine(nasRoot, top);
            if (!Directory.Exists(nas)) continue;

            if (top == ContentLayout.ScenariosDirectoryName)
            {
                foreach (var rel in ContentSeed.CuratedRelPaths(git))
                {
                    var working = Path.Combine(nas, rel);
                    if (!File.Exists(Path.Combine(working, ContentSeed.MarkerFileName))) continue;   // never seeded / user deleted it — skip, do not error
                    ContentSeed.CopyTree(working, Path.Combine(git, rel), top + "/" + rel, written);
                }
            }
            else
                ContentSeed.CopyTree(nas, git, top, written);
        }
        return written;
    }

    /// <summary>
    /// The scenario-only save — the NAS copy of every curated name (as defined by <paramref name="sourceRoot"/>'s manifest) back into
    /// <paramref name="sourceRoot"/>. A curated name with no working copy is skipped, not an error.
    /// </summary>
    public static IReadOnlyList<string> SaveTo(string sourceRoot, string workingRoot)
    {
        var written = new List<string>();
        foreach (var rel in ContentSeed.CuratedRelPaths(sourceRoot))
        {
            var working = Path.Combine(workingRoot, rel);
            if (!File.Exists(Path.Combine(working, ContentSeed.MarkerFileName))) continue;
            ContentSeed.CopyTree(working, Path.Combine(sourceRoot, rel), rel, null);
            written.Add(rel);
        }
        return written;
    }
}
