using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐⭐ <c>CE-2476</c> (Sensing §8.6e; 🔒 user: "wrong perception breaks many behaviors so i think it deserves extra effort to get it right") — what a
    /// man (or a squad's contact pool) BELIEVES, scored against the real enemies: the four errors of multi-target tracking evaluation.
    /// <list type="bullet">
    ///   <item>an IDENTIFIED contact of a truth entity is matched to it; one of a non-truth entity is ignored (a civilian is not an error);</item>
    ///   <item>a HEARD contact is matched to the nearest live truth within its radius + <c>m</c> (a pooled one carries no radius: <c>m</c> alone);
    ///     matched to none ⇒ a <see cref="Result.Phantom"/>;</item>
    ///   <item>per truth: no contact ⇒ <see cref="Result.Missed"/>; n ≥ 2 ⇒ n − 1 <see cref="Result.Duplicate"/>;</item>
    ///   <item>skipped: a burst (R-2071 — never an enemy claim); with <c>through = false</c> a contact heard only through an opening (D-8).</item>
    /// </list>
    /// It reads the LIVE components the brains read (the editor tier) — ⛔ never the dump, which hides heard contacts.
    /// </summary>
    public static class PerceptionScore
    {
        public readonly record struct Result(int Contacts, int Matched, int Missed, int Phantom, int Duplicate)
        {
            public int Of(string metric) => metric switch
            {
                "contacts" => Contacts, "matched" => Matched, "missed" => Missed, "phantom" => Phantom, "duplicate" => Duplicate,
                _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "contacts, matched, missed, phantom or duplicate"),
            };
        }

        public static readonly IReadOnlyList<string> Metrics = new[] { "contacts", "matched", "missed", "phantom", "duplicate" };

        /// <summary>Scores <paramref name="holder"/>'s beliefs — its <see cref="TargetMemory"/>, or with <paramref name="squad"/> its
        /// <see cref="SquadCognitiveState"/> pool — against <paramref name="truth"/> (each real enemy and where he is now).</summary>
        public static unsafe Result Of(EntityRepository world, Entity holder, IReadOnlyList<(Entity Who, Vector3 At)> truth, bool squad, float m, bool through)
        {
            var hits = new int[truth.Count];
            int contacts = 0, phantom = 0;
            if (squad)
            {
                if (!world.HasComponent<SquadCognitiveState>(holder)) return Tally(contacts, phantom, hits);
                var pool = world.GetComponent<SquadCognitiveState>(holder).Contacts;
                var span = SquadContacts.Slots(in pool);
                for (int i = 0; i < pool.Count && i < span.Length; i++)
                {
                    var c = span[i];
                    bool heard = (c.Flags & SquadContact.AnonymousFlag) != 0 || c.EntityId <= 0;
                    if (heard && !through && (c.Flags & SquadContact.HeardThroughFlag) != 0) continue;
                    Count(c.EntityId, heard, new Vector3(c.PositionX, c.PositionY, c.PositionZ), m, truth, hits, ref contacts, ref phantom);
                }
            }
            else
            {
                if (!world.HasComponent<TargetMemory>(holder)) return Tally(contacts, phantom, hits);
                var mem = world.GetComponent<TargetMemory>(holder);
                for (int i = 0; i < mem.Count; i++)
                {
                    if (TargetMemory.IsBurst(in mem, i)) continue;   // R-2071 — where a round landed, never an enemy
                    bool heard = TargetMemory.IsAnonymous(in mem, i);
                    if (heard && !through && TargetMemory.IsHeardThroughAnOpening(in mem, i)) continue;
                    Count(mem.EntityIds[i], heard, new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]),
                          heard ? mem.Radius[i] + m : 0f, truth, hits, ref contacts, ref phantom);
                }
            }
            return Tally(contacts, phantom, hits);
        }

        private static void Count(long id, bool heard, Vector3 at, float gate, IReadOnlyList<(Entity Who, Vector3 At)> truth, int[] hits,
                                  ref int contacts, ref int phantom)
        {
            if (!heard)
            {
                for (int t = 0; t < truth.Count; t++)
                    if ((long)truth[t].Who.PackedValue == id) { hits[t]++; contacts++; return; }
                return;   // an identified non-enemy: not a claim about the enemy
            }
            contacts++;
            int best = -1; float bestD = float.MaxValue;
            for (int t = 0; t < truth.Count; t++)
            {
                float d = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(truth[t].At.X, truth[t].At.Y));
                if (d <= gate && d < bestD) { best = t; bestD = d; }
            }
            if (best >= 0) hits[best]++; else phantom++;
        }

        private static Result Tally(int contacts, int phantom, int[] hits)
        {
            int matched = 0, missed = 0, duplicate = 0;
            foreach (int h in hits)
            {
                if (h == 0) missed++;
                else { matched++; duplicate += h - 1; }
            }
            return new Result(contacts, matched, missed, phantom, duplicate);
        }
    }
}
