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
    /// the rest are the later slices' states (CE-2236 … CE-2238).
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
