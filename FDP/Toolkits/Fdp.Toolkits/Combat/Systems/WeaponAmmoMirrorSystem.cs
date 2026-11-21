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
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class WeaponAmmoMirrorSystem : IEcsModuleSystem
    {
        // ⭐ CE-3240 (found by the CE-3222 perf gate) — each unit's mount children, gathered in ONE pass per frame. Was
        //   WeaponMountQuery.EnumerateMounts per unit, which scans every mount child in the world: O(units × mounts) —
        //   📐 3.4 ms at 100 civilians, 22.0 ms at 300 (CGF, --mode all).
        /// <summary>⭐ CE-4659 — shots less than this apart (s) are one burst.</summary>
        public const double BurstGapSeconds = 1.0;

        [Fdp.Core.FrameScratch("scratch — cleared at the top of each run and filled from this frame's world")]
        private readonly Dictionary<Entity, int> _first = new();
        [Fdp.Core.FrameScratch("scratch — cleared at the top of each run and filled from this frame's world")]
        private readonly List<(Entity Child, int Next)> _links = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<WeaponState>()
                || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            if (!repo.IsComponentTypeRegistered<WeaponAmmo>()) repo.RegisterComponent<WeaponAmmo>();
            bool mounts = repo.IsComponentTypeRegistered<WeaponMountInfo>();
            bool loads = repo.IsComponentTypeRegistered<AmmoLoadout>();   // ⭐ CE-1079 — the breech, for the HUD
            double now = repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0;   // ⭐ CE-4659
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
                bool had = repo.HasComponent<WeaponAmmo>(unit);
                var prev = had ? repo.GetComponentRO<WeaponAmmo>(unit) : WeaponAmmo.Empty();
                var ammo = WeaponAmmo.Empty();
                ammo.Count = 1;
                ref readonly var w0 = ref repo.GetComponentRO<WeaponState>(unit);
                ammo.Set(0, w0, LoadOf(repo, unit, loads));
                Burst(ref ammo, in prev, had, 0, in w0, now);
                if (mounts && _first.TryGetValue(unit, out int link))
                {
                    for (; link >= 0; link = _links[link].Next)
                    {
                        var child = _links[link].Child;
                        if (!repo.IsAlive(child) || !repo.HasComponent<WeaponState>(child)) continue;
                        int index = repo.GetComponentRO<WeaponMountInfo>(child).MountIndex;
                        if (index <= 0 || index >= WeaponAmmo.MaxMounts) continue;
                        ref readonly var wi = ref repo.GetComponentRO<WeaponState>(child);
                        ammo.Set(index, wi, LoadOf(repo, child, loads));
                        Burst(ref ammo, in prev, had, index, in wi, now);
                        ammo.Count = (byte)Math.Max(ammo.Count, index + 1);
                    }
                }
                // ⭐ CE-3232 (B-10, §7.11 LD-2) — the laid-down mounts cross with the counts
                if (repo.IsComponentTypeRegistered<LaidDownWeapons>() && repo.HasComponent<LaidDownWeapons>(unit))
                    ammo.LaidDown = (byte)(repo.GetComponentRO<LaidDownWeapons>(unit).Mounts & ((1 << WeaponAmmo.MaxMounts) - 1));
                if (had)
                {
                    if (!repo.GetComponentRO<WeaponAmmo>(unit).Equals(ammo)) repo.SetComponent(unit, ammo);
                }
                else repo.AddComponent(unit, ammo);
            }
        }

        /// <summary>
        /// ⭐ <c>CE-4659</c> (TD-5, §3d) — mount <paramref name="m"/>'s last burst: carried over, and on a SHOT (its round count fell since the last record)
        /// the last shot is now, the heat just after it, and the shot joins the burst — or opens a new one after <see cref="BurstGapSeconds"/> of silence.
        /// ⚠ A first record (no previous) only takes the barrel's state, never invents a shot. The round count already makes a shot a sample, so the
        /// burst adds none; nothing changes while the barrel cools.
        /// </summary>
        private static void Burst(ref WeaponAmmo ammo, in WeaponAmmo prev, bool had, int m, in WeaponState w, double now)
        {
            int rounds = prev.BurstRoundsOf(m);
            double start = prev.BurstStartedAtOf(m), last = prev.LastShotAtOf(m);
            float heat = m switch { 0 => prev.HeatAtLastShot0, 1 => prev.HeatAtLastShot1, 2 => prev.HeatAtLastShot2, _ => prev.HeatAtLastShot3 };
            int fired = had && m < prev.Count ? prev.AmmoOf(m) - w.Ammo : 0;
            if (fired > 0)
            {
                if (now - last > BurstGapSeconds || double.IsNegativeInfinity(last)) { rounds = 0; start = now; }
                rounds += fired;
                last = now;
                heat = w.Heat;
            }
            ammo.SetBurst(m, rounds, start, last, heat, w.HeatCoolPerSecond);
        }

        private static AmmoLoadout? LoadOf(EntityRepository repo, Entity mount, bool registered)
            => registered && repo.HasComponent<AmmoLoadout>(mount) ? repo.GetComponentRO<AmmoLoadout>(mount) : null;
    }
}
