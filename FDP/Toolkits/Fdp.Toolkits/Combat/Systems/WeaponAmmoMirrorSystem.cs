using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐ <c>CE-3174</c> (C-7; <c>docs/DESIGN_Entity_Control.md</c> §8d) — the ONE writer of <see cref="WeaponAmmo"/>: on the node running a
    /// unit's brain (where its <see cref="WeaponState"/>s are spent), it mirrors the ammunition of every mount — mount 0 on the unit, the
    /// others on its mount children — and writes only on a change, so the replicated <c>dtWeaponAmmo</c> crosses only when a round
    /// is fired or a magazine changes.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class WeaponAmmoMirrorSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<WeaponState>()
                || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            if (!repo.IsComponentTypeRegistered<WeaponAmmo>()) repo.RegisterComponent<WeaponAmmo>();
            bool mounts = repo.IsComponentTypeRegistered<WeaponMountInfo>();
            Span<Entity> children = stackalloc Entity[8];

            foreach (var unit in repo.Query().With<WeaponState>().With<BehaviorState>().Build())
            {
                if (!repo.HasAuthority<BehaviorState>(unit)) continue;
                var ammo = new WeaponAmmo { Count = 1 };
                ammo.Set(0, repo.GetComponentRO<WeaponState>(unit));
                if (mounts)
                {
                    int n = WeaponMountQuery.EnumerateMounts(repo, unit, children);
                    for (int i = 0; i < n; i++)
                    {
                        if (!repo.HasComponent<WeaponMountInfo>(children[i]) || !repo.HasComponent<WeaponState>(children[i])) continue;
                        int index = repo.GetComponentRO<WeaponMountInfo>(children[i]).MountIndex;
                        if (index <= 0 || index >= WeaponAmmo.MaxMounts) continue;
                        ammo.Set(index, repo.GetComponentRO<WeaponState>(children[i]));
                        ammo.Count = (byte)Math.Max(ammo.Count, index + 1);
                    }
                }
                if (repo.HasComponent<WeaponAmmo>(unit))
                {
                    if (!repo.GetComponentRO<WeaponAmmo>(unit).Equals(ammo)) repo.SetComponent(unit, ammo);
                }
                else repo.AddComponent(unit, ammo);
            }
        }
    }
}
