using CarKinem.Core;
using Fdp.Core;

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐ <c>CE-4598</c> (B-1; 📄 docs/DESIGN_Vehicle_Breaching.md §7 V-4, V-14, B-D1) — WHO may break a barrier it meets: only a hull a PLAYER
    /// is driving at this tick. Three facts, all on the hull itself (no cross-entity read in the per-entity phase):
    /// <list type="number">
    /// <item>it moves on the <see cref="KinematicsMode.Manual"/> branch — the operator's axes, never an autonomous model;</item>
    /// <item>a LIVE possession holds it (<c>PossessedBy</c>, removed on every release) in a driving mode — ⛔ NOT "has <c>ManualDrive</c>":
    ///       a release leaves that component on the hull (<c>Possession.LetGoOfManual</c>), so a once-possessed AI tank would breach at
    ///       every corner hold (V-4);</item>
    /// <item>no replay is running — the recording owns the world, a replayed possession re-decides nothing (V-14).</item>
    /// </list>
    /// The bulldozer's order (§7.6) is the one AI exception and comes through its own marker (B-4), not through this gate.
    /// </summary>
    public static class HullBreakGate
    {
        /// <summary>May the hull <paramref name="entity"/>, moving with <paramref name="nav"/>, break what it meets this tick?</summary>
        public static bool MayBreak(EntityRepository repo, Entity entity, in NavState nav)
        {
            if (nav.Mode != KinematicsMode.Manual) return false;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) return false;
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>()
                || !repo.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(entity)) return false;
            var held = repo.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(entity);
            // a RIDER sits in a vehicle someone else drives: he holds his own body, never the hull's controls
            return held.Mode == Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps;
        }
    }
}
