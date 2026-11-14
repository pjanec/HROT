using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ U-10 / M1 (docs/UX/DESIGN_Show_On_Map.md §2) — THE type table of "this field has a place on the map": the TYPE decides, never a panel
/// (R-174). A point (<see cref="WorldPoint"/>, <see cref="PointLink"/>) is a ring + its label; an entity (<see cref="EntityRef"/>, <see cref="EntityLink"/>)
/// a ring at its position; an area (<see cref="AreaLink"/>) its dashed outline (an area ENTITY: a ring at the entity). One colour, teal (Q-M4).
/// </summary>
public static class MapPreviews
{
    /// <summary>Teal (<c>IconPalette.Teal</c>, Colour Icons): everything shown from a form.</summary>
    public static readonly Rgba32 Colour = new(64, 199, 191);

    /// <summary>The layer the preview draws on: the always-on tool-handles bit — never filmed (<c>DebugGizmoLayer.InFilm</c>), never a checkbox.</summary>
    public const byte Layer = DebugTraceLayers.ToolHandles;

    /// <summary>Whether a field of <paramref name="type"/> has a place on the map (so its row draws the ⌖).</summary>
    public static bool Has(Type type)
        => type == typeof(WorldPoint) || type == typeof(PointLink) || type == typeof(EntityRef) || type == typeof(EntityLink) || type == typeof(AreaLink);

    /// <summary>
    /// ⭐ W-1 / P-8 8d (Mission plan E2) — WHERE a value with a place is now: a point, an entity's position, an area's centre; null when it stands for no
    /// place (an unresolved link, none). The same resolution <see cref="Draw"/> uses — one table of "this type has a place".
    /// </summary>
    public static Vector3? PlaceOf(object? value, LiveRefScope? refs)
    {
        if (value is AreaLink a && a.IsShape) { var c = a.Center; return new Vector3(c.X, c.Y, c.Z); }
        var (entity, point) = ReferenceLeafDrawers.ShowTarget(value, refs);
        if (point is null && !entity.IsNone && refs != null && refs.TryPositionOf(entity, out var at)) point = at;
        return point is { } p ? new Vector3(p.X, p.Y, p.Z) : null;
    }

    /// <summary>Draws <paramref name="value"/> into <paramref name="draw"/>; <c>false</c> when it stands for no place now (an unresolved link, none).</summary>
    public static bool Draw(DebugPrimitiveBuffer draw, object? value, LiveRefScope? refs, in Fdp.Core.FixedString32 label)
    {
        if (value is AreaLink a && a.IsShape)
        {
            var c = a.Center;
            if (a.Spec.Effective == RefForm.Circle)
                draw.DrawSphere(new Vector3(c.X, c.Y, c.Z), a.Size.X, Colour, thickness: 2f, sizeMode: SizeMode.WorldMeters, layer: Layer, style: LineStyle.Dashed);
            else
            {
                float rad = a.Heading.Degrees * (MathF.PI / 180f);
                var unit = new Vector2(MathF.Sin(rad), MathF.Cos(rad));   // compass, as AreaLink.Contains
                var along = unit * (a.Size.Y * 0.5f);
                var across = new Vector2(unit.Y, -unit.X) * (a.Size.X * 0.5f);
                var o = new Vector2(c.X, c.Y);
                Edge(draw, o + along + across, o + along - across, c.Z); Edge(draw, o + along - across, o - along - across, c.Z);
                Edge(draw, o - along - across, o - along + across, c.Z); Edge(draw, o - along + across, o + along + across, c.Z);
            }
            draw.DrawText(c.X, c.Y, label, Colour, layer: Layer);
            return true;
        }
        var (entity, point) = ReferenceLeafDrawers.ShowTarget(value, refs);
        if (point is null && !entity.IsNone && refs != null && refs.TryPositionOf(entity, out var at)) point = at;
        if (point is not { } p) return false;
        draw.DrawSphere(new Vector3(p.X, p.Y, p.Z), 9f, Colour, thickness: 2f, sizeMode: SizeMode.ScreenPixels, layer: Layer);
        draw.DrawText(p.X, p.Y, label, Colour, layer: Layer, lineOffsetPx: 14f);
        return true;
    }

    private static void Edge(DebugPrimitiveBuffer draw, Vector2 a, Vector2 b, float z)
        => draw.DrawLine(new Vector3(a, z), new Vector3(b, z), Colour, thickness: 2f, layer: Layer, style: LineStyle.Dashed);
}

/// <summary>
/// ⭐ U-10 / M1 (DESIGN_Show_On_Map §2) — ONE per map host: what forms ask the map to show. <b>Hover = preview</b> (this frame only), <b>click = pin</b>
/// (Q-M2). A pin lives while its row is drawn — the row re-submits it each frame through <see cref="Keep"/> — so a selection change (the row is gone)
/// clears it by construction. The map's <see cref="MapPreviewLayer"/> writes it into the host's LOCAL primitive buffer at draw time: never on the wire,
/// never recorded, never filmed. ⛔ No allocation on a steady frame.
/// </summary>
public sealed class MapPreview
{
    private readonly record struct Key(object Owner, string Path);
    private struct Pin { public object? Value; public Fdp.Core.FixedString32 Label; public int Kept; }

    private readonly Dictionary<Key, Pin> _pins = new();
    private readonly List<Key> _dead = new();
    private object? _hover;
    private Fdp.Core.FixedString32 _hoverLabel;
    private bool _hovered;
    private int _frame;

    /// <summary>How many pins are held (rails).</summary>
    public int PinCount => _pins.Count;

    /// <summary>A row is hovered: show <paramref name="value"/> this frame.</summary>
    public void Hover(object value, in Fdp.Core.FixedString32 label) { _hover = value; _hoverLabel = label; _hovered = true; }

    public bool IsPinned(object owner, string path) => _pins.ContainsKey(new Key(owner, path));

    /// <summary>Click on the ⌖: pin, or unpin a pinned row.</summary>
    public void TogglePin(object owner, string path, object value, in Fdp.Core.FixedString32 label)
    {
        var k = new Key(owner, path);
        if (!_pins.Remove(k)) _pins[k] = new Pin { Value = value, Label = label, Kept = _frame };
    }

    /// <summary>A pinned row is drawn this frame: keep its pin, with its current value.</summary>
    public void Keep(object owner, string path, object value)
    {
        var k = new Key(owner, path);
        if (_pins.TryGetValue(k, out var p)) { p.Value = value; p.Kept = _frame; _pins[k] = p; }
    }

    public void ClearPins() => _pins.Clear();

    /// <summary>The map draws: the hover, then every pin whose row was drawn since the last emit; a pin whose row is gone is dropped.</summary>
    public void Emit(DebugPrimitiveBuffer draw, LiveRefScope? refs)
    {
        if (_hovered) MapPreviews.Draw(draw, _hover, refs, in _hoverLabel);
        _dead.Clear();
        foreach (var kv in _pins)
        {
            if (kv.Value.Kept < _frame - 1) { _dead.Add(kv.Key); continue; }
            var label = kv.Value.Label;
            MapPreviews.Draw(draw, kv.Value.Value, refs, in label);
        }
        for (int i = 0; i < _dead.Count; i++) _pins.Remove(_dead[i]);
        _hovered = false; _hover = null;
        _frame++;
    }
}
