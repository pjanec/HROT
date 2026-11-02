using System.Text.Json;
using Fdp.Core;

namespace Hrot.UI.Common.AddEntity;

/// <summary>One <c>map.json</c> row: a DIS mask and the PNG (a file in the thumbnails folder) a matching type shows.</summary>
public sealed record ThumbnailRule(DisTypeMask Mask, string File);

/// <summary>
/// ⭐ <c>CE-4142</c> GL2 (G4) — which THUMBNAIL a gallery tile shows. 📄 docs/DESIGN_Add_Entity_Picker.md §2f G4, §2g.
///
/// <para>⭐ The precedence, decided ONCE when the catalogue is built so a tile only asks the image cache for a path:
/// ① the TKB visual's explicit <c>ThumbnailName</c> (a file in the folder) → ② the file of the MOST SPECIFIC <c>map.json</c> mask
/// matching the type's DIS type (<see cref="DisTypeMask.Specificity"/>; a tie ⇒ the row listed first) → ③ null: the tile draws
/// today's DIS glyph on a dashed tile. A named file that does not exist is skipped, so a stale name falls through to the mask, never
/// to a broken tile.</para>
/// </summary>
public sealed class ThumbnailMap
{
    private readonly string _folder;
    private readonly IReadOnlyList<ThumbnailRule> _rules;
    private readonly Func<string, bool> _exists;

    /// <summary>A map over <paramref name="folder"/> with <paramref name="rules"/>; <paramref name="exists"/> (default: the file system) decides whether a file is there.</summary>
    public ThumbnailMap(string folder, IReadOnlyList<ThumbnailRule> rules, Func<string, bool>? exists = null)
    {
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _exists = exists ?? File.Exists;
    }

    /// <summary>No rules: every tile without an explicit name draws its glyph.</summary>
    public static ThumbnailMap Empty(string folder) => new(folder, Array.Empty<ThumbnailRule>());

    /// <summary>The rules, in file order.</summary>
    public IReadOnlyList<ThumbnailRule> Rules => _rules;

    /// <summary>Parses <c>map.json</c>; throws <see cref="FormatException"/> naming a bad mask or a row with no file.</summary>
    public static ThumbnailMap Parse(string json, string folder, Func<string, bool>? exists = null)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new FormatException("map.json: " + e.Message); }

        using (doc)
        {
            var rules = new List<ThumbnailRule>();
            if (doc.RootElement.TryGetProperty("thumbnails", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var row in arr.EnumerateArray())
                {
                    string? mask = row.TryGetProperty("mask", out var m) ? m.GetString() : null;
                    string? file = row.TryGetProperty("file", out var f) ? f.GetString() : null;
                    if (!DisTypeMask.TryParse(mask, out var parsed)) throw new FormatException($"map.json: '{mask}' is not a DIS mask.");
                    if (string.IsNullOrWhiteSpace(file)) throw new FormatException($"map.json: the mask '{mask}' names no file.");
                    rules.Add(new ThumbnailRule(parsed, file));
                }
            return new ThumbnailMap(folder, rules, exists);
        }
    }

    /// <summary>Reads <paramref name="path"/> (the folder is its directory); an absent file is an empty map, an unreadable one null.</summary>
    public static ThumbnailMap? TryLoad(string path, out string? error)
    {
        error = null;
        string folder = Path.GetDirectoryName(path) ?? "";
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path), folder) : Empty(folder);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>
    /// The picture of a type: the full path of its thumbnail, or null (⇒ the glyph). <paramref name="explicitName"/> is the TKB visual's
    /// <c>ThumbnailName</c> (with or without <c>.png</c>); <paramref name="dis"/> is the type's DIS type, null for none.
    /// </summary>
    public string? Resolve(string? explicitName, DISEntityType? dis)
    {
        if (!string.IsNullOrWhiteSpace(explicitName) && FileIn(explicitName.Trim()) is { } named) return named;

        if (dis is { } t && t.Value != 0)
        {
            ThumbnailRule? best = null;
            foreach (var r in _rules)
                if (r.Mask.Matches(t) && (best is null || r.Mask.Specificity > best.Mask.Specificity) && FileIn(r.File) is not null)
                    best = r;
            if (best is not null) return FileIn(best.File);
        }
        return null;
    }

    private string? FileIn(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
        string path = Path.Combine(_folder, name);
        return _exists(path) ? path : null;
    }
}

/// <summary>
/// ⭐ <c>CE-4142</c> — the gallery's two data files read together from one folder (<c>assets/thumbnails</c>): the groups and the thumbnail map.
/// <see cref="TryLoad"/> is null when <c>groups.json</c> is missing or broken — Add Entity then offers the plain DIS tree rather than a
/// gallery with every type in <i>Other</i>.
/// </summary>
public sealed record GalleryData(GalleryGroups Groups, ThumbnailMap Thumbnails)
{
    public const string GroupsFile = "groups.json";
    public const string MapFile = "map.json";

    /// <summary>Loads <paramref name="folder"/>; <paramref name="error"/> says what failed when the result is null.</summary>
    public static GalleryData? TryLoad(string folder, out string? error)
    {
        var groups = GalleryGroups.TryLoad(Path.Combine(folder, GroupsFile), out error);
        if (groups is null) return null;
        var map = ThumbnailMap.TryLoad(Path.Combine(folder, MapFile), out error);
        return map is null ? null : new GalleryData(groups, map);
    }

    /// <summary>The shipped gallery: the NAS's <c>assets/thumbnails</c> (a dev start seeds it from the repository's).</summary>
    public static GalleryData? Shipped()
        => TryLoad(Fdp.Toolkit.Orchestration.ContentLayout.ThumbnailAssets(Fdp.Toolkit.Orchestration.ContentLayout.NasRoot), out _);
}
