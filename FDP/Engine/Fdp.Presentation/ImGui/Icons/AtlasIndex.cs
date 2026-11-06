using System.Collections.Generic;
using System.Text.Json;

namespace Fdp.Presentation.Icons;

/// <summary>
/// ⭐ CE-4271 (docs/DESIGN_Add_Entity_Picker.md §2j) — the PURE name → atlas-cell index of a fixed-cell picture atlas: no GPU, no file system, safe on any thread. It parses the index
/// <c>scripts/thumbnails.py</c> writes — <c>{ "version": 1, "cell": [w, h], "page": [w, h], "pages": ["entities-0.jpg", …], "items": { "&lt;name&gt;": [page, col, row], … } }</c> — and answers where a
/// name's cell is: its page and its pixel rectangle. ⛔ Nothing here knows a texture; <see cref="IconAtlas.GetUvForRect"/> turns the rectangle into UVs once a page is uploaded.
/// </summary>
public sealed class AtlasIndex
{
    /// <summary>The only index format this reads.</summary>
    public const int SupportedVersion = 1;

    private readonly Dictionary<string, (int Page, int Col, int Row)> _items;
    private readonly string[] _pages;

    private AtlasIndex(int cellW, int cellH, int pageW, int pageH, string[] pages, Dictionary<string, (int, int, int)> items)
    {
        CellW = cellW; CellH = cellH; PageW = pageW; PageH = pageH; _pages = pages; _items = items;
    }

    /// <summary>An index with no pictures.</summary>
    public static readonly AtlasIndex Empty = new(1, 1, 1, 1, System.Array.Empty<string>(), new Dictionary<string, (int, int, int)>());

    public int CellW { get; }
    public int CellH { get; }
    public int PageW { get; }
    public int PageH { get; }

    /// <summary>The page file names, in page-number order.</summary>
    public IReadOnlyList<string> Pages => _pages;

    /// <summary>How many names the index holds.</summary>
    public int Count => _items.Count;

    /// <summary>Cells across one page.</summary>
    public int Columns => PageW / CellW;

    /// <summary>Cells down one page.</summary>
    public int Rows => PageH / CellH;

    /// <summary>The names, in no promised order.</summary>
    public IEnumerable<string> Names => _items.Keys;

    public bool Contains(string name) => name is not null && _items.ContainsKey(name);

    /// <summary>The page number and the pixel rectangle (x, y, w, h) of <paramref name="name"/>'s cell; false when the name is not in the index.</summary>
    public bool TryGet(string name, out int page, out int x, out int y, out int w, out int h)
    {
        page = x = y = w = h = 0;
        if (name is null || !_items.TryGetValue(name, out var it)) return false;
        page = it.Page; x = it.Col * CellW; y = it.Row * CellH; w = CellW; h = CellH;
        return true;
    }

    /// <summary>Parses an index; throws <see cref="FormatException"/> naming what is wrong (bad JSON, an unknown version, missing or non-positive sizes, a cell that does not fit its page, a page or cell out of range, two names in one cell).</summary>
    public static AtlasIndex Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new FormatException("atlas index: " + e.Message); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("atlas index: the root is not an object.");
            if (!root.TryGetProperty("version", out var v) || !v.TryGetInt32(out int version) || version != SupportedVersion)
                throw new FormatException($"atlas index: version must be {SupportedVersion}.");
            var (cw, ch) = Pair(root, "cell");
            var (pw, ph) = Pair(root, "page");
            if (cw <= 0 || ch <= 0 || pw < cw || ph < ch) throw new FormatException("atlas index: the cell size must be positive and fit in the page.");
            int cols = pw / cw, rows = ph / ch;

            if (!root.TryGetProperty("pages", out var pagesEl) || pagesEl.ValueKind != JsonValueKind.Array) throw new FormatException("atlas index: 'pages' is missing.");
            var pages = new List<string>();
            foreach (var p in pagesEl.EnumerateArray())
            {
                string? name = p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) throw new FormatException("atlas index: a page names no file.");
                pages.Add(name);
            }

            var items = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
            var taken = new HashSet<(int, int, int)>();
            if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Object) throw new FormatException("atlas index: 'items' is missing.");
            foreach (var prop in itemsEl.EnumerateObject())
            {
                var a = prop.Value;
                if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 3
                    || !a[0].TryGetInt32(out int page) || !a[1].TryGetInt32(out int col) || !a[2].TryGetInt32(out int row))
                    throw new FormatException($"atlas index: '{prop.Name}' is not [page, col, row].");
                if (page < 0 || page >= pages.Count) throw new FormatException($"atlas index: '{prop.Name}' names page {page}, which does not exist.");
                if (col < 0 || col >= cols || row < 0 || row >= rows) throw new FormatException($"atlas index: '{prop.Name}' is outside the page ({col}, {row}).");
                if (!taken.Add((page, col, row))) throw new FormatException($"atlas index: two names share cell ({page}, {col}, {row}).");
                items[prop.Name] = (page, col, row);
            }
            return new AtlasIndex(cw, ch, pw, ph, pages.ToArray(), items);
        }
    }

    private static (int, int) Pair(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 2
            || !a[0].TryGetInt32(out int x) || !a[1].TryGetInt32(out int y))
            throw new FormatException($"atlas index: '{key}' is not [w, h].");
        return (x, y);
    }
}
