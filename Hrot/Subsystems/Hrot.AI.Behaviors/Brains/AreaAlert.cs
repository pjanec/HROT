using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2371</c> (AQ104 D, R-2079) — who keeps a man ALERTED (<see cref="Alerts"/>): his attack order's target, and every enemy the
    /// squad knows INSIDE the area its defence or clearing holds. Each call renews a short lease; a task that stops calling lets him forget
    /// normally. A lease is renewed only while the enemy is in the fight and the man's own memory has evidence of him from the last
    /// <see cref="EvidenceSeconds"/> — 🔒 user: <i>"they should stay alerted until the enemy is incapacitated"</i>.
    /// 📄 docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md §4 D.
    /// </summary>
    public static class AreaAlert
    {
        /// <summary>No new evidence of him this long (s) ⇒ the alert lapses (AQ104 D: 10 min).</summary>
        public const double EvidenceSeconds = 600.0;

        /// <summary>Renews <paramref name="man"/>'s alert on <paramref name="target"/> when he is still in the fight and remembered with recent
        /// evidence. True when pinned.</summary>
        public static unsafe bool Keep(EntityRepository world, Entity man, Entity target, double now)
        {
            if (target.IsNull || !world.IsAlive(target) || ThreatDanger.IsOutOfTheFight(world, target)) return false;
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(man)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(man);
            long id = (long)target.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != id || TargetMemory.IsAnonymous(in mem, i)) continue;
                if (SecondsSince(world, mem.LastSeenTick[i]) > EvidenceSeconds) return false;
                return Alerts.Pin(world, man, id, now);
            }
            return false;
        }

        /// <summary>Every live member of the squad stays alerted on every identified enemy in the squad's picture within
        /// <paramref name="radius"/> of <paramref name="centre"/> (the defended position, the building being cleared).</summary>
        public static unsafe void KeepInArea(in SquadPlayContext ctx, Vector3 centre, float radius)
        {
            if (!ctx.World.IsComponentTypeRegistered<Alerted>() || !ctx.World.HasComponent<SquadCognitiveState>(ctx.Commander)) return;
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0) continue;
                var where = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
                if (Flat(where - centre) > radius) continue;
                var enemy = new Entity((ulong)pool[c].EntityId);
                if (ThreatDanger.IsNeutral(ctx.World, enemy)) continue;
                for (int i = 0; i < ctx.Roster.Count; i++)
                    if (ctx.Live(i, out var m)) Keep(ctx.World, m, enemy, ctx.Now);
            }
        }

        /// <summary>Sim seconds since a perception tick (frames × the run's seconds per frame; ticks are <c>PerceptionClock.Now</c>).</summary>
        private static double SecondsSince(EntityRepository world, uint tick)
        {
            uint now = PerceptionClock.Now(world);
            if (tick >= now || !world.HasSingleton<GlobalTime>()) return 0d;
            var t = world.GetSingleton<GlobalTime>();
            double spf = t.FrameNumber > 0 && t.TotalTime > 0 ? t.TotalTime / t.FrameNumber : 1.0 / 60.0;
            return (now - tick) * spf;
        }

        private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);
    }
}
