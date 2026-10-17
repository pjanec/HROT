using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

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
        // ⭐ CE-3240 (found by the CE-3222 perf gate) — each unit's mount children, gathered in ONE pass per frame. Was
        //   WeaponMountQuery.EnumerateMounts per unit, which scans every mount child in the world: O(units × mounts) —
        //   📐 3.4 ms at 100 civilians, 22.0 ms at 300 (CGF, --mode all).
        private readonly Dictionary<Entity, int> _first = new();
        private readonly List<(Entity Child, int Next)> _links = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<WeaponState>()
                || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            if (!repo.IsComponentTypeRegistered<WeaponAmmo>()) repo.RegisterComponent<WeaponAmmo>();
            bool mounts = repo.IsComponentTypeRegistered<WeaponMountInfo>();
            bool loads = repo.IsComponentTypeRegistered<AmmoLoadout>();   // ⭐ CE-1079 — the breech, for the HUD
            _first.Clear();
            _links.Clear();
            if (mounts && repo.IsComponentTypeRegistered<PartMetadata>())
                foreach (var child in repo.Query().With<WeaponMountInfo>().With<PartMetadata>().Build())
                {
                    var parent = repo.GetComponentRO<PartMetadata>(child).ParentEntity;
                    _links.Add((child, _first.TryGetValue(parent, out int head) ? head : -1));
                    _first[parent] = _links.Count - 1;
                }

            foreach (var unit in repo.Query().With<WeaponState>().With<BehaviorState>().Build())
            {
                if (!repo.HasAuthority<BehaviorState>(unit)) continue;
                var ammo = new WeaponAmmo { Count = 1 };
                ammo.Set(0, repo.GetComponentRO<WeaponState>(unit), LoadOf(repo, unit, loads));
                if (mounts && _first.TryGetValue(unit, out int link))
                {
                    for (; link >= 0; link = _links[link].Next)
                    {
                        var child = _links[link].Child;
                        if (!repo.IsAlive(child) || !repo.HasComponent<WeaponState>(child)) continue;
                        int index = repo.GetComponentRO<WeaponMountInfo>(child).MountIndex;
                        if (index <= 0 || index >= WeaponAmmo.MaxMounts) continue;
                        ammo.Set(index, repo.GetComponentRO<WeaponState>(child), LoadOf(repo, child, loads));
                        ammo.Count = (byte)Math.Max(ammo.Count, index + 1);
                    }
                }
                // ⭐ CE-3232 (B-10, §7.11 LD-2) — the laid-down mounts cross with the counts
                if (repo.IsComponentTypeRegistered<LaidDownWeapons>() && repo.HasComponent<LaidDownWeapons>(unit))
                    ammo.LaidDown = (byte)(repo.GetComponentRO<LaidDownWeapons>(unit).Mounts & ((1 << WeaponAmmo.MaxMounts) - 1));
                if (repo.HasComponent<WeaponAmmo>(unit))
                {
                    if (!repo.GetComponentRO<WeaponAmmo>(unit).Equals(ammo)) repo.SetComponent(unit, ammo);
                }
                else repo.AddComponent(unit, ammo);
            }
        }

        private static AmmoLoadout? LoadOf(EntityRepository repo, Entity mount, bool registered)
            => registered && repo.HasComponent<AmmoLoadout>(mount) ? repo.GetComponentRO<AmmoLoadout>(mount) : null;
    }
}
