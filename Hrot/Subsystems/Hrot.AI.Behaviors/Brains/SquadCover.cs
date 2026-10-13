using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2158</c> (F-b/F-c, Squad Tactics §7.6.1) — each mover's cover point for his element's next move, by roster slot: the commander's
    /// one cover query, picked over its whole answer buffer. Part of a play's params (its own memory).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SquadCoverPicks
    {
        public fixed float X[16];
        public fixed float Y[16];
        public fixed float Z[16];
        /// <summary>The roster bits whose pick is a cover point. A mover without his bit set gets the play's own geometric point.</summary>
        public ushort CoverMask;
        /// <summary>Bit e: element e's picks are made (cleared when its move ends).</summary>
        public byte Ready;
    }

    /// <summary>
    /// ⭐ <c>CE-2158</c> — the cover pick both fire and movement (F-b) and bounding overwatch (F-c) use: ask the terrain for cover around an
    /// element's lead mover (hidden from the threat, or from a point where the enemy is expected), then give each mover the answer that brings
    /// him furthest toward the objective within one move — never behind him, never two men on one point.
    /// </summary>
    public static class SquadCover
    {
        /// <summary>A pick must bring the mover at least this much nearer the objective (m).</summary>
        public const float MinProgressMetres = 3f;
        /// <summary>Two movers' picks at least this far apart (m).</summary>
        public const float PickSpacingMetres = 2f;
        /// <summary>The query gives up after this long; the movers keep the play's geometric points (s).</summary>
        public const float PickTimeoutSeconds = 5f;

        public static bool IsReady(in SquadCoverPicks picks, byte element) => (picks.Ready & (1 << element)) != 0;

        public static unsafe bool Has(in SquadCoverPicks picks, int slot, out Vector3 point)
        {
            point = default;
            if (slot >= 16 || (picks.CoverMask & (1 << slot)) == 0) return false;
            point = new Vector3(picks.X[slot], picks.Y[slot], picks.Z[slot]);
            return true;
        }

        /// <summary>Element <paramref name="element"/>'s move ended: its picks are spent.</summary>
        public static void Spend(ref SquadCoverPicks picks, in SquadPlayContext ctx, byte element)
        {
            picks.Ready &= (byte)~(1 << element);
            picks.CoverMask &= (ushort)~SquadGates.Element(in ctx, element);
        }

        /// <summary>
        /// Make element <paramref name="element"/>'s picks: ask (threat entity, else <paramref name="expected"/> as a point) and pick once the
        /// query answers; no lead, or no answer after <see cref="PickTimeoutSeconds"/> since <paramref name="since"/> ⇒ ready with no cover.
        /// Call every tick until <see cref="IsReady"/>.
        /// </summary>
        public static void Make(ref SquadCoverPicks picks, ref SquadPlayState ws, in SquadPlayContext ctx, int site, byte element,
                                Vector3 objective, float moveMetres, Entity threat, Vector3 expected, double since)
        {
            var lead = SquadManeuverRunner.FirstLive(in ctx, element);
            if (lead.IsNull) { picks.Ready |= (byte)(1 << element); return; }
            Span<EqsResult> answers = stackalloc EqsResult[16];
            int count = 0;
            bool answered = threat.IsNull
                ? SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindCoverFromTarget.BlueprintId, site, moveMetres + 5f, lead, expected, out var buffer)
                : SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindCoverFromTarget.BlueprintId, site, moveMetres + 5f, lead, threat, out buffer);
            if (!answered)
            {
                if (ctx.Now - since < PickTimeoutSeconds) return;
                SquadManeuverRunner.DropSensor(ref ws, ctx.World);
            }
            else
            {
                count = Math.Min(buffer.Count, 16);
                buffer.GetSpanRO()[..count].CopyTo(answers);
            }
            Pick(ref picks, in ctx, element, objective, moveMetres, answers[..count]);
        }

        /// <summary>One cover point per live mover of <paramref name="element"/> that has one ahead within <paramref name="moveMetres"/>.</summary>
        public static unsafe void Pick(ref SquadCoverPicks picks, in SquadPlayContext ctx, byte element, Vector3 objective, float moveMetres,
                                       ReadOnlySpan<EqsResult> answers)
        {
            ushort movers = SquadGates.Element(in ctx, element);
            picks.CoverMask &= (ushort)~movers;
            Span<Vector2> taken = stackalloc Vector2[16];
            int takenCount = 0;
            var obj = new Vector2(objective.X, objective.Y);
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if ((movers & (1 << i)) == 0 || !ctx.Live(i, out var m)) continue;
                var me3 = ctx.At(m);
                var me = new Vector2(me3.X, me3.Y);
                float dist = Vector2.Distance(me, obj);
                int best = -1;
                float bestProgress = MinProgressMetres;
                for (int k = 0; k < answers.Length; k++)
                {
                    var pt = new Vector2(answers[k].PositionX, answers[k].PositionY);
                    if (Vector2.Distance(pt, me) > moveMetres + 2f) continue;
                    float progress = dist - Vector2.Distance(pt, obj);
                    if (progress < bestProgress || Near(taken[..takenCount], pt)) continue;
                    best = k; bestProgress = progress;
                }
                if (best < 0) continue;
                picks.X[i] = answers[best].PositionX; picks.Y[i] = answers[best].PositionY; picks.Z[i] = answers[best].PositionZ;
                picks.CoverMask |= (ushort)(1 << i);
                taken[takenCount++] = new Vector2(answers[best].PositionX, answers[best].PositionY);
            }
            picks.Ready |= (byte)(1 << element);
        }

        private static bool Near(ReadOnlySpan<Vector2> taken, Vector2 pt)
        {
            foreach (var t in taken) if (Vector2.Distance(t, pt) < PickSpacingMetres) return true;
            return false;
        }
    }
}
