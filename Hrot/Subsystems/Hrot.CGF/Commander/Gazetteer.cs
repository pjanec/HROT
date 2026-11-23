using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §3, §9 ⑤) — the scenario's place names ↔ positions, because a small model reasons badly about coordinates.
/// The places are the scenario's named <b>Point</b> map graphics (TKB 8816) and <b>tactical areas</b> (TKB 8803, at their centre) — they exist; this only
/// reads them — plus a coarse <see cref="GridMetres"/> grid. A place is said as a name (<c>"the house"</c>), <c>"N m &lt;dir&gt; of &lt;name&gt;"</c>, or
/// <c>"grid E2 N3"</c>. World axes: X east, Y north.
/// </summary>
public sealed class Gazetteer
{
    public const int PointType = 8816, AreaType = 8803;
    public const float GridMetres = 100f;

    private readonly List<(string Name, Vector2 At)> _places = new();

    /// <summary>The places, in the scenario's order (the model's prefix lists them).</summary>
    public IReadOnlyList<(string Name, Vector2 At)> Places => _places;

    private readonly Dictionary<string, string> _notes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>⭐ CE-4680 B-6 — the place's one-line terrain note (<see cref="PlaceNote"/>), or null.</summary>
    public string? NoteOf(string name) => _notes.TryGetValue(name, out var n) ? n : null;

    /// <summary>
    /// Reads the named points and areas of <paramref name="world"/> (on a seat's start — they do not move) that <paramref name="side"/> may know: a
    /// <c>Neutral</c> place (the default) is everybody's; a place drawn for a side (its <see cref="EntityInfo.ForceId"/>) is only that side's.
    /// ⭐ The duel (§8): a Red seat was told Blue's start, objective and support position with Blue's notes. Null = every place (a rail).
    /// </summary>
    public static Gazetteer Of(EntityRepository world, ForceId? side = null)
    {
        var g = new Gazetteer();
        foreach (var e in world.Query().With<TkbIdentity>().With<EntityInfo>().With<SimTransform>().Build())
        {
            long type = (long)world.GetComponentRO<TkbIdentity>(e).TkbType;
            if (type != PointType && type != AreaType) continue;
            var info = world.GetComponentRO<EntityInfo>(e);
            if (side is { } own && info.ForceId != ForceId.Neutral && info.ForceId != own) continue;   // the other side's control measure
            string name = info.Name.ToString().Trim();
            if (name.Length == 0) continue;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            var p = new Vector2(at.X, at.Y);
            if (type == AreaType && world.HasManagedComponent<EditablePolyline>(e)
                && ((ISimulationView)world).GetManagedComponentRO<EditablePolyline>(e) is { Points: { Count: > 0 } pts })
            {
                var sum = Vector2.Zero;
                foreach (var q in pts) sum += new Vector2(q.X, q.Y);
                p += sum / pts.Count;   // the points are offsets from the area's own position
            }
            g.Add(name, p);
            if (world.IsComponentTypeRegistered<PlaceNote>() && world.HasComponent<PlaceNote>(e)
                && world.GetComponentRO<PlaceNote>(e).Text.ToString().Trim() is { Length: > 0 } note)
                g._notes[name] = note;
        }
        return g;
    }

    public void Add(string name, Vector2 at) => _places.Add((name, at));

    private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>
    /// A place said by a person or a model ⇒ a position. Null with <paramref name="why"/> when it does not resolve (the action is refused with it).
    /// </summary>
    public Vector2? Resolve(string? where, out string? why)
    {
        why = null;
        if (string.IsNullOrWhiteSpace(where)) { why = "no place given"; return null; }
        string text = where.Trim();
        if (Named(text) is { } exact) return exact;

        // "N m <dir> of <name>"
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 5 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float metres)
            && parts[1] is "m" or "metres" or "meters" && parts[3].Equals("of", StringComparison.OrdinalIgnoreCase))
        {
            int dir = Array.FindIndex(Directions, d => d.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
            if (dir < 0) { why = $"'{parts[2]}' is not a direction (N NE E SE S SW W NW)"; return null; }
            string of = string.Join(' ', parts, 4, parts.Length - 4);
            if (Named(of) is not { } origin) { why = $"no place named '{of}' — known: {KnownNames()}"; return null; }
            double bearing = dir * Math.PI / 4d;   // 0 = north, clockwise
            return origin + new Vector2((float)(Math.Sin(bearing) * metres), (float)(Math.Cos(bearing) * metres));
        }

        // "grid E<i> N<j>" — the cell's centre
        if (parts.Length == 3 && parts[0].Equals("grid", StringComparison.OrdinalIgnoreCase)
            && TryCell(parts[1], 'E', out int ex) && TryCell(parts[2], 'N', out int ny))
            return new Vector2((ex + 0.5f) * GridMetres, (ny + 0.5f) * GridMetres);

        why = $"no place named '{text}' — known: {KnownNames()} (or \"N m <dir> of <name>\", or \"grid E<i> N<j>\")";
        return null;
    }

    /// <summary>A position ⇒ how the radio says it: the nearest place within 400 m as "N m &lt;dir&gt; of &lt;name&gt;" (or the name itself when within 15 m), else the grid cell.</summary>
    public string Describe(Vector2 at)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < _places.Count; i++)
        {
            float d = Vector2.Distance(at, _places[i].At);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best >= 0 && bestD <= 15f) return _places[best].Name;
        if (best >= 0 && bestD <= 400f)
        {
            var v = at - _places[best].At;
            double deg = (Math.Atan2(v.X, v.Y) * 180d / Math.PI + 360d) % 360d;
            string dir = Directions[(int)Math.Round(deg / 45d) % 8];
            int metres = (int)(Math.Round(bestD / 10d) * 10d);
            return string.Create(CultureInfo.InvariantCulture, $"{metres} m {dir} of {_places[best].Name}");
        }
        return string.Create(CultureInfo.InvariantCulture, $"grid E{(int)Math.Floor(at.X / GridMetres)} N{(int)Math.Floor(at.Y / GridMetres)}");
    }

    public string KnownNames() => _places.Count == 0 ? "(none)" : string.Join(", ", _places.ConvertAll(p => p.Name));

    private Vector2? Named(string name)
    {
        string n = Strip(name);
        foreach (var p in _places)
            if (Strip(p.Name).Equals(n, StringComparison.OrdinalIgnoreCase)) return p.At;
        return null;
    }

    private static string Strip(string s)
    {
        s = s.Trim();
        return s.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? s[4..].Trim() : s;
    }

    private static bool TryCell(string s, char axis, out int i)
    {
        i = 0;
        return s.Length > 1 && char.ToUpperInvariant(s[0]) == axis && int.TryParse(s.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
    }
}
