using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;

namespace Hrot.Core.Animals
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-9 (docs/designs/animals/DESIGN_Animals.md AR-16) — POPULATES each ANIMAL AREA (TKB 712) once, at run start, on the node that
    /// owns it: <see cref="AnimalArea.Count"/> animals of its species placed by its <see cref="AnimalArea.Seed"/> inside its outline (a flock: ONE flock entity of
    /// that many birds), through the ONE creation path in the shape <c>MacroInsertion</c> uses — TRANSIENT (never saved: the scenario keeps only the area), each
    /// GUID DERIVED from the area's GUID and the animal's slot (a replay re-derives the same ids). Then it marks the area <see cref="AnimalAreaPopulated"/> —
    /// recorded, so a restore or a replay NEVER repopulates. The area becomes the HERD entity of a herding species (its own <see cref="HerdState"/> and
    /// <see cref="AnimalHome"/>); the <see cref="HerdSystem"/> links the arriving animals to it. ⭐ Every animal's home is the area (its centre and reach,
    /// and ENCLOSED: a pen nothing runs out of).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class AnimalAreaSystem : IEcsModuleSystem
    {
        /// <summary>The most animals one area asks for (an authoring slip of 60 000 cows stops here).</summary>
        public const int MaxPerArea = 300;

        private readonly List<Entity> _due = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AnimalArea>() || !repo.IsComponentTypeRegistered<AnimalAreaPopulated>()) return;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(repo) || !repo.Bus.IsRegisteredManaged<EntityCreationRequest>()) return;
            _due.Clear();
            foreach (var area in repo.Query().With<AnimalArea>().With<SimTransform>().Without<AnimalAreaPopulated>().Build())
                if (repo.HasAuthority(area)) _due.Add(area);
            if (_due.Count == 0) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;
            for (int i = 0; i < _due.Count; i++) Populate(repo, _due[i], now);
        }

        /// <summary>Asks for one area's animals and marks it populated. Public for the rail.</summary>
        public static int Populate(EntityRepository repo, Entity area, double now)
        {
            var a = repo.GetComponentRO<AnimalArea>(area);
            Vector3 anchor = repo.GetComponentRO<SimTransform>(area).Position;
            IReadOnlyList<Vector2> outline = repo.HasManagedComponent<EditablePolyline>(area)
                ? ((ISimulationView)repo).GetManagedComponentRO<EditablePolyline>(area).Points : Array.Empty<Vector2>();
            Outline(outline, out var centroid, out float radius, out var min, out var max);
            var centre = new Vector3(anchor.X + centroid.X, anchor.Y + centroid.Y, anchor.Z);
            var home = new AnimalHome { Centre = centre, Radius = MathF.Max(radius, 5f), Enclosed = a.Enclosed };
            int species = (int)a.Species;
            long type = TkbEntityTypes.Animal_Cattle + species;
            var row = species >= 0 && species < AnimalTkbCatalog.Species.Length ? AnimalTkbCatalog.Species[species] : null;
            bool flock = a.Species == AnimalSpecies.BirdFlock;
            bool herding = row != null && row.Temperament.HerdCohesion > 0f;
            Guid parent = repo.IsComponentTypeRegistered<EntityGuid>() && repo.HasComponent<EntityGuid>(area) ? repo.GetComponentRO<EntityGuid>(area).Value : Guid.Empty;
            int count = flock ? (a.Count > 0 ? 1 : 0) : Math.Min((int)a.Count, MaxPerArea);
            // ⭐ AB-10 — each animal is NAMED after its area ("Pasture 3"; a flock carries the area's name): EntityInfo is a per-spawn value
            //   (EntityInfo.cs — the TKB stamps only a placeholder), and a scenario's checks, the entity list and the film find units by name
            string areaName = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(area)
                ? repo.GetComponentRO<EntityInfo>(area).Name.ToString() : string.Empty;
            if (areaName.Length == 0) areaName = row?.Name ?? "Animal";
            var rng = SimRng.FromSim(unchecked((int)a.Seed), 0x4629, 0f);
            for (int i = 0; i < count; i++)
            {
                var at = Place(ref rng, outline, min, max, anchor, centre, radius);
                float heading = rng.NextSingle() * 360f;
                var components = new List<object>
                {
                    new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, heading * MathF.PI / 180f) },
                    new EntityGuid(EntityGuidDerivation.Derive(parent, parent == Guid.Empty ? $"animal:{area.Index}:{i}" : $"animal:{i}")),
                    home,
                    new EntityInfo { Name = new FixedString64(flock ? areaName : $"{areaName} {i + 1}"), ForceId = ForceId.Neutral },
                };
                if (flock)
                    components.Add(new FlockState { Count = a.Count, InitialCount = a.Count, Seed = a.Seed, Mode = FlockMode.Perched });
                repo.Bus.PublishManaged(new EntityCreationRequest
                {
                    RequestId = EntityGuidDerivation.Derive(parent, $"animal-request:{area.Index}:{i}"),
                    OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                    TkbType = (TkbTypeId)type,
                    InitType = ReliableInitType.None,
                    IsTransient = true,   // SR-171 — the scenario keeps the AREA, never its animals
                    InitialComponents = components,
                });
            }
            // the area is the herd: its own home (grazing spots inside it) and its herd state
            if (herding && !flock)
            {
                if (repo.IsComponentTypeRegistered<HerdState>() && !repo.HasComponent<HerdState>(area))
                    repo.AddComponent(area, new HerdState { Species = a.Species, Centre = centre });
                if (repo.IsComponentTypeRegistered<AnimalHome>() && !repo.HasComponent<AnimalHome>(area)) repo.AddComponent(area, home);
            }
            repo.AddComponent(area, new AnimalAreaPopulated { At = (SimInstant)now });
            return count;
        }

        private static void Outline(IReadOnlyList<Vector2> pts, out Vector2 centroid, out float radius, out Vector2 min, out Vector2 max)
        {
            centroid = Vector2.Zero; radius = 0f; min = Vector2.Zero; max = Vector2.Zero;
            if (pts.Count == 0) return;
            min = new Vector2(float.MaxValue); max = new Vector2(float.MinValue);
            for (int i = 0; i < pts.Count; i++) { centroid += pts[i]; min = Vector2.Min(min, pts[i]); max = Vector2.Max(max, pts[i]); }
            centroid /= pts.Count;
            for (int i = 0; i < pts.Count; i++) radius = MathF.Max(radius, Vector2.Distance(pts[i], centroid));
        }

        /// <summary>A seeded point inside the outline (rejection in its box; a degenerate outline: round its centre).</summary>
        private static Vector3 Place(ref SimRng rng, IReadOnlyList<Vector2> outline, Vector2 min, Vector2 max, Vector3 anchor, Vector3 centre, float radius)
        {
            if (outline.Count >= 3)
                for (int tries = 0; tries < 32; tries++)
                {
                    var p = new Vector2(min.X + rng.NextSingle() * (max.X - min.X), min.Y + rng.NextSingle() * (max.Y - min.Y));
                    if (PolygonMath.Contains(outline, p)) return new Vector3(anchor.X + p.X, anchor.Y + p.Y, anchor.Z);
                }
            float ang = rng.NextSingle() * MathF.Tau, r = MathF.Sqrt(rng.NextSingle()) * MathF.Max(radius, 5f) * 0.8f;
            return new Vector3(centre.X + MathF.Cos(ang) * r, centre.Y + MathF.Sin(ang) * r, centre.Z);
        }
    }
}
