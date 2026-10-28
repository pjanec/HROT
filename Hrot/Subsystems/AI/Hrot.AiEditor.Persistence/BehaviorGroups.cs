namespace Hrot.AiEditor.Persistence;

/// <summary>
/// ⭐ <c>CE-3164</c> (R-268) — the ONE rule for a JSON asset's picker group: the folder path below its kind's root
/// (<c>assets/behaviors/BTrees/</c>, <c>…/HSMs/</c>, <c>…/Blueprints/</c> — ⭐ CE-4089: spelled as literals because this netstandard2.0 analyzer assembly cannot reference <c>ContentLayout</c>), <c>/</c>-separated. A file at the root has no group
/// (the picker shows it under "Other"). Relative by construction, so no machine path reaches generated code.
/// Used by all three asset generators; a hot reload (staging registry merged into the live one) carries none and the live registry keeps the one it had.
/// 📄 DESIGN_Behavior_Self_Registration.md §10c.
/// </summary>
public static class BehaviorGroups
{
    private static readonly string[] s_kindRoots = { "assets/behaviors/BTrees/", "assets/behaviors/HSMs/", "assets/behaviors/Blueprints/" };

    /// <summary>The group of the asset at <paramref name="path"/>, or <c>null</c> (root-level, or not under a kind root).</summary>
    public static string? FromAssetPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string p = path!.Replace('\\', '/');
        int best = -1, rootLen = 0;
        foreach (var root in s_kindRoots)
        {
            int i = p.LastIndexOf(root, System.StringComparison.OrdinalIgnoreCase);
            if (i > best) { best = i; rootLen = root.Length; }
        }
        if (best < 0) return null;
        string rest = p.Substring(best + rootLen);
        int slash = rest.LastIndexOf('/');
        return slash <= 0 ? null : rest.Substring(0, slash);
    }

    /// <summary>A C# string literal for a generated <c>Group = …</c> line (the group is folder names: no quotes, no escapes).</summary>
    public static string Literal(string group) => "\"" + group.Replace("\\", "/").Replace("\"", "") + "\"";
}
