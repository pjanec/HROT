using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ N-5 (📄 docs/DESIGN_Struct_Editor.md §5, CE-3382) — the entity picker's rows from the ONE reference scope (<see cref="LiveRefScope"/>): every named entity as
/// <i>glyph · name · side · distance</i>, by name; the distance from the host's selection (<see cref="IMapPickContext.TryGetSelectionPosition"/>) when it has one.
/// ⭐ Rebuilt only when the scope rebuilt or the selection moved (R-2084: a steady frame allocates nothing) — one instance per scope.
/// </summary>
public sealed class EntityPickerRows
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LiveRefScope, EntityPickerRows> ByScope = new();

    /// <summary>One row list with its own cache state: the base one (every named entity) and one per PRESET ARRAY (a field's <c>[MapPickableEntity]</c>, the attribute's own instance).</summary>
    private sealed class RowSet
    {
        public readonly List<EntityCandidate> Rows = new();
        public int Generation = int.MinValue, Version;
        public WorldPoint From;
        public bool HasFrom;
    }

    private readonly RowSet _all = new();
    private readonly PresetSetCache<RowSet> _byPresets = new();   // one list per DISTINCT preset set (by content — a document rebuild's new arrays share it)

    /// <summary>How many distinct preset sets hold a row list (a rail: it does not grow per document rebuild).</summary>
    public int RowSetCount => _byPresets.Count;

    private RowSet SetOf(string[]? presets)
    {
        if (presets == null || presets.Length == 0) return _all;
        return _byPresets.Of(presets);
    }

    /// <summary>The rows' version (the field rebuilds its filter when it moves) — of the unfiltered list.</summary>
    public int Version => _all.Version;

    /// <summary>⭐ CE-3399 — the version of the list narrowed by <paramref name="presets"/> (the field's own).</summary>
    public int VersionOf(string[]? presets) => SetOf(presets).Version;

    /// <summary>The cached rows of <paramref name="refs"/>.</summary>
    public static EntityPickerRows Of(LiveRefScope refs) => ByScope.GetValue(refs, _ => new EntityPickerRows());

    /// <summary>The rows, rebuilt when the scope rebuilt or <paramref name="from"/> moved more than a metre.</summary>
    public IReadOnlyList<EntityCandidate> Rows(LiveRefScope refs, WorldPoint? from) => Rows(refs, from, null);

    /// <summary>⭐ CE-3399 — the rows narrowed by a field's <c>[MapPickableEntity]</c> <paramref name="presets"/> (a TKB-type preset and / or map layers — the same filter the map pick uses): ONE list per scope and preset set, cached like the full one.</summary>
    public IReadOnlyList<EntityCandidate> Rows(LiveRefScope refs, WorldPoint? from, string[]? presets)
    {
        var set = SetOf(presets);
        int gen = refs.Generation;
        bool fromMoved = from.HasValue != set.HasFrom || from is { } f && Vector3.DistanceSquared(f.AsVector3, set.From.AsVector3) > 1f;
        if (gen == set.Generation && !fromMoved) return set.Rows;
        set.Generation = gen; set.HasFrom = from.HasValue; set.From = from ?? default;
        set.Rows.Clear();
        var ids = refs.NamedIds();
        var thing = NodeEditor.Core.Widgets.Icons.TryGet("thing/entity", out var g) ? g : new IconGlyph("", IconPalette.Grey);
        for (int i = 0; i < ids.Count; i++)
        {
            if (!refs.Admits(ids[i], presets)) continue;
            var r = new EntityRef(ids[i]);
            var kind = refs.TryKindOf(r, out var k) && k.IconKey.Length > 0 && NodeEditor.Core.Widgets.Icons.TryGet(k.IconKey, out var kg) ? kg : thing;
            var side = refs.TrySideOf(r, out var s) ? s : ForceId.Neutral;
            string distance = set.HasFrom && refs.TryPositionOf(r, out var at) ? DistanceText(Vector3.Distance(at.AsVector3, set.From.AsVector3)) : "";
            set.Rows.Add(new EntityCandidate(refs.ChipLabelOf(r), SideColor(side), kind, side.ToString(), distance, r.NetworkId));
        }
        set.Version++;
        return set.Rows;
    }

    /// <summary>"427 m" · "1.2 km".</summary>
    public static string DistanceText(float metres)
        => metres < 1000f ? ((int)MathF.Round(metres)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " m"
                          : (metres / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";

    /// <summary>The side's colour — the <see cref="ForceId"/> documented colours on the kit palette (⚠ one of the side palettes M16 has yet to unify).</summary>
    public static Vector4 SideColor(ForceId side) => side switch
    {
        ForceId.Friend => IconPalette.Blue,
        ForceId.Hostile => IconPalette.Error,
        ForceId.Neutral => IconPalette.Green,
        _ => IconPalette.Amber,
    };
}
