using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Replication.Edits;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ CE-4623 (AU-7, DESIGN_Animals §6.1 / AN-16 / AN-18) — <b>STAND-IN</b> for backend's TKB 712 <c>AnimalArea</c> parameters (species, count, seed, Enclosed on the area entity — AB-9) until that component lands.
/// Re-bind = the form and <see cref="AnimalAreaOps"/> read and write backend's component; this struct and <see cref="AnimalAreaOps.AreaType"/> are then deleted.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(511)]   // ⚠ TEMPORARY id — the last free one (506-511 are unallocated; ui's own block 460-475 is exhausted); deleted at the re-bind to backend's real component
[DataPolicy(DataPolicy.Default)]
public struct AnimalAreaDef
{
    public AnimalSpecies Species;
    public byte Enclosed;
    public ushort Count;
    public uint Seed;

    /// <summary>The defaults of a fresh area of <paramref name="species"/>: 12 animals, seed 1, Enclosed on for pigs and goats (AN-18), off for the rest.</summary>
    public static AnimalAreaDef Default(AnimalSpecies species) => new() { Species = species, Count = 12, Seed = 1, Enclosed = AnimalAreaOps.EnclosedByDefault(species) ? (byte)1 : (byte)0 };
}

/// <summary>⭐ CE-4623 (AU-7) — the animal-area form's WRITES: species, count, seed, Enclosed — each ONE edit step (the editor's history snapshots the world round the bracket), like <see cref="TerrainBlockOps"/>.</summary>
public static class AnimalAreaOps
{
    /// <summary>⭐ The fixed TKB type of the area (§4a, 712; backend declares it).</summary>
    public const long AreaType = 712;

    /// <summary>The most animals one area holds.</summary>
    public const int MaxCount = 200;

    /// <summary>The species an area can be filled with: the DOMESTIC ones (AN-16 / AN-17) — wild ones (birds, a flock) and the guard dog / cat have their own places.</summary>
    public static readonly AnimalSpecies[] Species = [AnimalSpecies.Cattle, AnimalSpecies.Sheep, AnimalSpecies.HorseHerd, AnimalSpecies.Hen, AnimalSpecies.Pig, AnimalSpecies.Goat];

    /// <summary>The picker's name of a species.</summary>
    public static string NameOf(AnimalSpecies s) => s switch { AnimalSpecies.HorseHerd => "Horses", AnimalSpecies.Hen => "Hens", AnimalSpecies.Cattle => "Cattle", AnimalSpecies.Sheep => "Sheep", AnimalSpecies.Pig => "Pigs", AnimalSpecies.Goat => "Goats", _ => s.ToString() };

    /// <summary>AN-18 — pigs' and goats' areas default to Enclosed (a pen they cannot breach); cattle, horses, sheep and hens to open.</summary>
    public static bool EnclosedByDefault(AnimalSpecies s) => s is AnimalSpecies.Pig or AnimalSpecies.Goat;

    private static bool Is(EntityRepository w, Entity e) => w.IsAlive(e) && w.IsComponentTypeRegistered<AnimalAreaDef>() && w.HasComponent<AnimalAreaDef>(e);

    private static bool Edit(EntityRepository w, Entity e, string label, Func<AnimalAreaDef, AnimalAreaDef> change)
    {
        if (!Is(w, e)) return false;
        var before = w.GetComponentRO<AnimalAreaDef>(e);
        var after = change(before);
        if (after.Species == before.Species && after.Count == before.Count && after.Seed == before.Seed && after.Enclosed == before.Enclosed) return false;
        EditSteps.Begin(w, label);
        try { w.GetComponentRW<AnimalAreaDef>(e) = after; }
        finally { EditSteps.End(w); }
        return true;
    }

    /// <summary>A new species; the Enclosed option follows the species' default only while the author has left it at the OLD species' default.</summary>
    public static bool SetSpecies(EntityRepository w, Entity e, AnimalSpecies species) => Edit(w, e, "Set animal area species", a =>
        a with { Species = species, Enclosed = (a.Enclosed != 0) == EnclosedByDefault(a.Species) ? (EnclosedByDefault(species) ? (byte)1 : (byte)0) : a.Enclosed });

    /// <summary>Count, kept in 1 … <see cref="MaxCount"/>.</summary>
    public static bool SetCount(EntityRepository w, Entity e, int count) => Edit(w, e, "Set animal area count", a => a with { Count = (ushort)Math.Clamp(count, 1, MaxCount) });

    /// <summary>The seed (a full uint): the same placement every run.</summary>
    public static bool SetSeed(EntityRepository w, Entity e, uint seed) => Edit(w, e, "Set animal area seed", a => a with { Seed = seed });

    /// <summary>The seed re-rolled (a new placement, the same place): a varied but deterministic next seed.</summary>
    public static bool Reroll(EntityRepository w, Entity e) => Edit(w, e, "Re-roll animal area", a => a with { Seed = a.Seed * 1664525u + 1013904223u });

    /// <summary>Enclosed on or off.</summary>
    public static bool SetEnclosed(EntityRepository w, Entity e, bool on) => Edit(w, e, on ? "Enclose animal area" : "Open animal area", a => a with { Enclosed = on ? (byte)1 : (byte)0 });
}
