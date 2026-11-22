using System;
using Fdp.Core;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>The rule's answer for one burst: how many rounds, and the pause after the last (0 = none asked: the asked interval stands).</summary>
    public readonly struct BurstPlan
    {
        /// <summary>Rounds in the burst; 0 = no plan (the mount keeps its own rule — a machine gun is cut by barrel heat, §5k).</summary>
        public readonly byte Rounds;
        /// <summary>Seconds after the last round (before the per-man jitter); only for <see cref="Rounds"/> &gt; 1.</summary>
        public readonly float PauseSeconds;
        public BurstPlan(byte rounds, float pauseSeconds) { Rounds = rounds; PauseSeconds = pauseSeconds; }
        /// <summary>No plan: the mount keeps today's rule.</summary>
        public static BurstPlan None => default;
    }

    /// <summary>
    /// ⭐ <c>CE-11033</c> (R-11012; 📄 docs/DESIGN_Weapon_Layers.md §5l) — the ONE rule for the AI's short bursts, and the burst bookkeeping. Deterministic: <see cref="SimRng"/> per shooter and burst, no wall clock, no static
    /// mutable state (it runs in the brains' parallel phase). 🔒 User, 2026-11-22: <i>"Whenever Hrot actually fires a burst and knows that it
    /// will fire a burst … cannot apply to player, because we never know how long burst the player is going to do."</i>
    /// </summary>
    public static class FireBursts
    {
        /// <summary>A target beyond this is a single aimed round (a rifle's deliberate fire).</summary>
        public const float FarMetres = 150f;
        /// <summary>Inside this, or against a moving target, the burst is 2–3 rounds.</summary>
        public const float NearMetres = 50f;
        /// <summary>A mount slower than this cyclic gap (s) fires no bursts — the rule's pause must exceed the jittered cyclic gap.</summary>
        public const float MaxCyclicGapSeconds = 0.5f;
        private const int Salt = 0x11033001;

        /// <summary>
        /// The rules table. <paramref name="burstId"/> seeds the draw (with the shooter), so the same shooter's same burst is the same every run.
        /// SmallArms: blind / suppressing 3–5 with a longer pause (1.5–2.5 s over the cyclic gap) · beyond <see cref="FarMetres"/> 1 · inside
        /// <see cref="NearMetres"/> or the target moving 2–3 · else 1–2 (pause 0.6–1.2 s over the cyclic gap). MachineGun / Autocannon: none
        /// (§5k). Any other category: 1. A SmallArms mount with no cyclic rate, or slower than <see cref="MaxCyclicGapSeconds"/>: 1.
        /// </summary>
        public static BurstPlan Choose(EntityRepository world, Entity shooter, Entity target, WeaponMountDto? mount, bool blind, uint burstId)
        {
            var category = mount?.Category ?? WeaponCategory.Unknown;
            if (category == WeaponCategory.MachineGun || category == WeaponCategory.Autocannon) return BurstPlan.None;
            if (category != WeaponCategory.SmallArms) return new BurstPlan(1, 0f);
            float cyclic = Cyclic(mount);
            if (cyclic <= 0f || cyclic > MaxCyclicGapSeconds) return new BurstPlan(1, 0f);

            var rng = SimRng.FromSim(shooter.Index, Salt ^ unchecked((int)(burstId * 7919u)), 0f);
            float r1 = rng.NextSingle(), r2 = rng.NextSingle();
            if (blind) return new BurstPlan((byte)(3 + (int)(r1 * 3f) % 3), cyclic + 1.5f + r2);

            bool far = false, near = false, moving = false;
            if (!target.IsNull && world.HasComponent<SimTransform>(shooter) && world.HasComponent<SimTransform>(target))
            {
                float d = (world.GetComponentRO<SimTransform>(target).Position - world.GetComponentRO<SimTransform>(shooter).Position).Length();
                far = d > FarMetres; near = d < NearMetres;
                moving = !far && HitModel.IsMoving(world, target);
            }
            if (far) return new BurstPlan(1, 0f);
            float pause = cyclic + 0.6f + r2 * 0.6f;
            return near || moving ? new BurstPlan((byte)(2 + (int)(r1 * 2f) % 2), pause) : new BurstPlan((byte)(1 + (int)(r1 * 2f) % 2), pause);
        }

        /// <summary>
        /// ⭐ The gap after a burst's last round. 🔒 User, 2026-11-22: <i>"burst everywhere with the same average rate of fire"</i> — with an asked
        /// interval, a burst of N rounds takes N asked intervals in all (N-1 cyclic gaps, then the rest), so a man fires as many rounds per minute as
        /// before, only bunched. With no asked interval (0 = the mount's own rate) the rule's <paramref name="rulePause"/> stands.
        /// </summary>
        public static float PauseAfter(byte rounds, float cyclic, float asked, float rulePause)
            => asked > 0f ? Math.Max(asked * rounds - cyclic * (rounds - 1), cyclic) : Math.Max(rulePause, cyclic);

        /// <summary>The mount's own cyclic gap in seconds (60 / rounds per minute); 0 when it states no rate.</summary>
        public static float Cyclic(WeaponMountDto? mount) => mount is { RateOfFire: > 0f } m ? 60f / m.RateOfFire : 0f;

        /// <summary>
        /// ⭐ The brain's burst, kept in the action's own channel state (4 bytes — over the turret path's <c>Accounted</c> int, which the per-shot
        /// path never reads; <c>OnEnter</c> zeroes it). Nothing is written to any component.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct BurstState
        {
            /// <summary>Which burst of this action (1 … 255, wrapping to 1); 0 = none yet.</summary>
            public byte Seq;
            /// <summary>Rounds of the open burst (0 = no planned burst open).</summary>
            public byte Rounds;
            /// <summary>Rounds of it already stamped.</summary>
            public byte Fired;
            /// <summary>Bit 7 = "this is a burst state" (<see cref="Valid"/>); bits 0-6 = the rule's pause after the burst, in 1/20 s (≤ 6.35 s).
            /// The turret path's <c>Accounted</c> int shares these bytes and is never negative, so its bit 31 (this byte's bit 7) is always 0: a
            /// state the turret path left behind reads as NOT valid, and the per-shot path starts clean.</summary>
            public byte PauseTicks;
            public const byte ValidBit = 0x80;
            public readonly bool Valid => (PauseTicks & ValidBit) != 0;
        }

        /// <summary>What one round carries to its <c>WeaponFireIntent</c>: <c>BurstId</c> 0 = no planned burst.</summary>
        public readonly struct BurstStamp
        {
            public readonly uint Id; public readonly byte Rounds; public readonly byte Index;
            public BurstStamp(uint id, byte rounds, byte index) { Id = id; Rounds = rounds; Index = index; }
        }

        /// <summary>The burst id of burst <paramref name="seq"/> of action <paramref name="actionId"/> — unique per shooter, never 0 for a planned burst.</summary>
        public static uint IdOf(uint actionId, byte seq) => unchecked((actionId << 8) | seq);

        /// <summary>
        /// ⭐ One AI round is about to be asked by <paramref name="shooter"/>'s own brain: advance its <paramref name="state"/>, return what the round
        /// carries (<see cref="BurstStamp"/>) and the gap to wait AFTER it, in seconds BEFORE the per-man jitter. Opens a burst when none is open (asks
        /// <see cref="Choose"/>). Inside a planned burst the gap is the mount's cyclic gap, after its last round the rule's pause (never under
        /// <paramref name="asked"/>). A 1-round plan or a mount with no plan (a machine gun) stamps nothing and keeps <paramref name="asked"/> as today.
        /// </summary>
        public static BurstStamp Step(EntityRepository world, Entity shooter, Entity target, WeaponMountDto? mount, bool blind,
                                      uint actionId, ref BurstState state, float asked, out float gap)
        {
            gap = asked;
            if (!state.Valid) state = default;   // ⭐ the turret path's Accounted lived here (CE-11033 corner): start clean
            if (state.Fired >= state.Rounds)
            {
                byte seq = state.Seq == 255 ? (byte)1 : (byte)(state.Seq + 1);
                var plan = Choose(world, shooter, target, mount, blind, IdOf(actionId, seq));
                state.Seq = seq; state.Fired = 0;
                state.Rounds = plan.Rounds >= 2 ? plan.Rounds : (byte)0;
                state.PauseTicks = (byte)(BurstState.ValidBit | Math.Min(127, (int)(plan.PauseSeconds * 20f + 0.5f)));
                if (state.Rounds == 0) return default;
            }
            byte index = ++state.Fired;
            gap = index < state.Rounds ? Cyclic(mount) : PauseAfter(state.Rounds, Cyclic(mount), asked, (state.PauseTicks & 0x7F) / 20f);
            return new BurstStamp(IdOf(actionId, state.Seq), state.Rounds, index);
        }
    }
}
