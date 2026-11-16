using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ <c>CE-8601</c> (exp/agg-ui, docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-3) — a UNIT's strength and ammunition for the Unit form, read
/// through the ONE strength seam (<see cref="UnitStrength"/>, the SITREP's) and the staff's ammunition reader (<see cref="StaffComposerSystem.RoundsOf"/> /
/// <see cref="StaffComposerSystem.AmmoOf"/>) — the form and the SITREP agree by construction, and a stochastic unit shows the day <see cref="UnitStrength"/>
/// answers it from its slot table (the behaviors lane's branch), with no change here. Before this a unit's Unit tab showed no strength at all (its Strength row
/// is a man's <c>Health</c>). Cached <see cref="FixedString64"/> texts, rebuilt only when the numbers change ⇒ 0 B on a steady frame.
/// </summary>
public static class UnitFacts
{
    private sealed class Cache
    {
        public readonly Dictionary<ulong, (long Key, FixedString64 Text)> Strength = new();
        public readonly Dictionary<ulong, (long Key, FixedString64 Text)> Ammo = new();
    }
    private static readonly ConditionalWeakTable<EntityRepository, Cache> Caches = new();
    private static Cache CacheOf(EntityRepository w) => Caches.GetValue(w, static _ => new Cache());

    /// <summary>A unit the strength rows apply to: it has a roster (what <see cref="UnitStrength.Of"/> sums).</summary>
    public static bool IsUnit(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<UnitRoster>() && w.HasComponent<UnitRoster>(e);

    /// <summary>"26 / 30 men · Green" — present (whole men, rounded down) of authorized, and the SITREP colour.</summary>
    public static FixedString64 StrengthText(EntityRepository w, Entity e)
    {
        if (!IsUnit(w, e)) return default;
        var s = UnitStrength.Of(w, e);
        if (!s.Known) return default;
        var colour = UnitStrength.ColourOf(w, e);
        long key = ((long)(s.Present * 10f) << 24) ^ ((long)s.Authorized << 4) ^ (long)colour;
        var cache = CacheOf(w).Strength;
        if (cache.TryGetValue(e.PackedValue, out var c) && c.Key == key) return c.Text;
        var text = new FixedString64(string.Create(CultureInfo.InvariantCulture, $"{(int)s.Present} / {s.Authorized} men  ·  {Word(colour)}"));
        cache[e.PackedValue] = (key, text);
        return text;
    }

    /// <summary>"62 %  ·  Amber" — the rounds the unit's men carry over their capacity, and the SITREP's ammunition colour; empty when no man carries a counted weapon.</summary>
    public static FixedString64 AmmoText(EntityRepository w, Entity e)
    {
        if (!IsUnit(w, e)) return default;
        long have = StaffComposerSystem.RoundsOf(w, e, out long max);
        if (max <= 0) return default;
        int pct = (int)(have * 100 / max);
        var colour = StaffComposerSystem.AmmoOf(w, e);
        long key = ((long)pct << 8) ^ (long)colour;
        var cache = CacheOf(w).Ammo;
        if (cache.TryGetValue(e.PackedValue, out var c) && c.Key == key) return c.Text;
        var text = new FixedString64(string.Create(CultureInfo.InvariantCulture, $"{pct} %  ·  {Word(colour)}"));
        cache[e.PackedValue] = (key, text);
        return text;
    }

    /// <summary>The SITREP colour's word (the report's own words, CB-37).</summary>
    public static string Word(StatusColour c) => c switch
    {
        StatusColour.Green => "Green",
        StatusColour.Amber => "Amber",
        StatusColour.Red => "Red",
        StatusColour.Black => "Black",
        _ => "unknown",
    };
}
