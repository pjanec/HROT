using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Creatures
{
    // ⭐⭐ CE-4628 (docs/designs/animals/DESIGN_Animals.md §10, §10a) — the components a CIVILIAN and an ANIMAL share. ⭐ No logic moved: each
    //   system computes what it computed before; only WHERE a creature's fear, home, group and mood are kept is one place now. Every
    //   reshaped component KEEPS its id (the playback skips a recorded component whose layout differs — §10a).

    /// <summary>
    /// ⭐⭐ <c>CE-4628</c> — a creature's FEAR, kept by the ONE fear system (<c>CivilianFearSystem</c>): 0 calm … 1 terrified / blind panic.
    /// The union of the civilian's fear fields (was on <c>CivilianState</c>) and the animal's (was <c>Animals.Fear</c>). ⛔ BRAIN-LOCAL: written
    /// on the node that runs the creature's brain, never replicated (every other node sees <see cref="Demeanour"/>), never saved.
    /// Added WITH the creature's mind (<c>CivilianMind.Ensure</c> / <c>AnimalTkbTranslator</c>), so no system adds it per frame.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Fear)]
    [DataPolicy(DataPolicy.NoScenario)]   // ⭐ run-time state: recorded and snapshotted, never written into a saved scenario (rebuilt at run start)
    public struct Fear
    {
        /// <summary>0 = calm … 1 = terrified (a civilian) / blind panic (an animal).</summary>
        public float Value;
        /// <summary>Where the last fright came from (a shot, the man walking up, the tank) — the creature runs AWAY from here; valid when <see cref="HasFrightFrom"/> is 1.</summary>
        public WorldPoint FrightFrom;
        /// <summary>Sim time of the last thing that frightened it (a fright this frame is not faded in the same frame); 0 when nothing has.</summary>
        public SimInstant LastFrightAt;
        /// <summary>⭐ <c>CE-2237</c> — sim time a civilian last HEARD violence (a shot, a blast); 0 when he never has. Tells a fright he heard from one he caught from his crowd.</summary>
        public SimInstant HeardAt;
        /// <summary>⭐ <c>CE-2239</c> part 2 (FL-6, §5j) — the latest end of a stun or an irritation (<c>EffectState</c>) that has frightened it already, so a running effect frightens once and only a new or longer one again.</summary>
        public SimInstant EffectFrightUntil;
        /// <summary>1 once <see cref="FrightFrom"/> holds a place.</summary>
        public byte HasFrightFrom;
        /// <summary>1 when the last fright was the FLIGHT ZONE (a threat seen near), 0 when it was heard or felt.</summary>
        public byte Seen;
        private byte _pad0, _pad1;
    }

    /// <summary>
    /// ⭐ <c>CE-4623</c> (AN-5, AN-16, AR-16) / <c>CE-4628</c> — a creature's HOME: the centre it keeps to and the radius it wanders within; an optional TETHER
    /// (a dog's chain: the length it can NEVER exceed, enforced on the motion). <see cref="Enclosed"/> = an enclosed pen it never leaves (AN-18). Was
    /// <c>AnimalHome</c>; a civilian's home (where he stood on his first tick) is the same component now. <c>NoScenario</c> for both: rebuilt at run start,
    /// recorded so a replay and a seek have it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Home)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Home
    {
        public WorldPoint Centre;
        public float Radius;
        /// <summary>0 = no tether; else the chain's length from <see cref="Centre"/> (m).</summary>
        public float TetherMetres;
        /// <summary>The area entity it was populated from (Null for an authored one and for a civilian).</summary>
        public Entity Area;
        public byte Enclosed;
        private byte _pad0;
        private ushort _pad1;
    }

    /// <summary>
    /// ⭐ <c>CE-4623</c> (AR-9) / <c>CE-4628</c> — the group a creature belongs to: its herd (a light herd entity, NEVER a <c>UnitRoster</c>) or its crowd (the
    /// <c>Crowd</c> entity). <see cref="Entity.Null"/> = none. Was <c>HerdMember.Herd</c> and the civilian's int <c>CrowdId</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GroupMember)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct GroupMember
    {
        public Entity Group;
    }

    /// <summary>What a <see cref="Group"/> entity is.</summary>
    public enum GroupKind : byte { Herd = 0, Crowd = 1 }

    /// <summary>
    /// ⭐ <c>CE-4628</c> — the header every group entity carries, whatever kind: the common part of <c>HerdState</c> and <c>Crowd</c>. The entity is the id.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Group)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Group
    {
        public GroupKind Kind;
        /// <summary>Its living members.</summary>
        public int Count;
        /// <summary>The mean position of its members.</summary>
        public WorldPoint Centre;
        /// <summary>A herd's lead animal; <see cref="Entity.Null"/> for a crowd.</summary>
        public Entity Lead;
    }

    /// <summary>
    /// ⭐ <c>CE-4628</c> §10c — how a creature APPEARS to others, ONE replicated record (Brain group, <c>dtDemeanour</c> 153, id 435): the GESTURE he makes
    /// (<see cref="Kind"/>, was the <c>Gesture</c> component, CE-3224 — written only through <see cref="Gestures.Set"/>) and how he SEEMS (<see cref="Mode"/>, fear and
    /// anger in quarters 0 … 4, CE-3265 G2 — written only through <see cref="Demeanours.SetMood"/>). ⭐ Two writers that never touch each other's fields; each ADDS the
    /// record with its final value when missing (the parallel rule). The brain-private <see cref="Fear"/> and <c>CivilianState</c> stay private.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Demeanour)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Demeanour
    {
        /// <summary>The gesture he makes now (None = none; the idle kinds are his mood shown). Written by <see cref="Gestures"/> only.</summary>
        public GestureKind Kind;
        public CivilianMode Mode;
        /// <summary>Fear in quarters: 0 calm … 4 terrified.</summary>
        public byte Fear;
        /// <summary>Anger in quarters: 0 … 4.</summary>
        public byte Anger;
    }

    /// <summary>⭐ <c>CE-3265</c> — the one writer of <see cref="Demeanour"/>'s MOOD half (Mode, Fear, Anger — never <see cref="Demeanour.Kind"/>) and the reader of the whole record. The writer's CALL SITE is behaviors' (§5o,
    /// <c>CE-2250</c>: <c>CrowdSystem</c>'s pass).</summary>
    public static class Demeanours
    {
        /// <summary>0 … 1 in quarters: 0 · 1 (≥ 0.125) · 2 (≥ 0.375) · 3 (≥ 0.625) · 4 (≥ 0.875).</summary>
        public static byte Quarter(float value) => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 4f, MidpointRounding.AwayFromZero), 0, 4);

        /// <summary>Writes <paramref name="man"/>'s mood when it CHANGED (the mode, or a quarter of fear or anger); true when it wrote. Only Mode / Fear / Anger are
        /// written — his gesture is kept; a missing record is ADDED with <see cref="GestureKind.None"/> and the mood. Call it on the node that owns his brain.</summary>
        public static bool SetMood(EntityRepository repo, Entity man, CivilianMode mode, float fear, float anger)
        {
            if (!repo.IsAlive(man) || !repo.IsComponentTypeRegistered<Demeanour>()) return false;
            byte q = Quarter(fear), a = Quarter(anger);
            if (repo.HasComponent<Demeanour>(man))
            {
                // ⭐ CE-4628 — compared READ-ONLY first: a RW reference bumps the chunk version (the recorder's and the egress' change mark) even when nothing changed
                ref readonly var cur = ref repo.GetComponentRO<Demeanour>(man);
                if (cur.Mode == mode && cur.Fear == q && cur.Anger == a) return false;
                ref var w = ref repo.GetComponentRW<Demeanour>(man);
                w.Mode = mode; w.Fear = q; w.Anger = a;   // never Kind
                return true;
            }
            repo.AddComponent(man, new Demeanour { Kind = GestureKind.None, Mode = mode, Fear = q, Anger = a });
            return true;
        }

        /// <summary>The man's mood on this node (the owner's or the replicated one); <c>false</c> when he has none.</summary>
        public static bool Of(ISimulationView view, Entity man, out Demeanour demeanour)
        {
            demeanour = default;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Demeanour>()) return false;
            if (!view.IsAlive(man) || !view.HasComponent<Demeanour>(man)) return false;
            demeanour = view.GetComponentRO<Demeanour>(man);
            return true;
        }
    }
}
