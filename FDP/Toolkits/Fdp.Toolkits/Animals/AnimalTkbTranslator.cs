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

        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(AnimalTemperamentDto); yield return typeof(AnimalAreaDto); }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(AnimalState);
            yield return typeof(Fear);
            yield return typeof(FlockState);
            yield return typeof(HerdMember);
            yield return typeof(AnimalHome);
            yield return typeof(HerdState);
            yield return typeof(Fdp.Toolkit.Civilians.Utterance);   // ⭐ AB-8 — its voice (calls, alarms, the bark)
            yield return typeof(AnimalArea);                         // ⭐ AB-9 — an animal area's species / count / seed / Enclosed
            yield return typeof(AnimalAreaPopulated);
            yield return typeof(Fdp.Toolkit.Navigation.NavigationIntent);   // ⭐ AB-10 — the navigation contract a MoveTo writes (a bird has no ground translator)
            yield return typeof(Fdp.Toolkit.Navigation.NavigationStatus);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            // ⭐ AB-9 — an animal AREA: its defaults, unless the author already set them (a loaded scenario's AnimalArea wins)
            if (template.GetDescriptor<AnimalAreaDto>() is { } area)
            {
                if (repo.IsComponentTypeRegistered<AnimalArea>() && !repo.HasComponent<AnimalArea>(entity))
                    repo.AddComponent(entity, new AnimalArea
                    {
                        Species = (AnimalSpecies)area.Species, Count = (ushort)Math.Clamp(area.Count, 0, ushort.MaxValue),
                        Enclosed = (byte)(area.Enclosed ? 1 : 0), Seed = (uint)entity.Index * 2654435761u,
                    });
                return;
            }
            var dto = template.GetDescriptor<AnimalTemperamentDto>();
            if (dto == null) return;
            if (repo.IsComponentTypeRegistered<AnimalState>() && !repo.HasComponent<AnimalState>(entity))
                repo.AddComponent(entity, new AnimalState { Species = (AnimalSpecies)dto.Species, Mode = AnimalMode.Calm });
            // ⭐ AB-8 — its voice, present from spawn so the brain only WRITES it (never adds a component inside the parallel phase)
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.Utterance>() && !repo.HasComponent<Fdp.Toolkit.Civilians.Utterance>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Civilians.Utterance());
            // ⭐ AB-3 — its fear (kept where its brain runs; harmless elsewhere); the flight-zone looks staggered by entity so they spread over frames
            if (repo.IsComponentTypeRegistered<Fear>() && !repo.HasComponent<Fear>(entity))
                repo.AddComponent(entity, new Fear { NextZoneLookAt = (SimInstant)((entity.Index % 10) * (AnimalFear.ZoneLookSeconds / 10.0)) });
            // ⭐ AB-10 — the navigation contract every animal's brain writes (MoveTo): a ground animal has it from the vehicle translator, a BIRD
            //   from nothing (it is no aircraft — no Gen.Flight). 📐 Measured in the farm demo: the flock's first flight threw "missing
            //   NavigationIntent" in the MoveTo executor and took the node down
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationIntent>() && !repo.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Navigation.NavigationIntent());
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationStatus>() && !repo.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Navigation.NavigationStatus());
            if ((AnimalSpecies)dto.Species == AnimalSpecies.BirdFlock && repo.IsComponentTypeRegistered<FlockState>() && !repo.HasComponent<FlockState>(entity))
                repo.AddComponent(entity, new FlockState { Count = DefaultFlockCount, InitialCount = DefaultFlockCount, Seed = (uint)template.TkbType, Mode = FlockMode.Perched });
        }
    }

    /// <summary>⭐ <c>CE-4623</c> AB-1 — THE question "is this an animal?" (AR-19): it carries <see cref="AnimalState"/>. One component lookup; no class walk.</summary>
    public static class Animals
    {
        public static bool IsAnimal(ISimulationView view, Entity e)
            => view.IsAlive(e) && view.HasComponent<AnimalState>(e);

        /// <summary>⭐ <c>CE-3453</c> — a sensor track that must not enter a man's target memory: an animal AREA (an authored outline, no body) or an
        /// animal that is not attacking. ⛔ They crowded the 16 slots and evicted every enemy (the animal feature scenarios, `2026-11-16`).</summary>
        public static bool NotAContact(ISimulationView view, Entity e)
            => view.IsAlive(e) && (view.HasComponent<AnimalArea>(e) || (view.HasComponent<AnimalState>(e) && (view.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.Dangerous) == 0));

        /// <summary>An animal attacking right now (<see cref="AnimalFlags.Dangerous"/>) — self-defence covers firing at it (AR-6).</summary>
        public static bool IsDangerous(ISimulationView view, Entity e)
            => IsAnimal(view, e) && (view.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.Dangerous) != 0;

        /// <summary>By DIS alone (a template, no entity): a life form in the animal categories 200…209.</summary>
        /// <summary>⭐ CE-4649 part C ① — the SEAM for AN-21g's hunter: does <paramref name="unit"/> carry a STANDING order to engage animals of this species? Not built (part D); always false.</summary>
        public static bool MayEngage(ISimulationView view, Entity unit, Entity animal) => false;

        public static bool IsAnimalDis(DISEntityType dis) => dis.Kind == 3 && dis.Category >= 200 && dis.Category <= 209;
    }
}
