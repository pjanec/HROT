using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⛔ <c>CE-4659</c>: TRIMMED again — to a STATION weapon's barrel only (a unit's own mounts are computed from <c>WeaponAmmo</c>'s last burst).
    /// ⛔ <c>CE-4658</c>: TRIMMED to the barrel (the body / engine heat is <see cref="HeatSourceSystem"/>'s decision + <see cref="ThermalDriversOps"/>); RETIRES with CE-4659.
    /// WAS: ⭐ <c>CE-3061</c> — heat builds while an entity runs or fires and cools when it stops (🔒 user, 2026-10-05: "Hot when
    /// running or firing - pls implement some simple heat accumulation and cooldown"). docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.2.
    /// <code>
    /// heat += RunningHeatPerSecond × min(1, speed / ReferenceSpeed) × dt + FiringHeatPerShot × shots
    /// heat -= heat × CooldownPerSecond × dt;   clamp 0..1
    /// </code>
    /// Runs on the main loop where the entity moves and fires (the thermal sensor solves on a background snapshot of it).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ThermalHeatSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ThermalState>()) return;

            // ⭐ CE-4659 — a unit's OWN barrels (mount 0, its mount children) are computed by every reader from WeaponAmmo's last burst. What is left
            //   here is a STATION weapon carried in a vehicle (a door gun on its helicopter: CE-1358), which has no WeaponAmmo of its own.
            bool weapons = repo.IsComponentTypeRegistered<WeaponState>() && repo.IsComponentTypeRegistered<IsEmbarkedTag>();
            foreach (var e in repo.Query().With<ThermalState>().Build())
                if (repo.GetComponentRO<ThermalState>(e).BarrelHeat != 0f) repo.GetComponentRW<ThermalState>(e).BarrelHeat = 0f;
            if (!weapons) return;
            foreach (var w in repo.Query().With<WeaponState>().With<IsEmbarkedTag>().Build())
            {
                float barrel = repo.GetComponentRO<WeaponState>(w).Heat;
                if (barrel <= 0f) continue;
                var owner = repo.GetComponentRO<IsEmbarkedTag>(w).VehicleEntity;
                if (owner.IsNull || owner == w || !repo.IsAlive(owner) || !repo.HasComponent<ThermalState>(owner)) continue;
                ref var t = ref repo.GetComponentRW<ThermalState>(owner);
                if (barrel > t.BarrelHeat) t.BarrelHeat = barrel;
            }
        }
    }
}
