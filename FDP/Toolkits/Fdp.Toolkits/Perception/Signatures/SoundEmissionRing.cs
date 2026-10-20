using System;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>⭐ <c>CE-3282</c> (Q95 C; docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.3) — one short sound: a shot or a burst, as it was made.</summary>
    public struct SoundEmission
    {
        /// <summary>Its number in the ring (1, 2, 3 …); 0 = a slot being written (or never written).</summary>
        public long Serial;
        /// <summary>Who made it (the packed entity) — the error's seed and the "not my own sound" test; never answered (anonymous).</summary>
        public ulong Source;
        /// <summary>The source's force: a listener does not hear his own side (the broadphase's rule).</summary>
        public byte Force;
        /// <summary><see cref="SoundKind"/>: Shot or Detonation.</summary>
        public byte Kind;
        /// <summary>Its <c>SoundSourceClass</c>.</summary>
        public byte Class;
        /// <summary>Where it was made.</summary>
        public Vector3 At;
        /// <summary>How far it carries (m).</summary>
        public float Carry;
        /// <summary>When it was made (sim seconds).</summary>
        public double Time;
        /// <summary>⭐ Q95 A (<c>CE-3283</c>) — what made it: the TKB type of the weapon (a shot) or the munition (a burst); 0 = unknown.</summary>
        public long Signature;
        /// <summary>⭐ <c>CE-3283</c> — <see cref="Signature"/>'s per-node token (<see cref="SoundEmissionRing.TypeOf"/>): what fits the answer's
        /// padding. Stamped by <see cref="SoundEmissionRing.Append"/>.</summary>
        public ushort Token;
        /// <summary>⭐ <c>CE-3283</c> — the source's <c>SoundFamily</c> (TKB data).</summary>
        public byte Family;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3282</c> (Q95 C; Sensing §8.3) — every shot and burst, serial-numbered, so a hearing sensor reads EVERYTHING after the last serial
    /// it consumed, whenever it runs — a solve skipped or discarded loses nothing (the 0.5 s linger did).
    /// <para>⚠ The background solver's snapshot BORROWS this object by reference (<c>EntityRepository.Sync.cs</c> allow-list), so the reader
    /// runs beside the writer: ONE writer (the main thread, <see cref="SoundEmissionSystem"/>) stamps each slot behind a sequence (blank the
    /// serial, write the fields, stamp the serial, then move <see cref="Head"/>); a reader copies a slot and keeps it only when its serial is the
    /// one it asked for, before and after the copy — a slot overwritten under a lagging reader is COUNTED (<see cref="Overflow"/>), never
    /// silently misread.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.SoundEmissionRing)]
    public sealed class SoundEmissionRing
    {
        /// <summary>How many sounds the ring holds: 4096 / 10 s (the solver circuit's worst skip) ≈ 400 sounds a second, sustained.</summary>
        public const int Capacity = 4096;

        /// <summary>A sound older than this (s) is not heard any more, however far behind a sensor is.</summary>
        public const double MaxAgeSeconds = 30.0;

        private readonly SoundEmission[] _slots = new SoundEmission[Capacity];
        private long _head;
        private long _overflow;
        private double _now;

        /// <summary>The serial of the newest sound (0 = none yet).</summary>
        public long Head => Volatile.Read(ref _head);

        /// <summary>Sounds lost because a sensor fell more than <see cref="Capacity"/> behind (or read a slot overwritten under it) — the
        /// diagnostic that says N is too small. Never a silent loss.</summary>
        public long Overflow => Interlocked.Read(ref _overflow);

        /// <summary>The writer's clock (sim seconds), moved every frame — what "old" is measured against (the snapshot has no clock).</summary>
        public double Now => Volatile.Read(ref _now);

        /// <summary>The writer moves the clock (main thread).</summary>
        public void Tick(double now) => Volatile.Write(ref _now, now);

        /// <summary>Appends one sound (the ONE writer: the main thread); returns its serial.</summary>
        public long Append(SoundEmission e)
        {
            e.Token = TokenOf(e.Signature);
            long serial = _head + 1;
            ref var slot = ref _slots[serial % Capacity];
            Volatile.Write(ref slot.Serial, 0L);   // ① blank: a reader copying now sees a mismatch
            e.Serial = 0L;
            slot = e;                               // ② the fields (Serial still 0)
            Volatile.Write(ref slot.Serial, serial); // ③ stamp
            Volatile.Write(ref _head, serial);      // ④ publish
            return serial;
        }

        /// <summary>
        /// Every sound after <paramref name="after"/> up to <see cref="Head"/> into <paramref name="into"/> (oldest first), skipping sounds older
        /// than <see cref="MaxAgeSeconds"/>; returns how many, and the serial to remember (<paramref name="last"/>). A sensor more than
        /// <see cref="Capacity"/> behind, or a slot overwritten under the copy, adds to <see cref="Overflow"/>. Safe beside the writer.
        /// </summary>
        /// <param name="notBefore">⭐ a sensor with no cursor yet (<paramref name="after"/> ≤ 0) reads only sounds made since this time, and
        /// counts nothing lost — it never existed for the older ones.</param>
        public int ReadAfter(long after, Span<SoundEmission> into, out long last, double notBefore = double.MinValue)
        {
            long head = Head;
            last = after;
            if (head <= after) return 0;
            long first = after + 1;
            if (head - first >= Capacity)
            {
                if (after > 0) Interlocked.Add(ref _overflow, head - Capacity + 1 - first);
                first = head - Capacity + 1;
            }
            double oldest = Math.Max(Now - MaxAgeSeconds, notBefore);
            int n = 0;
            for (long s = first; s <= head; s++)
            {
                if (n == into.Length) break;   // the caller's span is full: stop here, the next run reads on
                ref var slot = ref _slots[s % Capacity];
                long before = Volatile.Read(ref slot.Serial);
                var copy = slot;
                long after2 = Volatile.Read(ref slot.Serial);
                last = s;
                if (before != s || after2 != s) { Interlocked.Increment(ref _overflow); continue; }   // overwritten under me
                if (copy.Time < oldest) continue;
                copy.Serial = s;
                into[n++] = copy;
            }
            return n;
        }

        // ⭐ CE-3283 (Sensing §8.4) — signature tokens: a TKB type ↔ a 16-bit token, per node, append-only. The writer assigns (main thread);
        //   a reader only ever holds a token it read from a slot stamped AFTER the type was stored, so the array entry is already there.
        /// <summary>How many distinct signatures a node can tell apart; a type past it is heard as unknown (token 0).</summary>
        public const int MaxSignatures = 4096;
        private readonly long[] _types = new long[MaxSignatures];
        private readonly System.Collections.Generic.Dictionary<long, ushort> _tokens = new();

        private ushort TokenOf(long type)
        {
            if (type == 0) return 0;
            if (_tokens.TryGetValue(type, out var t)) return t;
            if (_tokens.Count + 1 >= MaxSignatures) return 0;
            t = (ushort)(_tokens.Count + 1);
            Volatile.Write(ref _types[t], type);
            _tokens[type] = t;
            return t;
        }

        /// <summary>⭐ <c>CE-3283</c> — the TKB type a token stands for (0 = unknown). Safe beside the writer.</summary>
        public long TypeOf(ushort token) => token == 0 || token >= MaxSignatures ? 0L : Volatile.Read(ref _types[token]);

        /// <summary>The world's ring, created on first use (idempotent).</summary>
        public static SoundEmissionRing Of(EntityRepository world)
        {
            if (world.HasSingletonManaged<SoundEmissionRing>()) return world.GetSingletonManaged<SoundEmissionRing>()!;
            var ring = new SoundEmissionRing();
            world.SetSingletonManaged(ring);
            return ring;
        }

        /// <summary>The ring a view holds, or null (a node that makes no sounds).</summary>
        public static SoundEmissionRing? In(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<SoundEmissionRing>() ? repo.GetSingletonManaged<SoundEmissionRing>() : null;
    }

    /// <summary>⭐ <c>CE-3282</c> (Sensing §8.3) — the last <see cref="SoundEmissionRing"/> serial a hearing sensor consumed. Written through the
    /// solver's command buffer, so a discarded solve discards it too and the next run reads the same sounds again.</summary>
    [ComponentId(GlobalComponentIds.AcousticCursor)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct AcousticCursor
    {
        public long LastSerial;
    }
}
