using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-6) — the damage each PART of a body has taken:
    /// a level 0–255 per part id of its plan (a person's ten). ONE component for a man's wounds and (S2) a vehicle's compartments. Written by
    /// the node that owns <c>Health</c> (the Brain, <c>HealthApplicationSystem</c>), replicated beside it (Brain group, <c>dtPartDamage</c>);
    /// every node DERIVES the consequences (<see cref="Wounds"/>) — never stored. Added on the first part hit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PartDamage)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public unsafe struct PartDamage
    {
        public const int MaxParts = 16;

        /// <summary>The body plan the levels index (<see cref="BodyPlan"/>).</summary>
        public BodyPlan Plan;

        /// <summary>The damage level per part id (0 none … 255 destroyed).</summary>
        public fixed byte Levels[MaxParts];

        public readonly byte LevelOf(byte part) => part < MaxParts ? Levels[part] : (byte)0;

        /// <summary>Adds <paramref name="amount"/> to <paramref name="part"/>'s level, saturating at 255.</summary>
        public void Add(byte part, int amount)
        {
            if (part >= MaxParts || amount <= 0) return;
            Levels[part] = (byte)System.Math.Min(255, Levels[part] + amount);
        }

        /// <summary>The level a hit of <paramref name="damage"/> adds to its part, against the body's <paramref name="maxHealth"/>: the
        /// share of the whole body's health × 255 (a quarter of a man's health in one limb ⇒ 64, the starter wound threshold).</summary>
        public static int LevelFor(float damage, float maxHealth)
            => damage <= 0f ? 0 : maxHealth <= 0f ? 255 : (int)MathF.Ceiling(System.Math.Clamp(damage / maxHealth, 0f, 1f) * 255f);
    }

    /// <summary>⭐ <c>CE-3210</c> (HL-8) — what left a mark.</summary>
    public enum HitMarkKind : byte { Bullet = 1, Fragment = 2, Burn = 3, Penetration = 4 }

    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (HL-8; 🔒 user: "Bullet holes or shrapnel fragment hit points could be published and replicated in a fixed size
    /// array component that is created only if the entity was hit; owned by muscle") — where rounds struck a body: a RING of
    /// <see cref="Capacity"/> marks, each a part, a point INSIDE that part (−0.5 … 0.5, quantised), the face, the kind and a running
    /// sequence. Written by the MUSCLE (the node that resolves the hit), replicated to every node (<c>dtHitMarks</c>); a viewer draws blood
    /// or a hole on the part, where it moves with the arm or turns with the turret, and keeps what it has seen by <see cref="NextSeq"/>.
    /// Added on the first hit; never saved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.HitMarks)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct HitMarks
    {
        public const int Capacity = 24;

        /// <summary>The sequence the NEXT mark gets (the count ever written; the ring holds the last <see cref="Capacity"/>).</summary>
        public ushort NextSeq;

        public fixed byte Parts[Capacity];
        public fixed short X[Capacity];
        public fixed short Y[Capacity];
        public fixed short Z[Capacity];
        public fixed byte Faces[Capacity];
        public fixed byte Kinds[Capacity];

        /// <summary>How many slots hold a mark.</summary>
        public readonly int Count => System.Math.Min((int)NextSeq, Capacity);

        /// <summary>Writes a mark over the oldest: <paramref name="local"/> is the point in the part's unit box (−0.5 … 0.5).</summary>
        public void Add(byte part, Vector3 local, byte face, HitMarkKind kind)
        {
            int i = NextSeq % Capacity;
            Parts[i] = part;
            X[i] = Quantise(local.X); Y[i] = Quantise(local.Y); Z[i] = Quantise(local.Z);
            Faces[i] = face;
            Kinds[i] = (byte)kind;
            NextSeq = (ushort)(NextSeq + 1);
        }

        /// <summary>The mark with sequence <paramref name="seq"/> if it is still in the ring.</summary>
        public readonly bool TryGet(int seq, out byte part, out Vector3 local, out byte face, out HitMarkKind kind)
        {
            part = 0; local = default; face = 0; kind = 0;
            if (seq < 0 || seq >= NextSeq || seq < NextSeq - Capacity) return false;
            int i = seq % Capacity;
            part = Parts[i]; local = new Vector3(Unquantise(X[i]), Unquantise(Y[i]), Unquantise(Z[i])); face = Faces[i]; kind = (HitMarkKind)Kinds[i];
            return true;
        }

        public static short Quantise(float v) => (short)System.Math.Clamp((int)MathF.Round(v * 65534f), short.MinValue, short.MaxValue);
        public static float Unquantise(short q) => q / 65534f;
    }

    /// <summary>⭐ <c>CE-3210</c> (HL-8) — the one writer of <see cref="HitMarks"/>: adds the component on a body's first mark.</summary>
    public static class HitMarksWriter
    {
        public static void Mark(EntityRepository repo, Entity body, byte part, Vector3 local, byte face, HitMarkKind kind)
        {
            if (part == PersonPart.None || !repo.IsAlive(body) || !repo.IsComponentTypeRegistered<HitMarks>()) return;
            if (!repo.HasComponent<HitMarks>(body)) repo.AddComponent(body, new HitMarks());
            repo.GetComponentRW<HitMarks>(body).Add(part, local, face, kind);
        }
    }
}
