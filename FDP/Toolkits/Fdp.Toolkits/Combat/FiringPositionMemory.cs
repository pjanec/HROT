using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt.Kernel;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-3136</c> P-6 — the phases of the <c>PeekAndFire</c> node (📄 docs/DESIGN_Peek_And_Fire.md §8.2).</summary>
    public enum PeekPhase : byte
    {
        /// <summary>Waiting for the cover sensor's answer to pick a hide point.</summary>
        Choose = 0,
        /// <summary>Walking to the hide point.</summary>
        MoveToHide = 1,
        /// <summary>Hidden (the hide stance at the hide point), waiting a random time — longer while suppressed or reloading.</summary>
        Hidden = 2,
        /// <summary>Coming up (stance peek) or stepping out (step peek); the grace time for seeing the enemy runs.</summary>
        Expose = 3,
        /// <summary>Firing aimed rounds at a SEEN enemy (the executor's aim time and sight gate apply).</summary>
        Aimed = 4,
        /// <summary>Not seen within the grace time: a blind burst at the remembered spot (no aim time).</summary>
        Blind = 5,
        /// <summary>Going back down / back behind the hide point.</summary>
        Recover = 6,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> B1 — the UNIT's used firing positions (<c>Q87</c> unit memory, <c>R-237</c>/<c>R-238</c>): outlives every exit
    /// of the <c>PeekAndFire</c> node (a posture switch, a reload hold, a re-plan), so a burned window stays burned and cools on its
    /// own. 8 slots of heat (B2: an exposure heats, being shot at heats more; B3: cooled lazily, <c>heat · 2^-(Δt/halfLife)</c>; B4:
    /// burned with hysteresis — <see cref="PositionHeat"/>). ⭐ It also mirrors the node's phase and points for the map's
    /// <c>PeekAndFireGizmo</c> (T4) — the node's own state sits in a tree's blackboard at an offset no gizmo can know. Recorded for
    /// replay, never saved, never on the wire (Q87). ⭐ Here, in the toolkit, so every map host that loads the toolkit can draw it
    /// (R-228) — the node itself lives with its shared steps in <c>Hrot.AI.Behaviors</c>.
    /// </summary>
    [UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FiringPositionMemory
    {
        /// <summary>The slots.</summary>
        public const int Capacity = 8;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        /// <summary>The heat when last written, at <see cref="HeatAt"/> (sim s).</summary>
        public fixed float Heat[Capacity];
        public fixed double HeatAt[Capacity];
        /// <summary>1 = burned (B4: set at <c>BurnHeat</c>, cleared below <c>ReuseHeat</c>) · exposures from the slot.</summary>
        public fixed byte Burned[Capacity];
        public fixed byte Uses[Capacity];
        /// <summary>Slots in use.</summary>
        public byte Count;

        /// <summary>T4 display — the node's phase (<see cref="PeekPhase"/>) and the sim time it was last written / its timer ends.</summary>
        public byte Phase;
        public double PhaseAt, PhaseUntil;
        public Vector3 HidePoint, PeekPoint;

        /// <summary>⭐ Declared: a unit memory is created with <c>new T()</c> (Q87 §1).</summary>
        public FiringPositionMemory() { }
    }

    /// <summary>⭐ <c>CE-3136</c> — B2–B5's numbers (the node fills them from its parameters; <see cref="Default"/> = §8.4's column).</summary>
    public struct HeatRules
    {
        /// <summary>Heat halves in this many seconds · burned at · usable again below · a warm spot's score penalty per heat · a
        /// candidate within this of a slot IS that slot (m).</summary>
        public float CoolHalfLifeSeconds, BurnHeat, ReuseHeat, HeatPenalty, MatchRadius;

        /// <summary>The design's defaults (§8.4).</summary>
        public static HeatRules Default => new() { CoolHalfLifeSeconds = 45f, BurnHeat = 2.5f, ReuseHeat = 1.0f, HeatPenalty = 0.3f, MatchRadius = 1.0f };
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-6 (B2–B5) — the heat rules over <see cref="FiringPositionMemory"/>, evaluated lazily at a sim time (deterministic,
    /// replay-exact, no per-tick system). 📄 docs/DESIGN_Peek_And_Fire.md §8.1.
    /// </summary>
    public static unsafe class PositionHeat
    {
        /// <summary>Slot <paramref name="i"/>'s heat at <paramref name="now"/>.</summary>
        public static float HeatNow(ref FiringPositionMemory m, int i, double now, float halfLifeSeconds)
        {
            double dt = Math.Max(0d, now - m.HeatAt[i]);
            return halfLifeSeconds > 0f ? m.Heat[i] * (float)Math.Pow(2d, -dt / halfLifeSeconds) : m.Heat[i];
        }

        /// <summary>B5 — the slot <paramref name="position"/> IS (within <paramref name="radius"/>), or −1.</summary>
        public static int Find(ref FiringPositionMemory m, Vector3 position, float radius)
        {
            for (int i = 0; i < m.Count; i++)
                if (Vector3.DistanceSquared(new Vector3(m.X[i], m.Y[i], m.Z[i]), position) <= radius * radius) return i;
            return -1;
        }

        /// <summary>
        /// B2 — adds <paramref name="heat"/> to <paramref name="position"/>'s slot (a new one when none matches; a full memory
        /// replaces its COOLEST slot, B5), counting a use when <paramref name="exposure"/>. Returns the slot.
        /// </summary>
        public static int Add(ref FiringPositionMemory m, Vector3 position, float heat, double now, in HeatRules r, bool exposure)
        {
            int i = Find(ref m, position, r.MatchRadius);
            if (i < 0)
            {
                if (m.Count < FiringPositionMemory.Capacity) i = m.Count++;
                else
                {
                    i = 0;
                    for (int k = 1; k < m.Count; k++)
                        if (HeatNow(ref m, k, now, r.CoolHalfLifeSeconds) < HeatNow(ref m, i, now, r.CoolHalfLifeSeconds)) i = k;
                }
                m.X[i] = position.X; m.Y[i] = position.Y; m.Z[i] = position.Z;
                m.Heat[i] = 0f; m.HeatAt[i] = now; m.Burned[i] = 0; m.Uses[i] = 0;
            }
            m.Heat[i] = HeatNow(ref m, i, now, r.CoolHalfLifeSeconds) + heat;
            m.HeatAt[i] = now;
            if (exposure && m.Uses[i] < byte.MaxValue) m.Uses[i]++;
            IsBurned(ref m, i, now, in r);
            return i;
        }

        /// <summary>B4 — burned at <c>BurnHeat</c>, usable again only below <c>ReuseHeat</c> (the flag is updated here).</summary>
        public static bool IsBurned(ref FiringPositionMemory m, int i, double now, in HeatRules r)
        {
            float heat = HeatNow(ref m, i, now, r.CoolHalfLifeSeconds);
            if (m.Burned[i] != 0 && heat < r.ReuseHeat) m.Burned[i] = 0;
            else if (m.Burned[i] == 0 && heat >= r.BurnHeat) m.Burned[i] = 1;
            return m.Burned[i] != 0;
        }

        /// <summary>B4/B6 — a candidate's score penalty: 0 when unknown, <c>heat × HeatPenalty</c> when warm, +∞ when burned.</summary>
        public static float Penalty(ref FiringPositionMemory m, Vector3 position, double now, in HeatRules r)
        {
            int i = Find(ref m, position, r.MatchRadius);
            if (i < 0) return 0f;
            return IsBurned(ref m, i, now, in r) ? float.PositiveInfinity : HeatNow(ref m, i, now, r.CoolHalfLifeSeconds) * r.HeatPenalty;
        }

        /// <summary>Is <paramref name="position"/>'s slot burned now?</summary>
        public static bool IsBurnedAt(ref FiringPositionMemory m, Vector3 position, double now, in HeatRules r)
        {
            int i = Find(ref m, position, r.MatchRadius);
            return i >= 0 && IsBurned(ref m, i, now, in r);
        }
    }
}
