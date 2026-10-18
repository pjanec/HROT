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
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class UnitCentroidSystem : IEcsModuleSystem
    {
        private EntityQuery? _units;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<UnitEntity>() || !repo.IsComponentTypeRegistered<UnitRoster>()) return;
            _units ??= repo.Query().With<UnitEntity>().With<UnitRoster>().WithOwned<SimTransform>().Build();
            foreach (var unit in _units)
            {
                ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(unit);
                var sum = Vector3.Zero;
                int live = 0;
                for (int i = 0; i < roster.Count; i++)
                {
                    var m = roster.SubordinateEntities[i];
                    // ⭐ CE-2262 SL-0 (CB-16): a unit member (a squad under a platoon) counts while it is combat effective — ⛔ before,
                    //   a wiped-out squad (no Health ⇒ always "alive") kept pulling the platoon's centre to where it died
                    bool counts = Fdp.Toolkit.Squad.CombatEffective.IsUnit(repo, m)
                        ? Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(repo, m)
                        : Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, m);
                    if (!counts || !repo.HasComponent<SimTransform>(m)) continue;
                    sum += repo.GetComponentRO<SimTransform>(m).Position;
                    live++;
                }
                if (live == 0) continue;
                ref var tf = ref repo.GetComponentRW<SimTransform>(unit);
                tf.Position = sum / live;
            }
        }
    }
}
