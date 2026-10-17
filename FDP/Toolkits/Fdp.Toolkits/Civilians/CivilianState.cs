using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

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
    }

    /// <summary>
    /// ⭐ <c>CE-2235</c> (CV-3, CV-4; §5d) — a civilian's mind: his <see cref="Fear"/> (raised by what he hears, by
    /// <see cref="CivilianFearSystem"/>), his <see cref="Anger"/> and hidden <see cref="Attitude"/> (the crowd and the hostile-act slices),
    /// the <see cref="Mode"/> his HSM is in, and the home his routine keeps to. ⛔ BRAIN-LOCAL: it lives on the node that runs his
    /// <c>CivilianHsm</c>, is never replicated (others see what he DOES — his side, his gesture, his cowering) and never saved.
    /// Added by the HSM on its first tick (<see cref="CivilianMind.Ensure"/>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CivilianState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CivilianState
    {
        /// <summary>0 (calm) … 1 (terrified).</summary>
        public float Fear;
        /// <summary>0 … 1 — raised by the crowd and by force used on him (CE-2237, CE-2241). Unused in slice 1.</summary>
        public float Anger;
        /// <summary>−1 (friendly) … +1 (hostile at heart) — hidden; his side flips only on a hostile ACT (CV-4). Unused in slice 1.</summary>
        public float Attitude;
        /// <summary>The state his HSM is in.</summary>
        public CivilianMode Mode;
        /// <summary>1 once <see cref="Home"/> is set.</summary>
        public byte HasHome;
        /// <summary>Where his routine keeps him — where he stood when his HSM first ran.</summary>
        public Vector3 Home;
        /// <summary>Sim time (s) of the last thing that frightened him; 0 when nothing has.</summary>
        public double LastFrightAt;
        /// <summary>⭐ <c>CE-2236</c> — where the last frightening SOUND seemed to come from (the hearing estimate, off by its radius);
        /// valid when <see cref="HasFrightFrom"/> is 1. A panicking man runs away from it.</summary>
        public Vector3 FrightFrom;
        public byte HasFrightFrom;
        /// <summary>⭐ <c>CE-2237</c> (§5f) — the crowd he stands in (<see cref="Crowd.Id"/>), 0 when none. Written by <see cref="CrowdSystem"/>.</summary>
        public int CrowdId;
        /// <summary>⭐ <c>CE-2237</c> — the incident that draws him (the nearest person hurt within <see cref="CrowdRules.AttractMetres"/>
        /// while he is calm); valid when <see cref="HasFocus"/> is 1. Written by <see cref="CrowdSystem"/>, read by his Gather state.</summary>
        public Vector3 Focus;
        public byte HasFocus;
        /// <summary>⭐ <c>CE-2237</c> — sim time (s) he last HEARD violence (a shot, a blast); 0 when he never has. Tells a fright he heard
        /// from one he caught from his crowd.</summary>
        public double HeardAt;
        /// <summary>⭐ <c>CE-2238</c> (§5i) — 1 while he is an INSTIGATOR: his anger is 1 and he raises the anger of his crowd near him
        /// (<see cref="CrowdSystem"/>). Written by his HSM from its params.</summary>
        public byte Instigator;
        /// <summary>⭐ <c>CE-2238</c> (§5i, CV-4) — sim time (s) of his first HOSTILE ACT (a blow); 0 when he committed none. Brain-local;
        /// the act and its kind replicate as <see cref="HostileAct"/> (<c>CE-3238</c>, B-16). Written by <see cref="HostileActs.Commit"/>.</summary>
        public double HostileActAt;
        /// <summary>⭐ <c>CE-2239</c> part 2 (FL-6, §5j) — the latest end of a stun or an irritation (<c>EffectState</c>) that has frightened
        /// him already, so a running effect frightens him once and only a new or longer one again. Written by <see cref="CivilianFearSystem"/>.</summary>
        public double EffectFrightUntil;
    }

    /// <summary>⭐ <c>CE-2235</c> — the one writer of <see cref="CivilianState"/>'s presence and mode.</summary>
    public static class CivilianMind
    {
        /// <summary>Adds <see cref="CivilianState"/> to <paramref name="unit"/> when it has none (its home = where it stands now) and
        /// returns true; false when the type is not registered on this node or the unit is gone.</summary>
        public static bool Ensure(EntityRepository repo, Entity unit)
        {
            if (!repo.IsAlive(unit) || !repo.IsComponentTypeRegistered<CivilianState>()) return false;
            if (repo.HasComponent<CivilianState>(unit)) return true;
            var state = new CivilianState();
            if (repo.HasComponent<SimTransform>(unit))
            {
                state.Home = repo.GetComponentRO<SimTransform>(unit).Position;
                state.HasHome = 1;
            }
            repo.AddComponent(unit, state);
            return true;
        }

        /// <summary>Records the mode the HSM is in (adds the state when missing).</summary>
        public static void SetMode(EntityRepository repo, Entity unit, CivilianMode mode)
        {
            if (!Ensure(repo, unit)) return;
            repo.GetComponentRW<CivilianState>(unit).Mode = mode;
        }

        /// <summary>His fear on this node; 0 when he has no <see cref="CivilianState"/>.</summary>
        public static float FearOf(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<CivilianState>()) return 0f;
            return view.IsAlive(unit) && view.HasComponent<CivilianState>(unit) ? view.GetComponentRO<CivilianState>(unit).Fear : 0f;
        }

        /// <summary>His mode on this node; <see cref="CivilianMode.None"/> when he has no <see cref="CivilianState"/>.</summary>
        public static CivilianMode ModeOf(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<CivilianState>()) return CivilianMode.None;
            return view.IsAlive(unit) && view.HasComponent<CivilianState>(unit) ? view.GetComponentRO<CivilianState>(unit).Mode : CivilianMode.None;
        }
    }
}
