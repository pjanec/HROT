using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Fdp.Presentation.Forms;
using Fdp.Presentation.Fonts;
using System.Numerics;

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
        public readonly Dictionary<ulong, (long Key, FixedString64 Text)> StrengthBar = new();
        public readonly Dictionary<ulong, (long Key, FixedString64 Text)> AmmoBar = new();
        public readonly Dictionary<ulong, (long Key, FixedString64 Text)> Vehicles = new();
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

    // ── ⭐ CE-8606 (UI-10, mockup ①) — the GAUGES: the same numbers, drawn as a bar in the SITREP colour + a status chip ──
    public static readonly Vector4 GreenColour = new(0.357f, 0.816f, 0.416f, 1f), AmberColour = new(0.949f, 0.753f, 0.200f, 1f),
                                   RedColour = new(0.902f, 0.333f, 0.333f, 1f), BlackColour = new(0.62f, 0.62f, 0.66f, 1f), VehicleColour = new(0.243f, 0.776f, 0.690f, 1f);
    private static readonly FixedString32 GreenWord = new("GREEN"), AmberWord = new("AMBER"), RedWord = new("RED"), BlackWord = new("BLACK"), UnknownWord = new("?");

    /// <summary>The SITREP colour as the chip: its word, its colour, its glyph (never colour alone).</summary>
    public static (FixedString32 Word, Vector4 Colour, string Glyph) Chip(StatusColour c) => c switch
    {
        StatusColour.Green => (GreenWord, GreenColour, IconsFontAwesome6.CircleCheck),
        StatusColour.Amber => (AmberWord, AmberColour, IconsFontAwesome6.TriangleExclamation),
        StatusColour.Red => (RedWord, RedColour, IconsFontAwesome6.TriangleExclamation),
        StatusColour.Black => (BlackWord, BlackColour, IconsFontAwesome6.CircleXmark),
        _ => (UnknownWord, BlackColour, IconsFontAwesome6.CircleXmark),
    };

    /// <summary>The Strength gauge: present / authorized men as a bar in the SITREP colour, ticks at the AMBER (70 %) and GREEN (90 %) thresholds.</summary>
    public static FormGauge StrengthGauge(EntityRepository w, Entity e)
    {
        var s = UnitStrength.Of(w, e);
        var colour = UnitStrength.ColourOf(w, e);
        var (word, col, glyph) = Chip(colour);
        long key = ((long)(s.Present * 10f) << 24) ^ ((long)s.Authorized << 4);
        var cache = CacheOf(w).StrengthBar;
        if (!cache.TryGetValue(e.PackedValue, out var c) || c.Key != key)
            cache[e.PackedValue] = c = (key, new FixedString64(string.Create(CultureInfo.InvariantCulture, $"{(int)s.Present} / {s.Authorized} men")));
        return new FormGauge { Fraction = s.Fraction, Fill = col, TickA = StaffComposerSystem.AmberFraction, TickB = StaffComposerSystem.GreenFraction,
                               Text = c.Text, Chip = word, ChipColour = col, ChipGlyph = glyph };
    }

    /// <summary>The Ammunition gauge: the men's rounds over their capacity, ticks at 25 % / 50 % (the staff's thresholds), "62 %  ·  1 860 rds".</summary>
    public static FormGauge AmmoGauge(EntityRepository w, Entity e)
    {
        long have = StaffComposerSystem.RoundsOf(w, e, out long max);
        float f = max > 0 ? (float)have / max : 0f;
        var (word, col, glyph) = Chip(StaffComposerSystem.AmmoOf(w, e));
        long key = (have << 20) ^ max;
        var cache = CacheOf(w).AmmoBar;
        if (!cache.TryGetValue(e.PackedValue, out var c) || c.Key != key)
            cache[e.PackedValue] = c = (key, new FixedString64(string.Create(CultureInfo.InvariantCulture, $"{(int)(f * 100f)} %  ·  {have:#,0} rds").Replace(',', ' ')));
        return new FormGauge { Fraction = f, Fill = col, TickA = StaffComposerSystem.AmmoAmberFraction, TickB = StaffComposerSystem.AmmoGreenFraction,
                               Text = c.Text, Chip = word, ChipColour = col, ChipGlyph = glyph };
    }

    /// <summary>A unit holds vehicles (a stochastic unit's <c>UnitHoldings</c>).</summary>
    public static bool HasVehicles(EntityRepository w, Entity e)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Aggregate.UnitHoldings>() || !w.HasComponent<Fdp.Toolkit.Aggregate.UnitHoldings>(e)) return false;
        ref readonly var h = ref w.GetComponentRO<Fdp.Toolkit.Aggregate.UnitHoldings>(e);
        for (int i = 0; i < h.Count; i++) if (h.Items[i].Kind == Fdp.Toolkit.Aggregate.HoldingKind.Vehicle && h.Items[i].Count > 0) return true;
        return false;
    }

    private static (int Count, int Operational) VehicleCount(EntityRepository w, Entity e)
    {
        ref readonly var h = ref w.GetComponentRO<Fdp.Toolkit.Aggregate.UnitHoldings>(e);
        int n = 0, ok = 0;
        for (int i = 0; i < h.Count; i++)
            if (h.Items[i].Kind == Fdp.Toolkit.Aggregate.HoldingKind.Vehicle) { n += h.Items[i].Count; ok += h.Items[i].Operational; }
        return (n, ok);
    }

    /// <summary>"3 / 4 operational" — the row's text (the rows and the rails read it).</summary>
    public static FixedString64 VehiclesText(EntityRepository w, Entity e)
    {
        if (!HasVehicles(w, e)) return default;
        var (n, ok) = VehicleCount(w, e);
        long key = ((long)n << 16) ^ ok;
        var cache = CacheOf(w).Vehicles;
        if (!cache.TryGetValue(e.PackedValue, out var c) || c.Key != key)
            cache[e.PackedValue] = c = (key, new FixedString64(string.Create(CultureInfo.InvariantCulture, $"{ok} / {n} operational")));
        return c.Text;
    }

    /// <summary>The Vehicles gauge: one icon per vehicle (at most 12), the operational ones teal, the lost ones red and crossed.</summary>
    public static FormGauge VehiclesGauge(EntityRepository w, Entity e)
    {
        if (!HasVehicles(w, e)) return default;
        var (n, ok) = VehicleCount(w, e);
        return new FormGauge { Icons = System.Math.Min(n, 12), IconsOk = System.Math.Min(ok, 12), IconGlyph = IconsFontAwesome6.VanShuttle, Fill = VehicleColour, Text = VehiclesText(w, e) };
    }
}
