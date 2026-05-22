using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodeEditor.Core.Bookmarks;

/// <summary>
/// Per-asset bookmark collection. Persisted as JSON in editor session state.
/// Supports 9 hotkey slots (1-9) plus unbound bookmarks (slot 0).
/// </summary>
public sealed class BookmarkStore
{
    private readonly Dictionary<string, Bookmark> _all     = new();
    private readonly Dictionary<int, string>      _slotToId = new();

    /// <summary>All bookmarks, bound and unbound.</summary>
    public IReadOnlyCollection<Bookmark> All => _all.Values;

    /// <summary>Returns the bookmark in slot 1-9, or null if the slot is empty.</summary>
    public Bookmark? GetSlot(int slot) =>
        _slotToId.TryGetValue(slot, out var id) && _all.TryGetValue(id, out var b) ? b : null;

    /// <summary>
    /// Add or replace a bookmark in the given slot.
    /// If <paramref name="slot"/> is in [1,9] and already occupied, the previous
    /// occupant's slot number is reset to 0 (unbound) but not removed.
    /// </summary>
    public void SetSlot(int slot, Bookmark bookmark)
    {
        // Evict previous occupant of the slot
        if (slot is >= 1 and <= 9 && _slotToId.TryGetValue(slot, out var prevId))
        {
            if (_all.TryGetValue(prevId, out var prev))
                _all[prevId] = prev with { SlotNumber = 0 };
            _slotToId.Remove(slot);
        }

        // Remove any other slot that this bookmark previously occupied
        if (_all.TryGetValue(bookmark.BookmarkId, out var existing) && existing.SlotNumber is >= 1 and <= 9)
            _slotToId.Remove(existing.SlotNumber);

        var withSlot = bookmark with { SlotNumber = slot };
        _all[bookmark.BookmarkId] = withSlot;
        if (slot is >= 1 and <= 9)
            _slotToId[slot] = bookmark.BookmarkId;
    }

    /// <summary>Remove a bookmark by id. Returns true if it existed.</summary>
    public bool Remove(string bookmarkId)
    {
        if (!_all.TryGetValue(bookmarkId, out var b)) return false;
        _all.Remove(bookmarkId);
        if (b.SlotNumber is >= 1 and <= 9)
            _slotToId.Remove(b.SlotNumber);
        return true;
    }

    /// <summary>Remove bookmarks whose target graph no longer exists.</summary>
    public int PurgeOrphans(IReadOnlyCollection<GraphId> validGraphIds)
    {
        var validSet = new HashSet<GraphId>(validGraphIds);
        var orphans  = _all.Values.Where(b => !validSet.Contains(b.TargetGraph)).Select(b => b.BookmarkId).ToList();
        foreach (var id in orphans) Remove(id);
        return orphans.Count;
    }

    /// <summary>Serialize to JSON for session-state persistence.</summary>
    public string ToJson()
    {
        var list = _all.Values.Select(b => new BookmarkDto
        {
            BookmarkId   = b.BookmarkId,
            TargetGraph  = b.TargetGraph.Value.ToString(),
            Label        = b.Label,
            PanX         = b.ViewportPan.X,
            PanY         = b.ViewportPan.Y,
            Zoom         = b.ViewportZoom,
            SlotNumber   = b.SlotNumber,
            CreatedAt    = b.CreatedAt.ToString("O"),
        }).ToList();
        return JsonSerializer.Serialize(list);
    }

    /// <summary>Load from JSON; replaces existing contents.</summary>
    public static BookmarkStore FromJson(string json)
    {
        var store = new BookmarkStore();
        var list  = JsonSerializer.Deserialize<List<BookmarkDto>>(json);
        if (list is null) return store;
        foreach (var dto in list)
        {
            if (!Guid.TryParse(dto.TargetGraph, out var graphGuid)) continue;
            var b = new Bookmark(
                dto.BookmarkId,
                new GraphId(graphGuid),
                dto.Label,
                new System.Numerics.Vector2(dto.PanX, dto.PanY),
                dto.Zoom,
                dto.SlotNumber,
                DateTime.TryParse(dto.CreatedAt, out var dt) ? dt : DateTime.UtcNow);
            store.SetSlot(b.SlotNumber, b);
        }
        return store;
    }

    // ── private DTO ──────────────────────────────────────────────────────────

    private sealed class BookmarkDto
    {
        public string BookmarkId  { get; set; } = "";
        public string TargetGraph { get; set; } = "";
        public string Label       { get; set; } = "";
        public float  PanX        { get; set; }
        public float  PanY        { get; set; }
        public float  Zoom        { get; set; }
        public int    SlotNumber  { get; set; }
        public string CreatedAt   { get; set; } = "";
    }
}
