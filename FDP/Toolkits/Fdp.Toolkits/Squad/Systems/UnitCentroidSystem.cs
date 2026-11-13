using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-2125</c> / <c>CE-514</c> (R-272, R-175) — a unit's body-less commander stands at the CENTROID of its LIVE members (the
    /// leader soldier included). Runs on the unit's OWNER (the creator — CGF on a scenario load): only an OWNED
    /// <see cref="SimTransform"/> is written, and the owner's egress carries it to every other node. No live member ⇒ it stays where
    /// it is. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.1 C-3.
    /// </summary>
    /// <remarks>
    /// ⭐ CE-3293 S5 (R-3036 §1b, approved "split") — <see cref="TickPolicy.EveryFrame"/>: its whole work is the paused-time duty
    /// (a commander follows his members when one is DRAGGED while paused), and it reads no event and keeps no state, so a paused
    /// frame computes exactly what the next step would — the split has no step half.
    /// ⭐⭐ F-3 (CE-4503) — and that claim is now TRUE, by two rules. ① ONE PASS IS EXACT: a unit's owned sub-units are placed first
    /// (bottom-up, once per pass), so a platoon never averages last frame's squad centres. ② INPUT PHASE: it runs before anything of
    /// the frame reads a unit's position. 📐 Measured before: a step read the centres a paused frame had refreshed (another pass up the
    /// echelons, and the members' moves of the step before) or had not — the number of paused frames between two steps is the wall
    /// clock's, so the stepped 1 000-man world depended on the thread count (the force and platoon HQs parted first).
    /// ⛔ SUPERSEDED: Simulation phase, mid-way through the CGF pack, units in query order ("run twice so the platoon reads fresh squad
    /// centres").
    /// </remarks>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class UnitCentroidSystem : IEcsModuleSystem
    {
        private EntityQuery? _units;
        private int[] _placed = Array.Empty<int>();   // entity index → the pass that placed it (grown, never shrunk)
        private int _pass;

        /// <summary>Echelons below a unit placed first, at most this deep (a malformed cycle stops here).</summary>
        private const int MaxDepth = 16;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<UnitEntity>() || !repo.IsComponentTypeRegistered<UnitRoster>()) return;
            _units ??= repo.Query().With<UnitEntity>().With<UnitRoster>().WithOwned<SimTransform>().Build();
            if (++_pass == int.MaxValue) { Array.Clear(_placed); _pass = 1; }
            foreach (var unit in _units) Place(repo, unit, 0);
        }

        // The unit at its live members' centroid — its owned sub-units placed first (the query's rule: a UnitEntity with a roster and an
        // OWNED SimTransform), so this pass reads their positions of THIS pass.
        private void Place(EntityRepository repo, Entity unit, int depth)
        {
            if (unit.Index >= _placed.Length) Array.Resize(ref _placed, Math.Max(unit.Index + 1, _placed.Length * 2));
            if (_placed[unit.Index] == _pass) return;
            _placed[unit.Index] = _pass;

            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(unit);
            if (depth < MaxDepth)
                for (int i = 0; i < roster.Count; i++)
                {
                    var m = roster.SubordinateEntities[i];
                    if (repo.IsAlive(m) && repo.HasComponent<UnitEntity>(m) && repo.HasComponent<UnitRoster>(m)
                        && repo.HasComponent<SimTransform>(m) && repo.HasAuthority<SimTransform>(m))
                        Place(repo, m, depth + 1);
                }

            var sum = Vector3.Zero;
            int live = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                bool counts = Fdp.Toolkit.Squad.CombatEffective.IsUnit(repo, m)
                    ? Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(repo, m)
                    : Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, m);
                if (!counts || !repo.HasComponent<SimTransform>(m)) continue;
                sum += repo.GetComponentRO<SimTransform>(m).Position;
                live++;
            }
            if (live == 0) return;
            var centre = sum / live;
            if (repo.GetComponentRO<SimTransform>(unit).Position != centre)
                repo.GetComponentRW<SimTransform>(unit).Position = centre;
        }
    }
}
