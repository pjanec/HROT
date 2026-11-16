using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Replication.Edits;

namespace Hrot.UI.Common.AddEntity;

/// <summary>⭐ CE-4623 (AU-7) — the animal-area form's WRITES on backend's <see cref="AnimalArea"/> (TKB 712, registered in <c>HrotSharedComponentRegistry</c>): species, count, seed, Enclosed — each ONE edit step (the editor's history snapshots the world round the bracket), like <see cref="TerrainBlockOps"/>. ✅ Bound to backend's component (the stand-in <c>AnimalAreaDef</c>, temporary id 511, is gone).</summary>
public static class AnimalAreaOps
{
    /// <summary>The defaults of a fresh area of <paramref name="species"/>: 12 animals, seed 1, Enclosed on for pigs and goats (AN-18), off for the rest.</summary>
    public static AnimalArea Default(AnimalSpecies species) => new() { Species = species, Count = 12, Seed = 1, Enclosed = EnclosedByDefault(species) ? (byte)1 : (byte)0 };

    /// <summary>The most animals one area holds.</summary>
    public const int MaxCount = 200;

    /// <summary>The species an area can be filled with: the DOMESTIC ones (AN-16 / AN-17) — wild ones (birds, a flock) and the guard dog / cat have their own places.</summary>
    public static readonly AnimalSpecies[] Species = [AnimalSpecies.Cattle, AnimalSpecies.Sheep, AnimalSpecies.HorseHerd, AnimalSpecies.Hen, AnimalSpecies.Pig, AnimalSpecies.Goat];

    /// <summary>The picker's name of a species.</summary>
    public static string NameOf(AnimalSpecies s) => s switch { AnimalSpecies.HorseHerd => "Horses", AnimalSpecies.Hen => "Hens", AnimalSpecies.Cattle => "Cattle", AnimalSpecies.Sheep => "Sheep", AnimalSpecies.Pig => "Pigs", AnimalSpecies.Goat => "Goats", _ => s.ToString() };

    /// <summary>AN-18 — pigs' and goats' areas default to Enclosed (a pen they cannot breach); cattle, horses, sheep and hens to open.</summary>
    public static bool EnclosedByDefault(AnimalSpecies s) => s is AnimalSpecies.Pig or AnimalSpecies.Goat;

    private static bool Is(EntityRepository w, Entity e) => w.IsAlive(e) && w.IsComponentTypeRegistered<AnimalArea>() && w.HasComponent<AnimalArea>(e);

    private static bool Edit(EntityRepository w, Entity e, string label, Func<AnimalArea, AnimalArea> change)
    {
        if (!Is(w, e)) return false;
        var before = w.GetComponentRO<AnimalArea>(e);
        var after = change(before);
        if (after.Species == before.Species && after.Count == before.Count && after.Seed == before.Seed && after.Enclosed == before.Enclosed) return false;
        EditSteps.Begin(w, label);
        try { w.GetComponentRW<AnimalArea>(e) = after; }
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
