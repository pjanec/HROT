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
    /// ⭐ <c>CE-3061</c> — heat builds while an entity runs or fires and cools when it stops (🔒 user, 2026-10-05: "Hot when
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

            // Shots fired this frame (FireProcessingSystem's notification, one per round).
            if (repo.Bus.IsRegistered<WeaponFireNotification>())
                foreach (ref readonly var shot in view.ReadEvents<WeaponFireNotification>())
                    if (shot.Fired && repo.IsAlive(shot.Shooter) && repo.HasComponent<ThermalState>(shot.Shooter)   // ⭐ Q94 A-2 (R-3018)
                        && !Fdp.Toolkit.Combat.Munitions.IsExpendable(repo, shot.AmmoGuid)   // ⭐ CE-2188 (§5.4b F5) — a dispenser heats no barrel
                        && !Fdp.Toolkit.Combat.PersonHands.Threw(repo, in shot))              // ⭐ CE-3248 (TC-6) — nor does a hand
                    {
                        ref var t = ref repo.GetComponentRW<ThermalState>(shot.Shooter);
                        t.Heat = MathF.Min(1f, t.Heat + t.FiringHeatPerShot);
                    }

            foreach (var e in repo.Query().With<ThermalState>().With<SimTransform>().Build())
            {
                ref var t = ref repo.GetComponentRW<ThermalState>(e);
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                float speed = 0f;
                if (t.HasLast && deltaTime > 0f)
                {
                    float dx = p.X - t.LastX, dy = p.Y - t.LastY, dz = p.Z - t.LastZ;
                    speed = MathF.Sqrt(dx * dx + dy * dy + dz * dz) / deltaTime;
                }
                t.LastX = p.X; t.LastY = p.Y; t.LastZ = p.Z; t.HasLast = true;

                float reference = t.ReferenceSpeed > 0f ? t.ReferenceSpeed : 5f;
                float heat = t.Heat + t.RunningHeatPerSecond * MathF.Min(1f, speed / reference) * deltaTime;
                heat -= heat * t.CooldownPerSecond * deltaTime;
                t.Heat = Math.Clamp(heat, 0f, 1f);
                t.BarrelHeat = repo.IsComponentTypeRegistered<WeaponState>() && repo.HasComponent<WeaponState>(e)
                    ? repo.GetComponentRO<WeaponState>(e).Heat : 0f;   // ⭐ CE-1358 — its own mount 0
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
