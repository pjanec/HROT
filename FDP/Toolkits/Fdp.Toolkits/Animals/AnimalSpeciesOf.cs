using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐⭐ <c>CE-4628</c> (docs/designs/animals/DESIGN_Animals.md §10, §10a) — an animal's SPECIES, read from its TKB type: the type's
    /// <see cref="AnimalTemperamentDto"/> carries it (the same descriptor <c>AnimalTkbTranslator</c> reads), so <c>AnimalState</c> no longer keeps a
    /// copy. ⭐ A per-type table, built once per TKB database (the <c>EntityClassTable.Chain</c> pattern): filled on the first ask for a type, a plain
    /// dictionary read after it — 0 B in the steady state, and safe from the parallel phases (a memo of immutable data: every writer stores the same value).
    /// ⛔ Keyed by the database INSTANCE as well as the type id: two databases (a test's, a node's) may give one id two meanings.
    /// </summary>
    public static class AnimalSpeciesOf
    {
        /// <summary>The table's marker for a type with no animal descriptor.</summary>
        private const int NotAnAnimal = -1;

        private static readonly ConditionalWeakTable<ITkbDatabase, ConcurrentDictionary<long, int>> Tables = new();

        /// <summary>The species of <paramref name="e"/>'s TKB type, asking the world's own TKB database; false when the world has none, the entity has no
        /// type, or the type is not an animal's.</summary>
        public static bool TryOf(EntityRepository world, Entity e, out AnimalSpecies species)
        {
            species = default;
            if (world == null || !world.HasSingletonManaged<ITkbDatabase>()) return false;
            return TryOf(world, world.GetSingletonManaged<ITkbDatabase>(), e, out species);
        }

        /// <summary>The same on any view (a replica, a solver snapshot) with the database handed in — what a reader that holds only a view and the live TKB
        /// source (<c>CombatTkb.Live</c>) asks.</summary>
        public static bool TryOf(ISimulationView view, ITkbDatabase? db, Entity e, out AnimalSpecies species)
        {
            species = default;
            if (db == null || e.IsNull || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<TkbIdentity>()) return false;
            if (!view.HasComponent<TkbIdentity>(e)) return false;
            long type = view.GetComponentRO<TkbIdentity>(e).TkbType.Value;
            if (!Tables.TryGetValue(db, out var table)) table = Tables.GetValue(db, static _ => new ConcurrentDictionary<long, int>());
            if (!table.TryGetValue(type, out int found))
            {   // first ask for this type: read its descriptor once
                found = db.TryGetByType(type, out var template) && template?.GetDescriptor<AnimalTemperamentDto>() is { } dto ? dto.Species : NotAnAnimal;
                table[type] = found;
            }
            if (found == NotAnAnimal) return false;
            species = (AnimalSpecies)found;
            return true;
        }
    }
}
