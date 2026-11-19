using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Creatures;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2235</c> (CV-3; docs/DESIGN_Civilians_And_Crowd_Control.md §2.2, §5d) — what a civilian's HSM is doing, one value per state
    /// of the civilian machine. The HSM writes it from the state it is in, so a reader that is not the HSM (the crowd, a live check, a
    /// debugger) does not need the machine's metadata. Slice 1 runs <see cref="Routine"/>, <see cref="Alarmed"/> and <see cref="Casualty"/>;
    /// the rest are the later slices' states (CE-2236 … CE-2238); <see cref="Gather"/> is CE-2237's.
    /// </summary>
    public enum CivilianMode : byte
    {
        None = 0,
        Routine = 1,
        Alarmed = 2,
        Panic = 3,
        Shelter = 4,
        Inside = 5,
        Cower = 6,
        Casualty = 7,
        Protest = 8,
        Riot = 9,
        Dispersing = 10,
        ArmedHostile = 11,
        /// <summary>⭐ <c>CE-2237</c> — an onlooker: he walked over to an incident and stands looking (§5f).</summary>
        Gather = 12,
        /// <summary>⭐ <c>CE-2241</c> ① — a man who committed a hostile act, covered by the forces: hands up, standing still (§5h.3).</summary>
        Surrender = 13,
        /// <summary>⭐ <c>CE-7030</c> (City Battle §3c) — CURIOUS: fighting heard far off (not close enough to frighten him); he stops and looks,
        /// some film it with a phone.</summary>
        Watch = 14,
    }

    /// <summary>
    /// ⭐ <c>CE-2235</c> (CV-3, CV-4; §5d) — a civilian's mind: his <see cref="Anger"/> and hidden <see cref="Attitude"/> (the crowd and the
    /// hostile-act slices) and the <see cref="Mode"/> his HSM is in. ⭐ <c>CE-4628</c>: his FEAR (<see cref="Fear"/>), his home (<see cref="Home"/>) and
    /// the crowd he stands in (<see cref="GroupMember"/>) are the components he shares with an animal — added WITH this one
    /// (<see cref="CivilianMind.Ensure"/>). ⛔ BRAIN-LOCAL: it lives on the node that runs his <c>CivilianHsm</c>, is never replicated (others see what
    /// he DOES — his side, his gesture, his cowering — and his <see cref="Demeanour"/>) and never saved. Added by the HSM on its first tick.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CivilianState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CivilianState
    {
        /// <summary>0 … 1 — raised by the crowd and by force used on him (CE-2237, CE-2241). Unused in slice 1.</summary>
        public float Anger;
        /// <summary>−1 (friendly) … +1 (hostile at heart) — hidden; his side flips only on a hostile ACT (CV-4). ⭐ <c>CE-2241</c> ② (§5m
        /// PV-3): it sets his riot threshold; written by his HSM from its <c>Attitude</c> param.</summary>
        public float Attitude;
        /// <summary>The state his HSM is in.</summary>
        public CivilianMode Mode;
        /// <summary>⭐ <c>CE-2237</c> — the incident that draws him (the nearest person hurt within <see cref="CrowdRules.AttractMetres"/>
        /// while he is calm); valid when <see cref="HasFocus"/> is 1. Written by <see cref="CrowdSystem"/>, read by his Gather state.</summary>
        public Vector3 Focus;
        public byte HasFocus;
        /// <summary>⭐ <c>CE-2238</c> (§5i) — 1 while he is an INSTIGATOR: his anger is 1 and he raises the anger of his crowd near him
        /// (<see cref="CrowdSystem"/>). Written by his HSM from its params.</summary>
        public byte Instigator;
        /// <summary>⭐ <c>CE-2238</c> (§5i, CV-4) — sim time (s) of his first HOSTILE ACT (a blow); 0 when he committed none. Brain-local;
        /// the act and its kind replicate as <see cref="HostileAct"/> (<c>CE-3238</c>, B-16). Written by <see cref="HostileActs.Commit"/>.</summary>
        public SimInstant HostileActAt;
        /// <summary>⭐ <c>CE-2241</c> ② (§5m PV-2) — the latest end of a knock-down, stun or irritation on him that has already spiked his
        /// neighbours' anger, so one blow spikes it once. Written by <see cref="CrowdSystem"/>.</summary>
        public SimInstant ForceSeenUntil;
    }

    /// <summary>⭐ <c>CE-2235</c> — the one writer of <see cref="CivilianState"/>'s presence and mode.</summary>
    public static class CivilianMind
    {
        /// <summary>Adds <see cref="CivilianState"/> — and the shared <see cref="Fear"/>, <see cref="Home"/> (where he stands now) and
        /// <see cref="GroupMember"/> (none) — to <paramref name="unit"/> when it has none, and returns true; false when the type is not
        /// registered on this node or the unit is gone.</summary>
        public static bool Ensure(EntityRepository repo, Entity unit)
        {
            if (!repo.IsAlive(unit) || !repo.IsComponentTypeRegistered<CivilianState>()) return false;
            if (repo.HasComponent<CivilianState>(unit)) { AddSharedParts(repo, unit); return true; }   // a mind whose shared parts are missing is completed, never left to a silent default
            AddMind(repo, unit, default);
            return true;
        }

        /// <summary>⭐ <c>CE-4628</c> — the whole mind in one place: the state and the shared creature components, EACH WITH ITS FINAL VALUE (inside the
        /// parallel brain phase an add is deferred, so "add, then write" would throw). His home is where he stands now.</summary>
        private static void AddMind(EntityRepository repo, Entity unit, in CivilianState state)
        {
            repo.AddComponent(unit, state);
            AddSharedParts(repo, unit);
        }

        private static void AddSharedParts(EntityRepository repo, Entity unit)
        {
            if (repo.IsComponentTypeRegistered<Fear>() && !repo.HasComponent<Fear>(unit)) repo.AddComponent(unit, new Fear());
            if (repo.IsComponentTypeRegistered<Home>() && !repo.HasComponent<Home>(unit))
                repo.AddComponent(unit, new Home { Centre = repo.HasComponent<SimTransform>(unit) ? (WorldPoint)repo.GetComponentRO<SimTransform>(unit).Position : default });
            if (repo.IsComponentTypeRegistered<GroupMember>() && !repo.HasComponent<GroupMember>(unit)) repo.AddComponent(unit, new GroupMember { Group = Entity.Null });
            // ⭐ CE-4628 §10c — his Demeanour from birth (calm, no gesture): its two writers — the behaviour's gesture (a DEFERRED add inside the parallel
            //   brain phase) and the crowd pass's mood (serial) — then only ever write their own fields, and neither add can land over the other's value
            if (repo.IsComponentTypeRegistered<Demeanour>() && !repo.HasComponent<Demeanour>(unit)) repo.AddComponent(unit, new Demeanour());
        }

        /// <summary>Records the mode the HSM is in (adds the mind when missing).</summary>
        public static void SetMode(EntityRepository repo, Entity unit, CivilianMode mode)
        {
            if (!repo.IsAlive(unit) || !repo.IsComponentTypeRegistered<CivilianState>()) return;
            if (repo.HasComponent<CivilianState>(unit)) { repo.GetComponentRW<CivilianState>(unit).Mode = mode; return; }
            // ⭐ CE-4623 (found by the backend's regression re-run, cross-lane) — a civilian with no state yet gets it WITH its mode: inside the
            //   parallel brain phase an add is DEFERRED (it lands next tick), so "add, then write" threw "missing CivilianState" and took the node
            //   down on the first tick of any scenario whose civilians were calm (📐 ua-civ-contagion exit 134 at c24f127cb too). The parallel
            //   rule: add a component WITH its final value (CLAUDE.md, "BEHAVIOUR CODE RUNS IN PARALLEL", D-31)
            AddMind(repo, unit, new CivilianState { Mode = mode });
        }

        /// <summary>His fear on this node; 0 when he has no <see cref="Fear"/>.</summary>
        public static float FearOf(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Fear>()) return 0f;
            return view.IsAlive(unit) && view.HasComponent<Fear>(unit) ? view.GetComponentRO<Fear>(unit).Value : 0f;
        }

        /// <summary>His mode on this node; <see cref="CivilianMode.None"/> when he has no <see cref="CivilianState"/>.</summary>
        public static CivilianMode ModeOf(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<CivilianState>()) return CivilianMode.None;
            return view.IsAlive(unit) && view.HasComponent<CivilianState>(unit) ? view.GetComponentRO<CivilianState>(unit).Mode : CivilianMode.None;
        }
    }
}
