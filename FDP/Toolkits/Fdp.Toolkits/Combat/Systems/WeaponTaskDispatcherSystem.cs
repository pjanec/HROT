using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐ WL-2 (docs/DESIGN_Weapon_Layers.md §5c, W-1, W-3, W-8, W-11 / R-267) — <b>the weapon muscle.</b> For every turret part with a
    /// <see cref="WeaponTask"/>: <c>Idle</c> holds the pose; <c>Track</c> (and, until WL-3 fires, <c>Engage</c>) solves the aim with
    /// <see cref="BallisticAim"/> and slews the <see cref="TurretPose"/> toward it at the turret's TKB rates and inside its limits
    /// (<see cref="TurretSlew"/>), reporting <see cref="WeaponTaskStatus"/> — Slewing, OnTarget, or Failed (OutOfArc / NoTarget /
    /// NoTurret) — under the task's epoch.
    /// <para>⛔ No authority gate, by design (the <see cref="FireProcessingSystem"/> CE-198 rule): only the node composing the combat
    /// capability (<c>CombatModule</c> — the Muscle, or the editor as its one node) schedules this system.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class WeaponTaskDispatcherSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException($"{nameof(WeaponTaskDispatcherSystem)} requires direct EntityRepository access.");
            if (!repo.IsComponentTypeRegistered<WeaponTask>() || !repo.IsComponentTypeRegistered<TurretPose>()) return;

            foreach (var part in repo.Query().With<WeaponTask>().With<TurretPose>().With<PartMetadata>().Build())
            {
                var task = repo.GetComponent<WeaponTask>(part);
                var report = Run(repo, part, task, deltaTime);
                Store(repo, part, report);
            }
        }

        private static WeaponTaskStatus Run(EntityRepository repo, Entity part, in WeaponTask task, float dt)
        {
            var status = new WeaponTaskStatus { Epoch = task.Epoch, RoundsFired = PreviousRounds(repo, part, task.Epoch) };
            // ⭐ WL-3 — the fire clock runs in every mode, so a Track → Engage switch finds it already drained.
            if (repo.IsComponentTypeRegistered<WeaponMuscleState>() && repo.HasComponent<WeaponMuscleState>(part))
            {
                ref var clock = ref repo.GetComponentRW<WeaponMuscleState>(part);
                if (clock.CooldownRemaining > 0f) clock.CooldownRemaining = System.MathF.Max(0f, clock.CooldownRemaining - dt);
            }
            if (task.Mode == WeaponTaskMode.Idle) return status;   // State None: the turret holds its pose

            var meta = repo.GetComponent<PartMetadata>(part);
            var hull = meta.ParentEntity;
            int index = TurretParts.IndexOf(meta.InstanceId);
            var turret = index >= 0 && repo.IsAlive(hull) && repo.HasComponent<SimTransform>(hull)
                ? CombatTkb.TurretOf(repo, hull, index) : null;
            if (turret == null) return Failed(status, WeaponTaskFailure.NoTurret);

            System.Numerics.Vector3 aim;
            if (task.Target.IsNull)
                aim = task.Point;
            else if (repo.IsAlive(task.Target) && repo.HasComponent<SimTransform>(task.Target))
                aim = AimPoint.Middle(repo, task.Target, out _, out _);
            else
                return Failed(status, WeaponTaskFailure.NoTarget);

            var (azimuth, elevation) = BallisticAim.Solve(repo, hull, turret, CombatTkb.MountOf(repo, hull, task.MountIndex), aim,
                                                          task.Arc, unchecked((long)task.AmmoGuid));   // ⭐ ui handoff P2 — the laid gun
            if (!TurretSlew.InArc(turret, azimuth, elevation)) return Failed(status, WeaponTaskFailure.OutOfArc);

            ref var pose = ref repo.GetComponentRW<TurretPose>(part);
            pose = TurretSlew.Step(pose, azimuth, elevation, turret, dt);
            status.State = TurretSlew.OnTarget(pose, azimuth, elevation) ? WeaponTaskState.OnTarget : WeaponTaskState.Slewing;
            if (task.Mode == WeaponTaskMode.Engage) Engage(repo, part, hull, task, ref status);
            return status;
        }

        /// <summary>
        /// ⭐ WL-3 (§5e, W-4, W-7) — on target, with a granted round left and the fire clock run down, the round leaves through the
        /// ordinary path (a <see cref="Fdp.Toolkit.Combat.Events.WeaponFireIntent"/> that <see cref="FireProcessingSystem"/> turns
        /// into a bullet), unless a friendly is on the line at that moment (<see cref="LineOfFire.BlockedByFriendly"/> — the ONE
        /// check, which the per-shot path calls too). All granted rounds fired ⇒ Done (the brain may grant more under the same epoch).
        /// </summary>
        private static void Engage(EntityRepository repo, Entity part, Entity hull, in WeaponTask task, ref WeaponTaskStatus status)
        {
            if (status.RoundsFired >= task.RoundsGranted)
            {
                if (task.RoundsGranted > 0) status.State = WeaponTaskState.Done;
                return;
            }
            if (status.State != WeaponTaskState.OnTarget || !repo.IsComponentTypeRegistered<WeaponMuscleState>()) return;
            if (!repo.HasComponent<WeaponMuscleState>(part)) repo.AddComponent(part, default(WeaponMuscleState));
            ref var clock = ref repo.GetComponentRW<WeaponMuscleState>(part);
            if (clock.CooldownRemaining > 0f) return;
            if (!task.Target.IsNull && LineOfFire.BlockedByFriendly(repo, hull, task.Target)) return;

            repo.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireIntent
            {
                Shooter     = hull,
                Target      = task.Target,
                WeaponIndex = task.MountIndex,
                // ⭐ Already AT the muscle: never forwarded as a WeaponFireRequest (the only reader of this flag on a fire intent is
                //   WeaponFireIntentEgressTranslator), so a Brain+Muscle node cannot fire the round twice.
                IsRemote    = true,
                AtPoint     = task.Target.IsNull,
                TargetPoint = task.Point,
                AmmoGuid    = unchecked((long)task.AmmoGuid),   // ⭐ M8 — the round the weapon brain loaded
            });
            clock.CooldownRemaining = task.CooldownSeconds;
            status.RoundsFired++;
            status.State = WeaponTaskState.Firing;
        }

        private static WeaponTaskStatus Failed(WeaponTaskStatus status, WeaponTaskFailure reason)
        {
            status.State = WeaponTaskState.Failed;
            status.Reason = reason;
            return status;
        }

        /// <summary>Rounds already fired under this epoch (a new epoch starts from 0).</summary>
        private static ushort PreviousRounds(EntityRepository repo, Entity part, uint epoch)
            => repo.IsComponentTypeRegistered<WeaponTaskStatus>() && repo.HasComponent<WeaponTaskStatus>(part)
               && repo.GetComponent<WeaponTaskStatus>(part).Epoch == epoch
                ? repo.GetComponent<WeaponTaskStatus>(part).RoundsFired : (ushort)0;

        /// <summary>Writes the report when it changed; adds the component on first use.</summary>
        private static void Store(EntityRepository repo, Entity part, WeaponTaskStatus report)
        {
            if (!repo.IsComponentTypeRegistered<WeaponTaskStatus>()) return;
            if (!repo.HasComponent<WeaponTaskStatus>(part)) { repo.AddComponent(part, report); return; }
            ref var current = ref repo.GetComponentRW<WeaponTaskStatus>(part);
            if (!current.Equals(report)) current = report;
        }
    }
}
