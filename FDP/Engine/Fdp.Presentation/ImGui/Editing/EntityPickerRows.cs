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

    private readonly List<EntityCandidate> _rows = new();
    private int _generation = int.MinValue;
    private WorldPoint _from;
    private bool _hasFrom;

    /// <summary>The rows' version (the field rebuilds its filter when it moves).</summary>
    public int Version { get; private set; }

    /// <summary>The cached rows of <paramref name="refs"/>.</summary>
    public static EntityPickerRows Of(LiveRefScope refs) => ByScope.GetValue(refs, _ => new EntityPickerRows());

    /// <summary>The rows, rebuilt when the scope rebuilt or <paramref name="from"/> moved more than a metre.</summary>
    public IReadOnlyList<EntityCandidate> Rows(LiveRefScope refs, WorldPoint? from)
    {
        int gen = refs.Generation;
        bool fromMoved = from.HasValue != _hasFrom || from is { } f && Vector3.DistanceSquared(f.AsVector3, _from.AsVector3) > 1f;
        if (gen == _generation && !fromMoved) return _rows;
        _generation = gen; _hasFrom = from.HasValue; _from = from ?? default;
        _rows.Clear();
        var ids = refs.NamedIds();
        var thing = NodeEditor.Core.Widgets.Icons.TryGet("thing/entity", out var g) ? g : new IconGlyph("", IconPalette.Grey);
        for (int i = 0; i < ids.Count; i++)
        {
            var r = new EntityRef(ids[i]);
            var kind = refs.TryKindOf(r, out var k) && k.IconKey.Length > 0 && NodeEditor.Core.Widgets.Icons.TryGet(k.IconKey, out var kg) ? kg : thing;
            var side = refs.TrySideOf(r, out var s) ? s : ForceId.Neutral;
            string distance = _hasFrom && refs.TryPositionOf(r, out var at) ? DistanceText(Vector3.Distance(at.AsVector3, _from.AsVector3)) : "";
            _rows.Add(new EntityCandidate(refs.ChipLabelOf(r), SideColor(side), kind, side.ToString(), distance, r.NetworkId));
        }
        Version++;
        return _rows;
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
