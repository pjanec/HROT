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
    private readonly ThumbnailAtlas? _atlas;

    /// <summary>A map over <paramref name="folder"/> with <paramref name="rules"/>; <paramref name="exists"/> (default: the file system) decides whether a file is there.
    /// ⭐ CE-4271 — <paramref name="atlas"/> is the embedded entity atlas step ④ asks (null: no step ④). ⛔ A production caller that HAS <see cref="ThumbnailAtlas.Shared"/> must pass it.</summary>
    public ThumbnailMap(string folder, IReadOnlyList<ThumbnailRule> rules, Func<string, bool>? exists = null, ThumbnailAtlas? atlas = null)
    {
        _atlas = atlas;
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _exists = exists ?? File.Exists;
    }

    /// <summary>No rules: every tile without an explicit name draws its glyph.</summary>
    public static ThumbnailMap Empty(string folder, ThumbnailAtlas? atlas = null) => new(folder, Array.Empty<ThumbnailRule>(), null, atlas);

    /// <summary>⭐ CE-4271 — the atlas step ④ asks (null: none).</summary>
    public ThumbnailAtlas? Atlas => _atlas;

    /// <summary>The rules, in file order.</summary>
    public IReadOnlyList<ThumbnailRule> Rules => _rules;

    /// <summary>Parses <c>map.json</c>; throws <see cref="FormatException"/> naming a bad mask or a row with no file.</summary>
    public static ThumbnailMap Parse(string json, string folder, Func<string, bool>? exists = null, ThumbnailAtlas? atlas = null)
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
            return new ThumbnailMap(folder, rules, exists, atlas);
        }
    }

    /// <summary>Reads <paramref name="path"/> (the folder is its directory); an absent file is an empty map, an unreadable one null.</summary>
    public static ThumbnailMap? TryLoad(string path, out string? error, ThumbnailAtlas? atlas = null)
    {
        error = null;
        string folder = Path.GetDirectoryName(path) ?? "";
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path), folder, null, atlas) : Empty(folder, atlas);
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
    /// ⭐ CE-4268 (§2i) — <paramref name="typeName"/> is the TKB type's <c>Name</c> (the one <c>GET /tkb/types</c> lists): step ③, the lowest, is <c>generated/&lt;typeName&gt;.png</c> in the same folder —
    /// the file <c>scripts/thumbnails.py</c> writes. A generated picture only fills a gap: ① and ② win. The name is sanitized (<see cref="GeneratedFileName"/>); one holding <c>..</c> is refused.
    /// </summary>
    public string? Resolve(string? explicitName, DISEntityType? dis, string? typeName = null)
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
        var stem = GeneratedFileName(typeName);
        if (stem is null) return null;
        if (FileIn(stem, GeneratedFolder) is { } loose) return loose;                                     // ③ a loose generated/<stem>.png (a fresh capture shows at once)
        return _atlas is { } a && a.Contains(stem) ? ThumbnailAtlas.KeyOf(stem) : null;                    // ④ ⭐ CE-4271 — the embedded atlas picture's key (the cache serves it resident)
    }

    /// <summary>The sub-folder of the generated pictures (beside <c>map.json</c>).</summary>
    public const string GeneratedFolder = "generated";

    /// <summary>⭐ CE-4268 — the file stem a type's generated picture has: letters, digits, <c>.</c> <c>_</c> <c>-</c> kept, anything else <c>_</c> (<c>scripts/thumbnails.py</c> uses the SAME rule). Null for an empty name or one holding <c>..</c>.</summary>
    public static string? GeneratedFileName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || typeName.Contains("..")) return null;
        var sb = new System.Text.StringBuilder(typeName.Trim().Length);
        foreach (char c in typeName.Trim()) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        return sb.ToString();
    }

    private string? FileIn(string name, string? subFolder = null)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
        string path = subFolder is null ? Path.Combine(_folder, name) : Path.Combine(_folder, subFolder, name);
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

    /// <summary>Loads <paramref name="folder"/>; <paramref name="error"/> says what failed when the result is null. ⭐ CE-4271 — <paramref name="atlas"/> feeds the thumbnail map's step ④.</summary>
    public static GalleryData? TryLoad(string folder, out string? error, ThumbnailAtlas? atlas = null)
    {
        var groups = GalleryGroups.TryLoad(Path.Combine(folder, GroupsFile), out error);
        if (groups is null) return null;
        var map = ThumbnailMap.TryLoad(Path.Combine(folder, MapFile), out error, atlas);
        return map is null ? null : new GalleryData(groups, map);
    }

    /// <summary>The shipped gallery: the NAS's <c>assets/thumbnails</c> (a dev start seeds it from the repository's).</summary>
    public static GalleryData? Shipped() => ShippedOver(Fdp.Toolkit.Orchestration.ContentLayout.ThumbnailAssets(Fdp.Toolkit.Orchestration.ContentLayout.NasRoot));

    /// <summary>⭐ CE-4271 — <see cref="Shipped"/> over a given folder: the ONE place the shipped gallery is built, and it PASSES <see cref="ThumbnailAtlas.Shared"/> (the forwarding rail builds one over a temp folder).</summary>
    public static GalleryData? ShippedOver(string folder) => TryLoad(folder, out _, ThumbnailAtlas.Shared);
}
