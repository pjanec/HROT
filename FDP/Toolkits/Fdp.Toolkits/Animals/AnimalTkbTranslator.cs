using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Creatures;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐ <c>CE-4623</c> AB-1 (docs/designs/animals/DESIGN_Animals.md §6.1) — an animal type (<c>Gen.Animal</c>) gets its <see cref="AnimalState"/>
    /// on EVERY node at spawn (calm; ⭐ CE-4628: its species is the TKB type's — <see cref="AnimalSpeciesOf"/>), and a flock its <see cref="FlockState"/>; the other animal components are registered here so a
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
            yield return typeof(AnimalTasks);   // ⭐ CE-4628 — the animal-only part of its fear state (intruder, handler, quarry, bolt, zone-look schedule)
            yield return typeof(FlockState);
            yield return typeof(GroupMember);
            yield return typeof(Home);
            yield return typeof(HerdState);
            yield return typeof(Group);
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
                repo.AddComponent(entity, new AnimalState { Mode = AnimalMode.Calm });
            // ⭐ AB-8 — its voice, present from spawn so the brain only WRITES it (never adds a component inside the parallel phase)
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.Utterance>() && !repo.HasComponent<Fdp.Toolkit.Civilians.Utterance>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Civilians.Utterance());
            // ⭐ AB-3 — its fear (kept where its brain runs; harmless elsewhere), the shared one (CE-4628); ⭐ and its animal-only part, with the flight-zone
            //   looks staggered by entity so they spread over frames. Each WITH its final value (no add-then-write)
            if (repo.IsComponentTypeRegistered<Fear>() && !repo.HasComponent<Fear>(entity)) repo.AddComponent(entity, new Fear());
            // ⭐ CE-4628 — its HOME on spawn (radius 0, tether 0 = no wandering limit, so behaviour is unchanged): where it stands if it has a pose yet. An AREA animal's own
            //   home (AnimalAreaSystem puts it in the creation request's InitialComponents) is SET over this one afterwards (NetworkSpawningSystem: Inject runs first), so it wins
            if (repo.IsComponentTypeRegistered<Home>() && !repo.HasComponent<Home>(entity))
                repo.AddComponent(entity, new Home { Centre = repo.HasComponent<SimTransform>(entity) ? (WorldPoint)repo.GetComponentRO<SimTransform>(entity).Position : default, Area = Entity.Null });
            if (repo.IsComponentTypeRegistered<AnimalTasks>() && !repo.HasComponent<AnimalTasks>(entity))
                repo.AddComponent(entity, new AnimalTasks { NextZoneLookAt = (SimInstant)((entity.Index % 10) * (AnimalFear.ZoneLookSeconds / 10.0)) });
            // ⭐ AB-10 — the navigation contract every animal's brain writes (MoveTo): a ground animal has it from the vehicle translator, a BIRD
            //   from nothing (it is no aircraft — no Gen.Flight). 📐 Measured in the farm demo: the flock's first flight threw "missing
            //   NavigationIntent" in the MoveTo executor and took the node down
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationIntent>() && !repo.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Navigation.NavigationIntent());
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationStatus>() && !repo.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Navigation.NavigationStatus());
            // ⭐ CE-4649 part D (AN-21b) — a FREEZER (the rabbit) has the stance a crouching man has: the brain writes it, the logical-stance readers (line of sight, the hit model) shrink its signature
            if (dto.Freezes && repo.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>() && !repo.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(entity))
                repo.AddComponent(entity, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = StanceId.Standing });
            if ((AnimalSpecies)dto.Species is AnimalSpecies.BirdFlock or AnimalSpecies.DuckFlock && repo.IsComponentTypeRegistered<FlockState>() && !repo.HasComponent<FlockState>(entity))
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
        public static bool NotAContact(ISimulationView view, Entity e, Entity observer = default)
            => view.IsAlive(e) && !MayEngage(view, observer, e) && (view.HasComponent<AnimalArea>(e) || (view.HasComponent<AnimalState>(e) && (view.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.Dangerous) == 0));

        /// <summary>An animal attacking right now (<see cref="AnimalFlags.Dangerous"/>) — self-defence covers firing at it (AR-6).</summary>
        public static bool IsDangerous(ISimulationView view, Entity e)
            => IsAnimal(view, e) && (view.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.Dangerous) != 0;

        /// <summary>By DIS alone (a template, no entity): a life form in the animal categories 200…209.</summary>
        /// <summary>⭐ CE-4649 part C ① — AN-21g: does <paramref name="unit"/> carry a STANDING hunting rule (<c>HuntingRulesDto</c>) naming this animal's species? ⛔ People are never animals, so no rule can name one.</summary>
        public static bool MayEngage(ISimulationView view, Entity unit, Entity animal)
        {
            if (view is not EntityRepository repo || unit.IsNull || animal.IsNull || !IsAnimal(view, animal)) return false;
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, unit, out var template) || template.GetDescriptor<HuntingRulesDto>() is not { } rules) return false;
            if (!AnimalSpeciesOf.TryOf(repo, animal, out var animalSpecies)) return false;
            var species = (byte)animalSpecies;
            for (int i = 0; i < rules.Species.Count; i++) if (rules.Species[i] == species) return true;
            return false;
        }

        /// <summary>⭐ CE-4649 part D (AN-21g) — does <paramref name="unit"/> carry a standing hunting rule (a hunter)?</summary>
        public static bool IsHunter(EntityRepository repo, Entity unit)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, unit, out var template) && template.GetDescriptor<HuntingRulesDto>() != null;

        public static bool IsAnimalDis(DISEntityType dis) => dis.Kind == 3 && dis.Category >= 200 && dis.Category <= 209;
    }
}
