using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat
{
    /// <summary>How a round ended (or that it is still flying).</summary>
    public enum ShotOutcome : byte { InFlight, Hit, StoppedByTerrain, Expired }

    /// <summary>One terrain piece a round crossed: what, where, its resistance and the round's chance through it.</summary>
    public readonly record struct ShotCrossing(string Kind, string? Label, string Material, float ResistanceMmRha, float RoundPenetrationMm,
        float Chance, bool Passed, Vector3 At);

    /// <summary>
    /// ⭐ Tuning T-4 — one round's record, written by the systems that DECIDE it (📄 docs/DESIGN_Terrain_Combat_Tuning.md §4):
    /// <c>FireProcessingSystem</c> (the inputs it fired with), <c>BallisticsSystem</c> (each terrain crossing, a stop, expiry) and
    /// <c>HitResolutionSystem</c> (the unit struck and what arrived). ⛔ Nothing here is recomputed afterwards — the API reads what
    /// happened, so it cannot disagree with it.
    /// </summary>
    public sealed class ShotRecord
    {
        public long Seq { get; init; }
        public uint Tick { get; init; }
        public Entity Shooter { get; init; }
        public Entity Target { get; init; }
        public Entity Bullet { get; init; }
        public int WeaponIndex { get; init; }
        public Vector3 Muzzle { get; init; }
        public Vector3 Aim { get; init; }
        public uint Ordinal { get; init; }
        /// <summary>AQ85 σ (rad) and the deflection θ the round was turned by.</summary>
        public float Sigma { get; init; }
        public float Deflection { get; init; }
        public float Penetration { get; init; }
        public string PenetrationSource { get; init; } = "";
        public float Damage { get; init; }

        public ShotOutcome Outcome { get; internal set; }
        public uint EndTick { get; internal set; }
        public Vector3? EndPoint { get; internal set; }
        public Entity HitEntity { get; internal set; }
        public float ArrivingDamage { get; internal set; }
        public float ArrivingPenetration { get; internal set; }
        public List<ShotCrossing> Crossings { get; } = new();

        /// <summary>The crossings of the segment whose raycast has not resolved yet — committed by the next ballistics pass,
        /// dropped when that segment's raycast hits a unit first (the hit re-carries the round only up to the unit).</summary>
        internal List<ShotCrossing> Pending { get; } = new();
        internal Vector3? PendingStop { get; set; }
    }

    /// <summary>
    /// ⭐ Tuning T-4 — the per-world ring of the last <see cref="Capacity"/> shots (a typed ring, not the shared 500-event log). Held
    /// beside the world (<see cref="For"/>), so no system needs a singleton registered and a world without shots costs nothing.
    /// Single-threaded: written by the combat systems on the main thread, read by the debug API on the same thread.
    /// </summary>
    public sealed class ShotLog
    {
        public const int Capacity = 256;
        private static readonly ConditionalWeakTable<EntityRepository, ShotLog> s_logs = new();

        private readonly ShotRecord?[] _ring = new ShotRecord?[Capacity];
        private readonly Dictionary<(int Index, int Gen), ShotRecord> _byBullet = new();
        private long _next;

        /// <summary>The shot log of <paramref name="world"/>, created on first use.</summary>
        public static ShotLog For(EntityRepository world) => s_logs.GetValue(world, _ => new ShotLog());

        /// <summary>The log only if <paramref name="world"/> already has one (readers never create it).</summary>
        public static ShotLog? Peek(EntityRepository world) => s_logs.TryGetValue(world, out var log) ? log : null;

        public long Count => _next;

        /// <summary>Records a new round; evicts the oldest past <see cref="Capacity"/>.</summary>
        public ShotRecord Add(ShotRecord r)
        {
            var record = new ShotRecord
            {
                Seq = _next, Tick = r.Tick, Shooter = r.Shooter, Target = r.Target, Bullet = r.Bullet, WeaponIndex = r.WeaponIndex,
                Muzzle = r.Muzzle, Aim = r.Aim, Ordinal = r.Ordinal, Sigma = r.Sigma, Deflection = r.Deflection,
                Penetration = r.Penetration, PenetrationSource = r.PenetrationSource, Damage = r.Damage,
            };
            int slot = (int)(_next % Capacity);
            if (_ring[slot] is { } evicted) _byBullet.Remove(Key(evicted.Bullet));
            _ring[slot] = record;
            _byBullet[Key(record.Bullet)] = record;
            _next++;
            return record;
        }

        /// <summary>The live record of <paramref name="bullet"/>, or null (evicted, or fired before the log existed).</summary>
        public ShotRecord? Of(Entity bullet) => _byBullet.TryGetValue(Key(bullet), out var r) && r.Outcome == ShotOutcome.InFlight ? r : null;

        /// <summary>The last <paramref name="last"/> records, newest first.</summary>
        public IReadOnlyList<ShotRecord> Recent(int last)
        {
            var list = new List<ShotRecord>();
            for (long s = _next - 1; s >= 0 && s >= _next - Capacity && list.Count < last; s--)
                if (_ring[(int)(s % Capacity)] is { } r) list.Add(r);
            return list;
        }

        internal static void End(ShotRecord r, ShotOutcome outcome, uint tick, Vector3? at)
        {
            r.Outcome = outcome; r.EndTick = tick; r.EndPoint = at;
        }

        private static (int, int) Key(Entity e) => (e.Index, e.Generation);
    }
}
