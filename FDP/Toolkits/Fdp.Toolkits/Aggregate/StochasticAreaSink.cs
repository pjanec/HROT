using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8010</c> (ST-3, C-6, RV-8; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §18) — THE STOCHASTIC SINK of
    /// the serial <c>AreaEffectSystem</c>: a burst's lethal circle over a stochastic unit's FOOTPRINT (M-3: frontage × depth, here a circle of
    /// the same area) × the density of its men ⇒ the expected casualties λ = present × overlap / footprint × <see cref="LethalShare"/> × cover
    /// × posture; the draw per present man p = 1 − exp(−λ / n) (seeded by the unit and the detonation); a casualty is a man DOWN; the burst also
    /// suppresses (the overlap's share of a full window) and stresses (the soldier's near-miss / friend-down constants). ⛔ Before, every
    /// <c>UnitEntity</c> was skipped as hidden (<c>AreaEffectSystem.cs:175</c>, <c>PhysicalPresence.cs:26-29</c>) — a unit never felt a burst.
    /// ⭐ CE-8005 (R-171: damage is applied where the health is) — driven by <see cref="StochasticAreaEffectSystem"/> on the node that OWNS the
    /// unit (the Brain group, like its slot table), from the detonation every node receives. ⛔ SUPERSEDED: called at the end of the Muscle's
    /// <c>AreaEffectSystem.Assess</c>, which on a split cluster wrote a slot table the Muscle does not own.
    /// </summary>
    public static class StochasticAreaSink
    {
        /// <summary>CALIBRATED — the share of men inside the lethal circle who become casualties (the rest are wounded below a casualty or
        /// missed by the fragments' pattern).</summary>
        public const float LethalShare = 0.5f;
        /// <summary>CALIBRATED — the least depth (m) a footprint is given (a line has some depth).</summary>
        public const float MinDepth = 20f;
        private const int Salt = 0x8010;

        /// <summary>The lethal radius of a warhead for the sink: its blast lethal radius or half its fragment radius, whichever is larger.</summary>
        public static float LethalRadius(float blastLethal, float fragment) => MathF.Max(blastLethal, fragment * 0.5f);

        /// <summary>The overlap area of two circles (radii <paramref name="r1"/>, <paramref name="r2"/>, centres <paramref name="d"/> apart).</summary>
        public static float Overlap(float r1, float r2, float d)
        {
            if (r1 <= 0f || r2 <= 0f || d >= r1 + r2) return 0f;
            if (d <= MathF.Abs(r1 - r2)) { float r = MathF.Min(r1, r2); return MathF.PI * r * r; }
            float a = r1 * r1 * MathF.Acos(Math.Clamp((d * d + r1 * r1 - r2 * r2) / (2f * d * r1), -1f, 1f));
            float b = r2 * r2 * MathF.Acos(Math.Clamp((d * d + r2 * r2 - r1 * r1) / (2f * d * r2), -1f, 1f));
            float c = 0.5f * MathF.Sqrt(MathF.Max(0f, (-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2)));
            return a + b - c;
        }

        /// <summary>Every stochastic unit the burst at <paramref name="burst"/> reaches (serial). Returns the casualties caused. 0 B.</summary>
        public static int Apply(EntityRepository repo, Vector3 burst, float lethalRadius, uint seq, SimInstant now, bool ownedOnly = false)
        {
            if (lethalRadius <= 0f || !repo.IsComponentTypeRegistered<AggregateUnit>() || !repo.IsComponentTypeRegistered<UnitSlotTable>()) return 0;
            int casualties = 0;
            foreach (var e in repo.Query().With<AggregateUnit>().With<UnitSlotTable>().With<SimTransform>().Build())
            {
                ref var u = ref repo.GetComponentRW<AggregateUnit>(e);
                if (u.Effective != AggregateResolution.Stochastic) continue;
                if (ownedOnly && !repo.HasAuthority<AggregateUnit>(e)) continue;   // ⭐ CE-8005 — only the unit's owner applies it (exactly once)
                var at = (Vector3)repo.GetComponentRO<SimTransform>(e).Position;
                float d = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(burst.X, burst.Y));
                float area = MathF.Max(u.Width, 1f) * MathF.Max(u.Depth, MinDepth);
                float rf = MathF.Sqrt(area / MathF.PI);
                float overlap = Overlap(rf, lethalRadius, d);
                if (overlap <= 0f) continue;
                ref var slots = ref repo.GetComponentRW<UnitSlotTable>(e);
                int present = slots.Present();
                if (present == 0) continue;
                float share = Math.Clamp(overlap / area, 0f, 1f);
                float cover = StochasticCombat.CoverFactor(u.Prepared) * (u.Posture == UnitPosture.Mounted ? StochasticCombat.MountedExposure : 1f);
                float lambda = present * share * LethalShare * cover;
                float p = 1f - MathF.Exp(-lambda / present);
                var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(e.Index, Salt ^ (int)seq, (float)(double)now,
                    repo.HasSingleton<RunSeed>() ? repo.GetSingleton<RunSeed>().Value : Fdp.Toolkit.Determinism.SimRng.RunSeed);
                int down = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (!slots.IsUp(i)) continue;
                    if (rng.NextSingle() >= p) continue;
                    slots.Slots[i].Health = (Half)0f;   // a casualty in the lethal area is down (wounds below it are folded into LethalShare)
                    slots.Slots[i].Stress = (Half)MathF.Min(1f, (float)slots.Slots[i].Stress + StochasticCombat.StressHit);
                    down++;
                }
                float neighbours = Math.Clamp(2f * StochasticCombat.FriendDownMetres / MathF.Max(u.Width, 1f), 0.1f, 1f);
                bool inCover = u.Prepared != PreparedLevel.Unprepared;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (!slots.IsUp(i)) continue;
                    float s = (float)slots.Slots[i].Stress + down * StochasticCombat.StressFriendDown * neighbours
                              + share * StochasticCombat.StressNearMiss * (inCover ? 0.5f : 1f);
                    slots.Slots[i].Stress = (Half)Math.Clamp(s, 0f, 1f);
                }
                // the burst suppresses: its share of a whole window, into the current step's bucket (C-4, the near-miss law)
                int head = u.IncomingHead % AggregateUnit.SuppressionWindowSteps;
                u.IncomingRounds[head] += share * StochasticCombat.SuppressingRoundsPerMan;
                float window = 0f;
                for (int i = 0; i < AggregateUnit.SuppressionWindowSteps; i++) window += u.IncomingRounds[i];
                u.SuppressedShare = Math.Clamp(window / StochasticCombat.SuppressingRoundsPerMan, 0f, 1f);
                u.LastContactAt = now;
                casualties += down;
            }
            return casualties;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8005</c> (R-171) — a burst on a stochastic unit, applied by the node that OWNS the unit: every detonation this node sees, its own
    /// or replicated (the detonation ingress runs on every node, CE-1148), its warhead's lethal circle over the units this node owns
    /// (<see cref="StochasticAreaSink.Apply"/>, <c>ownedOnly</c>). Seeded by the burst itself (its point and munition) and the sim time, so the
    /// draw does not depend on which node assessed it. ⭐ One place: the Muscle's <c>AreaEffectSystem</c> no longer touches a unit. Serial; it
    /// allocates nothing but the query, built once.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StochasticAreaEffectSystem : Fdp.ModuleHost.Abstractions.IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        public StochasticAreaEffectSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(Fdp.ModuleHost.Abstractions.ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>()) return;
            var events = repo.ReadEvents<Fdp.Toolkit.Combat.Contracts.DetonationNotification>();
            if (events.Length == 0 || !repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return;
            var db = repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            var now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.Ammo == 0) continue;
                var (w, _, _) = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(db, evt.Ammo);
                if (w == null || !w.HasAreaEffect) continue;
                StochasticAreaSink.Apply(repo, new Vector3(evt.HitX, evt.HitY, evt.HitZ),
                    StochasticAreaSink.LethalRadius(w.BlastLethalRadiusM, w.FragmentRadiusM), Seed(in evt), now, _gateOnAuthority);
            }
        }

        /// <summary>The burst's own identity: its point and munition (the same bytes on every node).</summary>
        internal static uint Seed(in Fdp.Toolkit.Combat.Contracts.DetonationNotification evt)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(evt.HitX)) * 16777619u;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(evt.HitY)) * 16777619u;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(evt.HitZ)) * 16777619u;
                h = (h ^ (uint)evt.Ammo) * 16777619u;
                return h ^ (uint)(evt.Ammo >> 32);
            }
        }
    }
}
