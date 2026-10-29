using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// ⭐⭐ <c>CE-1443</c> part B2 (AQ108 §2.15 "Save … as Recipe") — <b>the ONE place a file kind's recipes are found and written</b>: the kind's
/// <c>recipes/behaviors/&lt;Folder&gt;</c> under the content root (<see cref="AssetRoots.RecipesFor"/> — the NAS, the same root the assets come from),
/// files of the kind's extension (<see cref="AssetSavePath.GetExtension"/>). Every kind's <i>New …</i> lists through <see cref="Files"/> and every
/// <i>Save … as Recipe…</i> writes to <see cref="PathFor"/>, so what one saves the other lists. ⛔ Not three copies of a directory scan.
/// </summary>
public static class RecipeFiles
{
    /// <summary>The recipe files of <paramref name="kind"/>, by name (case-insensitive extension match: files authored on Windows may differ in case).</summary>
    public static IReadOnlyList<string> Files(AssetKind kind)
    {
        var dir = AssetRoots.RecipesFor(kind);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "*" + AssetSavePath.GetExtension(kind), new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
    }

    /// <summary>Where a new recipe named <paramref name="name"/> is written — ⛔ never over an existing one: <i>name (2)</i>, <i>name (3)</i>, … instead.</summary>
    public static string PathFor(AssetKind kind, string name)
    {
        var dir = AssetRoots.RecipesFor(kind);
        var ext = AssetSavePath.GetExtension(kind);
        var path = Path.Combine(dir, name + ext);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{name} ({n}){ext}");
        return path;
    }

    /// <summary>The recipes of <paramref name="kind"/> read by <paramref name="read"/>; a file that does not read (null or a throw) is skipped.</summary>
    public static IEnumerable<T> Read<T>(AssetKind kind, Func<string, T?> read) where T : class
    {
        foreach (var file in Files(kind))
        {
            T? item = null;
            try { item = read(file); } catch { /* an unreadable recipe is skipped, never fatal to the New dialog */ }
            if (item != null) yield return item;
        }
    }
}
