using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐ <c>CE-4623</c> AB-1 (docs/designs/animals/DESIGN_Animals.md §6.1) — an animal type (<c>Gen.Animal</c>) gets its <see cref="AnimalState"/>
    /// on EVERY node at spawn (its species, calm), and a flock its <see cref="FlockState"/>; the other animal components are registered here so a
    /// brain or a populating area can add them. ⭐ <see cref="Animals.IsAnimal"/> asks the one component — one lookup per question.
    /// </summary>
    public sealed class AnimalTkbTranslator : ITkbEntityTranslator
    {
        /// <summary>A flock's birds when nothing set its count (an area or the author may).</summary>
        public const ushort DefaultFlockCount = 40;

        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(AnimalTemperamentDto); }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(AnimalState);
            yield return typeof(Fear);
            yield return typeof(FlockState);
            yield return typeof(HerdMember);
            yield return typeof(AnimalHome);
            yield return typeof(HerdState);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<AnimalTemperamentDto>();
            if (dto == null) return;
            if (repo.IsComponentTypeRegistered<AnimalState>() && !repo.HasComponent<AnimalState>(entity))
                repo.AddComponent(entity, new AnimalState { Species = (AnimalSpecies)dto.Species, Mode = AnimalMode.Calm });
            // ⭐ AB-3 — its fear (kept where its brain runs; harmless elsewhere); the flight-zone looks staggered by entity so they spread over frames
            if (repo.IsComponentTypeRegistered<Fear>() && !repo.HasComponent<Fear>(entity))
                repo.AddComponent(entity, new Fear { NextZoneLookAt = (entity.Index % 10) * (AnimalFear.ZoneLookSeconds / 10f) });
            if ((AnimalSpecies)dto.Species == AnimalSpecies.BirdFlock && repo.IsComponentTypeRegistered<FlockState>() && !repo.HasComponent<FlockState>(entity))
                repo.AddComponent(entity, new FlockState { Count = DefaultFlockCount, InitialCount = DefaultFlockCount, Seed = (uint)template.TkbType, Mode = FlockMode.Perched });
        }
    }

    /// <summary>⭐ <c>CE-4623</c> AB-1 — THE question "is this an animal?" (AR-19): it carries <see cref="AnimalState"/>. One component lookup; no class walk.</summary>
    public static class Animals
    {
        public static bool IsAnimal(ISimulationView view, Entity e)
            => view.IsAlive(e) && view.HasComponent<AnimalState>(e);

        /// <summary>An animal attacking right now (<see cref="AnimalFlags.Dangerous"/>) — self-defence covers firing at it (AR-6).</summary>
        public static bool IsDangerous(ISimulationView view, Entity e)
            => IsAnimal(view, e) && (view.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.Dangerous) != 0;

        /// <summary>By DIS alone (a template, no entity): a life form in the animal categories 200…209.</summary>
        public static bool IsAnimalDis(DISEntityType dis) => dis.Kind == 3 && dis.Category >= 200 && dis.Category <= 209;
    }
}
