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
        /// <summary>The unit a weapon's barrel warms: a mount child's parent, an embarked weapon's vehicle, else itself.</summary>
        private static Entity OwnerOf(EntityRepository repo, Entity weapon)
        {
            if (repo.IsComponentTypeRegistered<WeaponMountInfo>() && repo.HasComponent<WeaponMountInfo>(weapon)
                && repo.IsComponentTypeRegistered<PartMetadata>() && repo.HasComponent<PartMetadata>(weapon))
                return repo.GetComponentRO<PartMetadata>(weapon).ParentEntity;
            if (repo.IsComponentTypeRegistered<IsEmbarkedTag>() && repo.HasComponent<IsEmbarkedTag>(weapon))
                return repo.GetComponentRO<IsEmbarkedTag>(weapon).VehicleEntity;
            return weapon;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ThermalState>()) return;

            // ⭐ CE-4658 — the body / engine heat is no longer integrated here: HeatSourceSystem writes the replicated DECISION and every reader computes the heat
            //   (ThermalDriversOps). What is left is the barrel (CE-1358), until CE-4659 computes it from each mount's last burst.
            bool weapons = repo.IsComponentTypeRegistered<WeaponState>();
            foreach (var e in repo.Query().With<ThermalState>().Build())
            {
                float own = weapons && repo.HasComponent<WeaponState>(e) ? repo.GetComponentRO<WeaponState>(e).Heat : 0f;   // ⭐ CE-1358 — its own mount 0
                if (repo.GetComponentRO<ThermalState>(e).BarrelHeat != own) repo.GetComponentRW<ThermalState>(e).BarrelHeat = own;   // ⭐ a write only on a change
            }

            // ⭐ CE-1358 (DESIGN_Weapon_Layers §5k) — a mount child's barrel, and a station weapon's (a door gun on its helicopter), warm their unit
            if (!repo.IsComponentTypeRegistered<WeaponState>()) return;
            foreach (var w in repo.Query().With<WeaponState>().Build())
            {
                float barrel = repo.GetComponentRO<WeaponState>(w).Heat;
                if (barrel <= 0f) continue;
                var owner = OwnerOf(repo, w);
                if (owner.IsNull || owner == w || !repo.IsAlive(owner) || !repo.HasComponent<ThermalState>(owner)) continue;
                ref var t = ref repo.GetComponentRW<ThermalState>(owner);
                if (barrel > t.BarrelHeat) t.BarrelHeat = barrel;
            }
        }
    }
}
