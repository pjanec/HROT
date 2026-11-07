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

    /// <summary>⭐ B-9 N3 — the file a recipe named <paramref name="name"/> is written to when the user CHOSE the name: exactly that path, an existing recipe is replaced (the browser asked first).</summary>
    public static string ExactPathFor(AssetKind kind, string name)
        => Path.Combine(AssetRoots.RecipesFor(kind), name + AssetSavePath.GetExtension(kind));

    /// <summary>Does a recipe named <paramref name="name"/> already exist for <paramref name="kind"/>?</summary>
    public static bool Exists(AssetKind kind, string name) => File.Exists(ExactPathFor(kind, name));

    /// <summary>
    /// ⭐ B-9 N3 (docs/UX/DESIGN_New_Scenario_And_Recipe.md N3: "updating a recipe = a plain Save As over its name") — the Save-As browser request for
    /// <i>Save … as Recipe…</i>: ONE folder (the kind's recipes folder), listing the recipes already there, <c>NameExists</c> ⇒ the browser asks <i>Replace?</i>.
    /// </summary>
    public static NodeEditor.UI.Dialogs.SaveAsRequest SaveAsRequest(AssetKind kind, string title, string initialName)
        => new()
        {
            Title              = title,
            InitialName        = initialName,
            InitialDestination = "",
            ConfirmLabel       = "Save",
            GetFolderTree      = () => Browser.AssetFolderDerivation.ToCategoryNode(new List<string>()),
            GetFolderContents  = _ => Files(kind)
                .Select(f => new NodeEditor.UI.Dialogs.SaveAsContentItem(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)), AssetKindIcons.GetIconKey(kind)))
                .ToList(),
            NameExists         = (name, _) => Exists(kind, name),
            ValidateName       = name => string.IsNullOrWhiteSpace(name) ? "Name must not be empty."
                                       : name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "Name must not contain \\ / : * ? \" < > |."
                                       : null,
        };

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
