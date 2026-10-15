using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-10, §1 module diagram) — the shots and bursts of the world, as the map host's
    /// <c>EffectSpawnSystem</c> reads them (it already sees every fire and detonation event, VE-A): queued here for the
    /// <see cref="AudioLayer"/> to drain each frame. It exists ONLY while the layer listens (<see cref="Listen"/> … <see cref="Release"/>),
    /// so a host that never possesses pays nothing; capped so a stalled layer cannot grow it. Held beside the world (one per world), like
    /// <c>RecentShots</c> — presentation memory, never saved or sent.
    /// </summary>
    public sealed class SoundEvents
    {
        /// <summary>A shot as the spawner saw it.</summary>
        public readonly record struct Shot(Entity Shooter, int Mount, Vector3 At, long Ammo, float DamagePerHit, bool Automatic, long ShooterType);

        /// <summary>A detonation: where, of what, and whether it hit a body (a soft impact) or the world.</summary>
        public readonly record struct Burst(Vector3 At, long Ammo, float Damage, bool HitBody);

        /// <summary>The most of each kept between two frames.</summary>
        public const int Cap = 256;

        public List<Shot> Shots { get; } = new();
        public List<Burst> Bursts { get; } = new();

        public void Add(in Shot s) { if (Shots.Count < Cap) Shots.Add(s); }
        public void Add(in Burst b) { if (Bursts.Count < Cap) Bursts.Add(b); }

        public void Clear() { Shots.Clear(); Bursts.Clear(); }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, SoundEvents> ByWorld = new();

        /// <summary>The world's queue while a listener is on; null otherwise — the spawner's one cheap check.</summary>
        public static SoundEvents? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var e) ? e : null;

        /// <summary>The listener starts: the queue exists from now on.</summary>
        public static SoundEvents Listen(EntityRepository world) => ByWorld.GetValue(world, _ => new SoundEvents());

        /// <summary>The listener stops: the queue is gone and the spawner stops filling it.</summary>
        public static void Release(EntityRepository world) => ByWorld.Remove(world);
    }
}
