using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Convoy
{
    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> (C-4, C-8; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md §3) — this vehicle is in a road convoy: the column's LEAD, its place
    /// (<see cref="Rank"/> 0 = the lead itself) and the spacing. Written by the vehicle's OWN behaviour (own entity, R-4212); the kinematic owner's
    /// <see cref="Systems.ConvoyLeadSystem"/> / <see cref="Systems.ConvoyFollowSystem"/> drive the column from it. Unmanaged (recorded, R-2084 "NEW COMPONENTS").
    /// </summary>
    [ComponentId(GlobalComponentIds.ConvoyMember)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ConvoyMember
    {
        /// <summary>The column's lead (rank 0 holds itself).</summary>
        public Entity Lead;
        /// <summary>Centre-to-centre spacing to the vehicle ahead, measured ALONG the lead's path (m).</summary>
        public float Gap;
        /// <summary>At or under this (along the path, centre to centre) the vehicle stops — it never closes further (m).</summary>
        public float MinGap;
        /// <summary>The place in the column: 0 = the lead, 1 = right behind it …</summary>
        public byte Rank;
        /// <summary><see cref="ConvoyMode"/>.</summary>
        public byte Mode;
        /// <summary>Bit 0: the lead is a priority vehicle (beacon, siren — C-1).</summary>
        public byte Flags;

        public const float DefaultGap = 15f;
        public const float DefaultMinGap = 7f;

        public bool IsLead => Rank == 0;
    }

    /// <summary>How the lead drives (C-9): on the SUMO network (handed to SUMO), or on our own route.</summary>
    public enum ConvoyMode : byte
    {
        /// <summary>Traffic when the lead can be handed to SUMO, else our route.</summary>
        Auto = 0,
        /// <summary>Mode 1 — the lead handed to SUMO.</summary>
        Traffic = 1,
        /// <summary>Mode 2 — our own route (off-road, joining and leaving the road graph).</summary>
        Route = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> (C-4) — a follower's place on the lead's trail, kept by <see cref="Systems.ConvoyFollowSystem"/> on the kinematic owner (local, recorded,
    /// never on the wire — C-8). <see cref="ArcS"/> is on the trail's own arc-length axis.
    /// </summary>
    [ComponentId(GlobalComponentIds.ConvoyProgress)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ConvoyProgress
    {
        /// <summary>This vehicle's position along the lead's trail (m, the trail's axis).</summary>
        public double ArcS;
        /// <summary>1 once the vehicle has reached the trail (before that it drives to the trail's oldest point).</summary>
        public byte OnTrail;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> (C-4, C-6, C-8) — the LEAD's actual path: a ring of the last <see cref="Capacity"/> positions, one every <see cref="Spacing"/> m,
    /// with their arc length — every follower drives this, so the column cannot split. Also the column's state the lead's system gathers each frame:
    /// each rank's place and speed on the trail (the followers read their predecessor's from here), and the lead's speed CAP from the tail's lag.
    /// Local to the kinematic owner (recorded, never on the wire — C-8). Unmanaged, fixed size: 0 B per frame.
    /// </summary>
    [ComponentId(GlobalComponentIds.ConvoyTrail)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ConvoyTrail
    {
        public const int Capacity = 256;
        public const int MaxRanks = 32;
        /// <summary>A point is appended once the lead is this far from the newest (m).</summary>
        public const float Spacing = 2f;

        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed double S[Capacity];
        /// <summary>The newest point's physical slot · how many points are held.</summary>
        public int Head;
        public int Count;

        /// <summary>Each rank's position along the trail and its speed, as the lead's system read them at the start of this frame; <see cref="RankValid"/> bit per rank.</summary>
        public fixed double RankS[MaxRanks];
        public fixed float RankV[MaxRanks];
        public uint RankValid;
        /// <summary>The highest rank present.</summary>
        public byte LastRank;

        /// <summary>⭐ C-6 — the lead's speed is capped to this fraction of its top speed (1 = no cap) while the column is stretched.</summary>
        public float SpeedCapFactor;
        /// <summary>The tail's lag behind its place (m) — the cap's input, kept for the map and the rails.</summary>
        public float TailLag;

        public ConvoyTrail() { SpeedCapFactor = 1f; }

        /// <summary>The physical slot of logical point <paramref name="j"/> (0 = the oldest).</summary>
        public readonly int Slot(int j) => (Head - Count + 1 + j + Capacity * 2) % Capacity;

        public readonly double OldestS => Count == 0 ? 0 : S[Slot(0)];
        public readonly double NewestS => Count == 0 ? 0 : S[Head];
        public readonly Vector2 Point(int j) { int k = Slot(j); return new Vector2(X[k], Y[k]); }
        public readonly double ArcOf(int j) => S[Slot(j)];

        /// <summary>Appends the lead's position when it is <see cref="Spacing"/> from the newest point (the first call starts the trail at 0).</summary>
        public void Record(Vector2 p)
        {
            if (Count == 0) { Head = 0; X[0] = p.X; Y[0] = p.Y; S[0] = 0; Count = 1; return; }
            var last = new Vector2(X[Head], Y[Head]);
            float d = Vector2.Distance(p, last);
            if (d < Spacing) return;
            double s = S[Head] + d;
            Head = (Head + 1) % Capacity;
            X[Head] = p.X; Y[Head] = p.Y; S[Head] = s;
            if (Count < Capacity) Count++;
        }

        /// <summary>The point at arc length <paramref name="s"/> (clamped to the held points) and the path's direction there.</summary>
        public readonly (Vector2 Pos, Vector2 Dir) Sample(double s)
        {
            if (Count == 0) return (Vector2.Zero, Vector2.UnitX);
            if (Count == 1) return (Point(0), Vector2.UnitX);
            if (s <= OldestS) { var a = Point(0); var b = Point(1); return (a, Dir(a, b)); }
            if (s >= NewestS) { var a = Point(Count - 2); var b = Point(Count - 1); return (b, Dir(a, b)); }
            int lo = 0, hi = Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (ArcOf(mid) <= s) lo = mid; else hi = mid; }
            var p0 = Point(lo); var p1 = Point(hi);
            double s0 = ArcOf(lo), s1 = ArcOf(hi);
            float t = s1 > s0 ? (float)((s - s0) / (s1 - s0)) : 0f;
            return (Vector2.Lerp(p0, p1, t), Dir(p0, p1));
        }

        /// <summary>
        /// The arc length of the trail's point nearest <paramref name="p"/>, searching only within [<paramref name="from"/>, <paramref name="to"/>] (the window
        /// around a follower's last place — a trail that crosses itself never makes it jump). Returns false when no segment lies in the window.
        /// </summary>
        public readonly bool Project(Vector2 p, double from, double to, out double arc, out float distance)
        {
            arc = 0; distance = float.MaxValue;
            if (Count < 2) return false;
            int lo = 0, hi = Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (ArcOf(mid) <= from) lo = mid; else hi = mid; }
            bool any = false;
            for (int j = lo; j < Count - 1; j++)
            {
                double s0 = ArcOf(j);
                if (s0 > to) break;
                var a = Point(j); var b = Point(j + 1);
                var ab = b - a; float len2 = ab.LengthSquared();
                float t = len2 > 1e-6f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                if (d < distance) { distance = d; arc = s0 + (ArcOf(j + 1) - s0) * t; any = true; }
            }
            return any;
        }

        public readonly double RankArc(int r) => RankS[r];
        public readonly float RankSpeed(int r) => RankV[r];
        public readonly bool HasRank(int r) => r >= 0 && r < MaxRanks && (RankValid & (1u << r)) != 0;

        private static Vector2 Dir(Vector2 a, Vector2 b) { var d = b - a; return d.LengthSquared() > 1e-8f ? Vector2.Normalize(d) : Vector2.UnitX; }
    }
}
