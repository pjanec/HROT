using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐⭐ <c>CE-4649</c> PART C ③ (AN-20b; docs/designs/animals/DESIGN_Animals.md §4b) — a round through a FLOCK. A flock is ONE entity drawn as N birds (AN-4), so a round that meets its volume (the collider
    /// circle, <see cref="FlockState.Count"/> birds spread over it) has a CHANCE PER DRAWN BIRD IT PASSES to hit ONE: <c>passed = N · w · chord / (π R²)</c> (a bird <see cref="BirdWidthM"/> wide, the chord the round cuts
    /// through the circle of radius R), <c>P = 1 − (1 − <see cref="HitChancePerBird"/>)^passed</c>. A hit: the flock's count −1 and ONE lone DEAD bird of the flock's species (<see cref="AnimalTemperamentDto.LoneType"/>) is
    /// requested at the strike (it falls and lies — <c>BirdFlightModel</c>); the flock's own health is never touched; the rest flee through the fear the shot already gave. A miss: the round flies on (it is not consumed).
    /// ⭐ Deterministic: the roll is a hash of the flock, its count and the round — no shared RNG, no allocation but the one spawn request on a hit; any thread order gives the same bird.
    /// </summary>
    public static class FlockHits
    {
        /// <summary>The chance that a round which PASSES one drawn bird hits it (a pellet is small, a bird quick).</summary>
        public const float HitChancePerBird = 0.6f;
        /// <summary>The width a drawn bird presents to a round (m).</summary>
        public const float BirdWidthM = 0.3f;

        /// <summary>Is <paramref name="e"/> a flock (carries <see cref="FlockState"/>; an emptied one still is — its own health is never the target)?</summary>
        public static bool IsFlock(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<FlockState>() && repo.IsAlive(e) && repo.HasComponent<FlockState>(e);

        /// <summary>The expected number of drawn birds a segment cuts through a flock of <paramref name="count"/> birds in a circle of <paramref name="radius"/> at <paramref name="centre"/> (flat).</summary>
        public static float BirdsPassed(int count, float radius, Vector3 centre, Vector3 start, Vector3 end)
        {
            if (count <= 0 || radius <= 0f) return 0f;
            float dx = end.X - start.X, dy = end.Y - start.Y;
            float fx = start.X - centre.X, fy = start.Y - centre.Y;
            float a = dx * dx + dy * dy;
            float chord;
            if (a < 1e-8f) chord = (fx * fx + fy * fy <= radius * radius) ? 0f : 0f;
            else
            {
                float b = 2f * (fx * dx + fy * dy), c = fx * fx + fy * fy - radius * radius;
                float disc = b * b - 4f * a * c;
                if (disc <= 0f) return 0f;
                float s = MathF.Sqrt(disc);
                float t0 = Math.Clamp((-b - s) / (2f * a), 0f, 1f), t1 = Math.Clamp((-b + s) / (2f * a), 0f, 1f);
                chord = (t1 - t0) * MathF.Sqrt(a);
            }
            return count * BirdWidthM * chord / (MathF.PI * radius * radius);
        }

        /// <summary>The chance that a segment hits one bird: <c>1 − (1 − p)^passed</c>.</summary>
        public static float Chance(float passed) => passed <= 0f ? 0f : 1f - MathF.Pow(1f - HitChancePerBird, passed);

        private static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }

        /// <summary>The deterministic roll in [0,1) for this flock, its count and this round.</summary>
        public static float Roll(Entity flock, int count, long roundKey)
        {
            uint h = Hash((uint)flock.Index * 2654435761u ^ (uint)count * 0x9E3779B1u ^ (uint)roundKey ^ Hash((uint)(roundKey >> 32)));
            return (h >> 8) / 16777216f;
        }

        /// <summary>
        /// Resolves a round's segment <paramref name="start"/> → <paramref name="end"/> through <paramref name="flock"/>: true when it HIT a bird (the count is down by one and a dead bird is requested at
        /// <paramref name="strike"/>); false when it passed between them.
        /// </summary>
        public static bool TryHit(EntityRepository repo, Entity flock, Vector3 start, Vector3 end, Vector3 strike, long roundKey)
        {
            if (!IsFlock(repo, flock) || repo.GetComponentRO<FlockState>(flock).Count <= 0 || !repo.HasComponent<SimTransform>(flock)
                || !repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>() || !repo.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(flock)) return false;
            ref var state = ref repo.GetComponentRW<FlockState>(flock);
            float passed = BirdsPassed(state.Count, repo.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(flock).Radius, repo.GetComponentRO<SimTransform>(flock).Position, start, end);
            if (Roll(flock, state.Count, roundKey) >= Chance(passed)) return false;
            state.Count--;
            Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, flock, out var template);   // may be absent: then no dead bird is drawn, the count still drops
            long lone = template?.GetDescriptor<AnimalTemperamentDto>()?.LoneType ?? 0L;
            if (lone != 0 && repo.Bus.IsRegisteredManaged<EntityCreationRequest>())
            {
                float maxHealth = 1f; string loneName = "bird";
                if (repo.HasSingletonManaged<ITkbDatabase>() && repo.GetSingletonManaged<ITkbDatabase>() is { } db && db.TryGetByType(lone, out var loneTemplate) && loneTemplate?.GetDescriptor<CombatPlatformDefDto>() is { } def)
                    { maxHealth = def.MaxHealth; loneName = loneTemplate.Name; }
                repo.Bus.PublishManaged(new EntityCreationRequest
                {
                    RequestId = Guid.NewGuid(),
                    OwnerAppInstanceId = 0,   // the system picks
                    TkbType = (TkbTypeId)lone,
                    InitialComponents = new List<object>
                    {
                        new SimTransform { Position = strike, Rotation = Quaternion.Identity },
                        new Health { Current = 0f, Max = maxHealth, StateSince = repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0 },   // dead from the first frame: it falls and lies — ⭐ CE-4661: killed NOW (first sight stamps nothing)
                        new EntityInfo { Name = "Dead " + loneName, ForceId = ForceId.Neutral },   // named, so a scenario can address the bird a hit left
                    },
                });
            }
            return true;
        }
    }
}
