using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat
{
    /// <summary>One entity a burst reached: how exposed it was, and what each effect did. Written by <c>AreaEffectSystem</c>.</summary>
    public sealed record DetonationEffect(Entity Entity, string Stance, int BodyPoints, float Distance,
        float FragmentExposure, float FragmentFalloff, float FragmentArmourChance, float FragmentDamage,
        float BlastFalloff, float BlastBarrier, float BlastShadow, float BlastDamage, float TotalDamage, string? ShieldedBy)
    {
        /// <summary>⭐ <c>CE-3117</c> — where the entity stood when the burst reached it (the map labels its exposure there).</summary>
        public Vector3 At { get; init; }
    }

    /// <summary>⭐ <c>CE-3117</c> — one fragment ray the area effect traced: the body point, how much got through (0..1), and the
    /// first obstacle on the way (a terrain piece or a collider), or null for a clear line.</summary>
    public readonly record struct DetonationRayRecord(Vector3 To, float Transmission, Vector3? StopAt);

    /// <summary>
    /// ⭐ Buildings Stage 6 (<c>CE-1032</c>, W-10) — one detonation's record, as the area effect decided it: the burst, the munition
    /// and where its warhead numbers came from, every entity in reach and the doors it breached. ⛔ Nothing is recomputed afterwards
    /// (the shot log's rule).
    /// </summary>
    public sealed class DetonationRecord
    {
        public long Seq { get; init; }
        public uint Tick { get; init; }
        public Entity Shooter { get; init; }
        public Entity Struck { get; init; }
        public Vector3 Burst { get; init; }
        public long Ammo { get; init; }
        public string Warhead { get; init; } = "";
        public string WarheadSource { get; init; } = "";
        public float FragmentRadius { get; init; }
        public float BlastInjuryRadius { get; init; }
        public List<DetonationEffect> Effects { get; } = new();
        public List<string> DoorsBreached { get; } = new();
        /// <summary>⭐ <c>CE-3117</c> — every fragment ray, in target order (what the map draws).</summary>
        public List<DetonationRayRecord> Rays { get; } = new();
    }

    /// <summary>
    /// ⭐ <c>CE-1032</c> (W-10) — the per-world ring of the last <see cref="Capacity"/> detonations with an area effect, beside the world
    /// like <see cref="ShotLog"/>. Single-threaded: written by <c>AreaEffectSystem</c>, read by the debug API on the main thread.
    /// </summary>
    public sealed class DetonationLog
    {
        public const int Capacity = 64;
        private static readonly ConditionalWeakTable<EntityRepository, DetonationLog> s_logs = new();
        private readonly DetonationRecord?[] _ring = new DetonationRecord?[Capacity];
        private long _next;

        public static DetonationLog For(EntityRepository world) => s_logs.GetValue(world, _ => new DetonationLog());
        public static DetonationLog? Peek(EntityRepository world) => s_logs.TryGetValue(world, out var log) ? log : null;

        public long Count => _next;

        /// <summary>Takes a sequence number for a new record.</summary>
        public long NextSeq() => _next;

        public void Add(DetonationRecord r) { _ring[_next % Capacity] = r; _next++; }

        /// <summary>⭐ <c>CE-3117</c> — the record numbered <paramref name="seq"/>, if it is still in the ring (no allocation).</summary>
        public DetonationRecord? At(long seq) =>
            seq >= 0 && seq < _next && seq >= _next - Capacity && _ring[seq % Capacity] is { } r && r.Seq == seq ? r : null;

        /// <summary>The newest <paramref name="max"/> records, newest first.</summary>
        public IEnumerable<DetonationRecord> Recent(int max = Capacity)
        {
            for (long i = _next - 1, n = 0; i >= 0 && i >= _next - Capacity && n < max; i--, n++)
                if (_ring[i % Capacity] is { } r) yield return r;
        }
    }
}
