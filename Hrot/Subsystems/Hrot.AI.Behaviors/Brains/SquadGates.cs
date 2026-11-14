using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2158</c> F-a (Squad Tactics §7.6) — what the commander remembers to read the two gates: each member's rounds at the last
    /// look and when one last left (the "covering" gate), per roster slot. Part of <see cref="SquadPlayState"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FireGateMemory
    {
        /// <summary>Member i's rounds at the last look (−1 = not seen yet).</summary>
        public fixed int Ammo[16];
        /// <summary>Sim time member i's rounds last went down (−1 = not since the play started).</summary>
        public fixed float FiredAt[16];
        /// <summary>1 once <see cref="Ammo"/> holds a first look.</summary>
        public byte Primed;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2158</c> F-a (R-2013, Squad Tactics §7.6) — the commander's two gates, read off what it can see of its members (never a
    /// message between them): <b>covering</b> — every coverer fired within <see cref="CoveringSeconds"/> (his rounds went down);
    /// <b>set</b> — every mover stopped at his point, and that point was a cover point (or he is prone). The plays feed them to the
    /// library shell as <c>ShotFired</c> / <c>DefiladeReached</c>.
    /// </summary>
    public static class SquadGates
    {
        /// <summary>A coverer "is covering" when a round left him this recently (s).</summary>
        public const float CoveringSeconds = 2f;

        /// <summary>Look at every live member's rounds once per tick: a drop since the last look stamps <see cref="FireGateMemory.FiredAt"/>.</summary>
        public static unsafe void Track(ref FireGateMemory gate, in SquadPlayContext ctx)
        {
            if (gate.Primed == 0)
            {
                for (int i = 0; i < 16; i++) { gate.Ammo[i] = -1; gate.FiredAt[i] = -1f; }
                gate.Primed = 1;
            }
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if (!ctx.Live(i, out var m) || !ctx.World.HasComponent<WeaponState>(m)) continue;
                int ammo = ctx.World.GetComponentRO<WeaponState>(m).Ammo;
                if (gate.Ammo[i] >= 0 && ammo < gate.Ammo[i]) gate.FiredAt[i] = (float)ctx.Now;
                gate.Ammo[i] = ammo;
            }
        }

        /// <summary>Member <paramref name="i"/> fired within <see cref="CoveringSeconds"/>.</summary>
        public static unsafe bool Fired(in FireGateMemory gate, int i, double now)
            => i < 16 && gate.FiredAt[i] >= 0f && now - gate.FiredAt[i] <= CoveringSeconds;

        /// <summary>
        /// The "covering" gate over the members whose roster bits are set in <paramref name="coverers"/>: each one in the fight who
        /// carries a weapon has fired within <see cref="CoveringSeconds"/>. True when none of them can fire (nothing to wait for).
        /// </summary>
        public static bool Covering(in FireGateMemory gate, in SquadPlayContext ctx, ushort coverers)
        {
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if ((coverers & (1 << i)) == 0 || !ctx.Live(i, out var m) || !ctx.World.HasComponent<WeaponState>(m)) continue;
                if (ctx.World.GetComponentRO<WeaponState>(m).Ammo <= 0) continue;   // out of rounds: cannot cover, not waited for
                if (!Fired(in gate, i, ctx.Now)) return false;
            }
            return true;
        }

        /// <summary>⭐ <c>CE-2158</c> F-b — a mover "is set" this close to the point his order sent him to (m).</summary>
        public const float SetMetres = 1.5f;

        /// <summary>
        /// ⭐ <c>CE-2158</c> F-a — the "set" gate over the members whose roster bits are set in <paramref name="movers"/>: each live one
        /// stands within <paramref name="metres"/> of the point his order sent him to (<see cref="SquadPlayState"/>'s targets). Whether
        /// that point was cover is the play's pick (the trace shows it per order). True when nobody is left to wait for.
        /// </summary>
        public static unsafe bool Set(in SquadPlayState ws, in SquadPlayContext ctx, ushort movers, float metres)
        {
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if ((movers & (1 << i)) == 0 || !ctx.Live(i, out var m)) continue;
                var at = ctx.At(m);
                float dx = at.X - ws.TargetX[i], dy = at.Y - ws.TargetY[i];
                if (dx * dx + dy * dy > metres * metres) return false;
            }
            return true;
        }

        /// <summary>The roster bits of the live members of <paramref name="element"/>.</summary>
        public static ushort Element(in SquadPlayContext ctx, byte element)
        {
            ushort mask = 0;
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
                if (squad.Elements.MemberElements[i] == element && ctx.Live(i, out _)) mask |= (ushort)(1 << i);
            return mask;
        }
    }
}
