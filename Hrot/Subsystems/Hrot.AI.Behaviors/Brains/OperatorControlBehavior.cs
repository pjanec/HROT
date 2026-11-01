using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-3172</c> / <c>CE-3173</c> (M-1, M-7; 📄 docs/DESIGN_Entity_Control.md §8b) — <c>OperatorControl</c>, what a POSSESSED
    /// unit runs: <see cref="PossessionSystem"/> assigns it at origin <c>Possession</c> (which pauses the unit's task), and its one node
    /// hands the station's newest frame (<see cref="OperatorInput"/>) to the body as <see cref="ManualDrive"/>, with the motion request
    /// in <see cref="NavigationMode.Manual"/>. ⛔ Not an AI-off switch (R-270): it is a behaviour like any other, and clearing it resumes
    /// the paused task.
    /// </summary>
    public static class OperatorControlBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ManualControlParams
        {
            /// <summary>Unused — the node takes its axes from <see cref="OperatorInput"/>.</summary>
            public byte Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ManualControlState
        {
            /// <summary>The frame last handed to the body.</summary>
            public uint Seq;
            /// <summary>⭐ CE-1078 / CE-1079 (DESIGN_Entity_Control §8e) — the press counters already acted on.</summary>
            public byte ShotSeq, CancelSeq, AmmoSeq;
            /// <summary>⭐ CE-1078 — 1 while a LOCKED shot waits for its round to leave; ⭐ CE-1127 (§8g F-2) — 2 while a SIGHT shot does (its
            /// point is the crosshair of the tick it leaves, never held).</summary>
            public byte Pending;
            /// <summary>⭐ CE-1078 — the locked shot's unit (null = <see cref="PendingPoint"/>).</summary>
            public Entity PendingTarget;
            /// <summary>The locked shot's point: in the world, or ⭐ CE-1125 in <see cref="PendingTarget"/>'s own frame when it is on a unit.</summary>
            public System.Numerics.Vector3 PendingPoint;
            /// <summary>⭐ CE-1078 — the weapon action's round count at which the locked shot has left (counted when it was locked).</summary>
            public int Cap;
            /// <summary>⭐ CE-1154 (DESIGN_Entity_Control §8h U-2) — the use presses already acted on.</summary>
            public byte UseSeq;
            /// <summary>⭐ CE-1154 — the interaction action the last use press started (0 = none running).</summary>
            public ushort UseAction;
            /// <summary>⭐ CE-1154 — its <see cref="InteractionChannel.ActionInstanceId"/>, so the answer is read only from OUR action.</summary>
            public uint UseStarted;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OperatorControlBlackboard
        {
            public ManualControlParams Control;
            public ManualControlState ControlState;
        }

        /// <summary>
        /// Every tick: the motion request is <see cref="NavigationMode.Manual"/> (entered once — and again whenever anything else
        /// cleared it, e.g. the paused task's move leaving), and the newest frame becomes <see cref="ManualDrive"/>. No frame yet ⇒ the
        /// body holds still. Runs until the possession is released; ⚠ the let-go on release is <c>PossessionSystem</c>'s (a root
        /// behaviour's deactivators do not run when it is cleared — DESIGN_Behaviour_Fault_And_Teardown, rejected alternatives).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus ManualControl(ref ManualControlParams p, ref ManualControlState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<NavigationIntent>(self)) return NodeStatus.Running;   // nothing to drive
            ref var intent = ref world.GetComponentRW<NavigationIntent>(self);
            if (intent.Mode != NavigationMode.Manual)
            {
                intent.Mode = NavigationMode.Manual;
                intent.TargetSpeed = 0f;
                unchecked { intent.IntentId++; }   // the bridge applies a mode only on a new IntentId
            }

            if (!world.IsComponentTypeRegistered<ManualDrive>()) world.RegisterComponent<ManualDrive>();
            uint lastSeq = ws.Seq;
            var drive = default(ManualDrive);
            if (world.IsComponentTypeRegistered<OperatorInput>() && world.HasComponent<OperatorInput>(self))
            {
                var input = world.GetComponentRO<OperatorInput>(self);
                drive = new ManualDrive
                {
                    Forward = input.Forward, Lateral = input.Lateral, Steer = input.Steer, Yaw = input.Yaw, Vertical = input.Vertical, Gait = input.Gait,
                    Seq = input.Seq,
                };
                // ⭐ CE-1074 (DESIGN_Entity_Control.md §13.5) — the operator's stance, through the brain's one way to ask for it
                //   (StanceRequest: a version bump only on a change, so a frame per tick is harmless). 🔒 User, 2026-10-14: "I will
                //   need to be able to switch the possessed character to crouched as well as lying pose (cycling using a key press)".
                if (input.Stance is >= 1 and <= 3)
                    Hrot.MuscleCharacter.Animation.Stance.StanceRequest.Set(world, self, (Fdp.Toolkit.Tkb.Domain.StanceId)(input.Stance - 1), OperatorStanceBlendSeconds);
                // ⭐ CE-1212 (DESIGN_Entity_Control §8j L-1) — the lean the operator holds (Q / E), through its one writer; written only on a
                //   change, so a frame per tick does not re-send dtLean. A prone man does not lean (the sim's rule, Leans.SignOf).
                if (input.Lean <= (byte)LeanSide.LeanRight && Leans.Of(world, self) != (LeanSide)input.Lean)
                    Leans.Set(world, self, (LeanSide)input.Lean);
                // ⭐ CE-1219 (§8k B-1) — binoculars up while B is held, through the one writer (it refuses a man who carries none); down again
                //   when let go — only his OWN binoculars, never another held item
                bool up = HeldItems.BinocularsUp(world, self);
                if (input.Binoculars != 0 && !up) HeldItems.Set(world, self, Fdp.Toolkit.Combat.Components.HeldItemKind.Binoculars);
                else if (input.Binoculars == 0 && up) HeldItems.Set(world, self, Fdp.Toolkit.Combat.Components.HeldItemKind.None);
            }
            else if (world.HasComponent<ManualDrive>(self))
                drive.Yaw = world.GetComponentRO<ManualDrive>(self).Yaw;   // no frame: stop, keep facing
            // ⭐ CE-1157 — no frame (a RIDER: the possession takes his frames while the station drives his vehicle) keeps the last frame
            //   number, so a station that restarts its frames after the ride (Seq 1 again, counters from 0) still reads as a NEW station and
            //   resets the press counters; zeroing it swallowed the first press after the ride (its counter equal to the last one acted on)
            if (drive.Seq != 0) ws.Seq = drive.Seq;
            if (world.HasComponent<ManualDrive>(self)) world.SetComponent(self, drive);
            else world.AddComponent(self, drive);
            OperateWeapon(world, self, ref ws, lastSeq);
            OperateUse(world, self, ref ws, lastSeq);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1154</c> (📄 docs/DESIGN_Entity_Control.md §8h U-1, U-2, U-6, U-7, §8h.6) — the operator's USE key, the
        /// <see cref="OperateWeapon"/> shape for the interaction channel: a new <see cref="OperatorInput.UseSeq"/> asks the ONE rule
        /// (<c>OperatorUseActions.For</c> — the same the station's HUD prompt calls) what the crosshair's target offers, takes the
        /// verb <see cref="OperatorInput.UseVerb"/> names (0 = the primary) and, if it is available, starts its executor on the
        /// <see cref="InteractionChannel"/> with the params <see cref="OperatorUseActions.WriteParams"/> packs. The executor keeps the
        /// reach, the time and the networked command; ⛔ no walk-to (U-3). The verdict goes back to the station as STATE
        /// (<see cref="OperatorUseStatus"/>, replicated as <c>dtOperatorUseStatus</c>): Running when it starts, Done or Refused + Reason
        /// when it ends — the reason from the rule when it refuses, <see cref="UseReason.Failed"/> when the executor fails after the start.
        /// <para>A press while an earlier use action runs replaces it (a new action instance — the executor starts over). A stale frame
        /// (a new station's <c>Seq</c> going back) resets the counter, as for the trigger.</para>
        /// </summary>
        private static unsafe void OperateUse(EntityRepository world, Entity self, ref ManualControlState ws, uint lastSeq)
        {
            if (!world.HasComponent<InteractionChannel>(self) || !world.IsComponentTypeRegistered<OperatorInput>()
                || !world.HasComponent<OperatorInput>(self)) return;
            var input = world.GetComponentRO<OperatorInput>(self);
            if (input.Seq < lastSeq) ws.UseSeq = 0;   // a new station's counters start from 0

            // the action our last press started: forward its answer once, then hand the channel back (the DoorNodes rule)
            if (ws.UseStarted != 0)
            {
                if (EmbarkNodes.OwnsInteraction(world, self, ws.UseStarted, ws.UseAction))
                {
                    var status = world.GetComponentRO<InteractionChannel>(self).Status;
                    if (status != NodeStatus.Running)
                    {
                        EmbarkNodes.Release(world, self);
                        WriteUseStatus(world, self, ws.UseSeq, ws.UseAction,
                            status == NodeStatus.Success ? UseOutcome.Done : UseOutcome.Refused,
                            status == NodeStatus.Success ? UseReason.Ok : UseReason.Failed);
                        ws.UseStarted = 0;
                        ws.UseAction = 0;
                    }
                }
                else { ws.UseStarted = 0; ws.UseAction = 0; }   // someone else took the channel (the possession ended, a new run)
            }

            if (input.UseSeq == ws.UseSeq) return;
            ws.UseSeq = input.UseSeq;

            var actions = s_useActions ??= new UseAction[OperatorUseActions.MaxActions];   // a press is rare; no allocation per press
            int n = OperatorUseActions.For(world, self, UseTargetOf(world, self, input.UseTarget), actions);
            if (input.UseVerb >= n)
            {
                WriteUseStatus(world, self, ws.UseSeq, 0, UseOutcome.Refused, UseReason.NothingToDo);
                return;
            }
            var action = actions[input.UseVerb];
            if (!action.Available)
            {
                WriteUseStatus(world, self, ws.UseSeq, action.ActionId, UseOutcome.Refused, action.Reason);
                return;
            }
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            EmbarkNodes.Start(world, self, ref channel, action.ActionId);
            OperatorUseActions.WriteParams(action, MemoryMarshal.CreateSpan(ref channel.Params[0], BehaviorConstants.ActionParamsByteSize));
            ws.UseStarted = channel.ActionInstanceId;
            ws.UseAction = action.ActionId;
            WriteUseStatus(world, self, ws.UseSeq, action.ActionId, UseOutcome.Running, UseReason.Ok);
        }

        /// <summary>⭐ CE-1154 — the rule's answer buffer (<see cref="UseAction"/> carries its label, so it cannot live on the stack).</summary>
        [ThreadStatic] private static UseAction[]? s_useActions;

        /// <summary>⭐ CE-1154 — what the use press aimed at (its network id), or null: the rule still offers Put down / Get out then.</summary>
        private static Entity UseTargetOf(EntityRepository world, Entity self, long networkId)
        {
            if (networkId == 0 || !world.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()) return Entity.Null;
            return world.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()!.TryGetEntity(networkId, out var e)
                   && world.IsAlive(e) ? e : Entity.Null;
        }

        private static void WriteUseStatus(EntityRepository world, Entity self, byte useSeq, ushort actionId, UseOutcome outcome, UseReason reason)
        {
            if (!world.IsComponentTypeRegistered<OperatorUseStatus>()) world.RegisterComponent<OperatorUseStatus>();
            var status = new OperatorUseStatus { UseSeq = useSeq, ActionId = actionId, Outcome = outcome, Reason = reason };
            if (world.HasComponent<OperatorUseStatus>(self)) world.SetComponent(self, status);
            else world.AddComponent(self, status);
        }

        /// <summary>⭐ <c>CE-3172</c> S4d — seconds between an operator's rounds for a mount that states NO rate of fire. ⭐ <c>CE-1358</c>
        /// (DESIGN_Weapon_Layers §5k) — a mount with a <c>RateOfFire</c> fires at it (<see cref="OperatorCooldownFor"/>); its barrel heat
        /// stops the bursts. ⛔ Before: always this constant — an M134 fired 9 rounds in 2 s against its 3000/min.</summary>
        public const float OperatorCooldownSeconds = 0.25f;

        /// <summary>⭐ <c>CE-1358</c> — the gap the operator asks: 0 ("the mount's own", <c>AimAndFireExecutor.CooldownFor</c>) when the
        /// mount states a rate of fire, else <see cref="OperatorCooldownSeconds"/>.</summary>
        public static float OperatorCooldownFor(Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount)
            => mount is { RateOfFire: > 0f } ? 0f : OperatorCooldownSeconds;

        /// <summary>⭐ CE-1074 — how long the operator's stance change blends (s).</summary>
        public const float OperatorStanceBlendSeconds = 0.4f;

        /// <summary>
        /// ⭐ <c>CE-3172</c> S4d (M-8) — the operator's weapon: the unit's weapon channel runs <c>AimAndFire</c> in OPERATOR mode,
        /// aimed at the frame's point (<c>AimAndFireExecutor</c> keeps ammunition, magazine, cooldown and the friendly line; it skips
        /// seen-now, aim time and the ROE — C-2). Started once, then steered in place each frame. Stamped with this run's id, so
        /// <c>ChannelArbitrationSystem</c> stops it the moment the possession ends.
        /// <para>⭐⭐ CE-1078 (§8e D-1) — the LATCH: a new trigger press (<see cref="OperatorInput.ShotSeq"/>) locks ONE shot on the unit
        /// under the crosshair (or the point) and caps the action's rounds at one more; the weapon slews and fires when it bears, then
        /// follows the crosshair again. RMB (<see cref="OperatorInput.CancelSeq"/>) drops it. A held trigger keeps firing only an
        /// automatic weapon (<see cref="WeaponBrainProfiles.FiresPerPress"/>). ⭐ CE-1079 (D-2/D-3) — <c>F</c>
        /// (<see cref="OperatorInput.AmmoSeq"/>) moves the selected weapon to its next round, costing one reload.</para>
        /// </summary>
        private static unsafe void OperateWeapon(EntityRepository world, Entity self, ref ManualControlState ws, uint lastSeq)
        {
            if (!world.HasComponent<WeaponChannel>(self) || !world.IsComponentTypeRegistered<OperatorInput>()
                || !world.HasComponent<OperatorInput>(self)) return;
            var input = world.GetComponentRO<OperatorInput>(self);
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            ref var p = ref System.Runtime.CompilerServices.Unsafe.As<byte, AimAndFireParams>(ref channel.Params[0]);
            bool restarted = false;
            if (channel.ActiveAction != CombatConstants.ActionIdAimAndFire || p.Operator == 0 || channel.Status != NodeStatus.Running)
            {
                if (world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(self))
                    channel.BehaviorInstanceId = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(self).InstanceId;
                unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
                channel.Status = NodeStatus.Running;
                restarted = true;
            }
            // a new action counts its rounds from 0 (OnEnter), so a waiting shot needs one round of the NEW count
            int fired = restarted ? 0 : AimAndFireExecutor.RoundsFiredOf(ref channel);
            if (restarted && ws.Pending != 0) ws.Cap = 1;

            // a new station's frames start again from 1, and its counters from 0
            if (input.Seq < lastSeq) { ws.ShotSeq = ws.CancelSeq = ws.AmmoSeq = 0; ws.Pending = 0; }
            if (input.CancelSeq != ws.CancelSeq) { ws.CancelSeq = input.CancelSeq; ws.Pending = 0; }
            // ⭐ CE-1155 (§8h U-5; Weapon Systems §6 WS-6) — a CONTROLLED index (32+k: the weapon whose gunner seat he holds) names
            //   that weapon's own mount 0: its type decides the fire mode and the per-press rule, and its WeaponState carries the ammo
            var (platform, platformMount) = OperatedMount(world, self, input.Mount);
            var mountDto = CombatTkb.MountOf(world, platform, platformMount);
            // ⭐ CE-1127 (§8g F-1) — a thrown / arc or a guided weapon LOCKS; a rifle, a mount with no turret, an unguided rocket fires by
            //   SIGHT (the round goes where the crosshair is when it leaves). ⭐ CE-4143 (§8r) — a rapid-fire TURRET weapon is SIGHT too
            //   (and the operator's L preference, input.FirePref, switches a direct-fire turret weapon between the two).
            var mode = OperatorFireModes.Of(world, platform, mountDto,
                AmmoChoice.LoadedAmmo(world, WeaponMountQuery.MountEntity(world, platform, platformMount)), input.FirePref);
            // ⭐ CE-4143 (R-3, R-4) — SIGHT on a TURRET weapon: aim along the look ray at the zero range, fire along the barrel
            //   (a frame with no look ray — an older station — keeps the crosshair point and the on-target gate)
            bool alongBarrel = mode == OperatorFireMode.Sight && input.LookDir.LengthSquared() > 0.5f
                && Fdp.Toolkit.Combat.WeaponBrainProfiles.For(mountDto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown)
                       .UsesTurretTask(world, platform, mountDto);
            var lookPoint = input.LookFrom + input.LookDir * OperatorFireModes.SightZeroMetres;
            if (input.ShotSeq != ws.ShotSeq)
            {
                ws.ShotSeq = input.ShotSeq;
                if (mode == OperatorFireMode.Lock)
                {
                    ws.Pending = 1;
                    ws.PendingTarget = ShotTargetOf(world, self, input.ShotTarget);
                    // ⭐ CE-1125 — a point on a unit is held in HIS frame, so the shot goes where the crosshair was on him as he moves
                    ws.PendingPoint = ws.PendingTarget.IsNull ? input.AimPoint : AimPoint.ToUnitFrame(world, ws.PendingTarget, input.AimPoint);
                }
                else
                {
                    ws.Pending = 2;
                    ws.PendingTarget = Entity.Null;
                }
                ws.Cap = fired + 1;
            }
            int ammoSteps = (byte)(input.AmmoSeq - ws.AmmoSeq);
            if (ammoSteps > 0)
            {
                ws.AmmoSeq = input.AmmoSeq;
                var mountEntity = WeaponMountQuery.MountEntity(world, platform, platformMount);
                if (world.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(mountEntity))
                    AmmoChoice.Cycle(world, mountEntity, ammoSteps, world.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponState>(mountEntity).ReloadSeconds);
            }
            if (ws.Pending != 0 && (fired >= ws.Cap || !ws.PendingTarget.IsNull && !Standing(world, ws.PendingTarget)))
                ws.Pending = 0;   // the round left — or the unit it was locked on is gone

            var category = mountDto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown;
            bool held = input.Trigger != 0 && !WeaponBrainProfiles.FiresPerPress(category);
            p = ws.Pending != 0
                ? new AimAndFireParams
                {
                    Operator = ws.Pending == 2 && alongBarrel ? AimAndFireParams.OperatorAlongBarrel : (byte)1,   // ⭐ CE-4143
                    Trigger = 1, Mount = input.Mount, Target = ws.PendingTarget, Rounds = ws.Cap,
                    Point = ws.Pending == 2 ? (alongBarrel ? lookPoint : input.AimPoint)   // ⭐ CE-1127 — SIGHT: this tick's crosshair (CE-4143: a turret's look ray)
                          : ws.PendingTarget.IsNull ? ws.PendingPoint : AimPoint.FromUnitFrame(world, ws.PendingTarget, ws.PendingPoint),
                    CooldownSeconds = OperatorCooldownFor(mountDto),
                }
                : new AimAndFireParams
                {
                    Operator = alongBarrel ? AimAndFireParams.OperatorAlongBarrel : (byte)1,   // ⭐ CE-4143
                    Trigger = held ? (byte)1 : (byte)0, Mount = input.Mount, Point = alongBarrel ? lookPoint : input.AimPoint,
                    CooldownSeconds = OperatorCooldownFor(mountDto),
                };
        }

        /// <summary>⭐ <c>CE-1155</c> — the entity and mount index a frame's <c>Mount</c> names: the man's own mount, or — for a controlled
        /// index (<see cref="WeaponSystems.IsControlledIndex"/>) — the weapon system he controls and ITS mount 0, as
        /// <c>AimAndFireExecutor</c> resolves it when it fires. A controlled index with no weapon behind it stays on the man.</summary>
        private static (Entity Platform, int Mount) OperatedMount(EntityRepository world, Entity self, int mount)
        {
            if (!WeaponSystems.IsControlledIndex(mount)) return (self, mount);
            var weapon = WeaponSystems.ControlledAt(world, self, mount);
            return weapon.IsNull ? (self, mount) : (weapon, 0);
        }

        /// <summary>⭐ CE-1078 — the unit a press locked on (its network id; never the operator's own unit), or null for the point.</summary>
        private static Entity ShotTargetOf(EntityRepository world, Entity self, long networkId)
        {
            if (networkId == 0 || !world.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()) return Entity.Null;
            return world.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()!.TryGetEntity(networkId, out var e)
                   && e != self && world.IsAlive(e) ? e : Entity.Null;
        }

        private static bool Standing(EntityRepository world, Entity unit)
            => world.IsAlive(unit) && (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || !world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(unit)
                                       || world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(unit).Current > 0f);

        /// <summary>⭐ <c>CE-2135</c> (P3, B3b-1) — the operator's behaviour takes nothing: its block is internal (the possession sends
        /// <c>"{}"</c>, <c>Possession.cs</c>), so it names <see cref="NoParams"/> and this resolver starts the block clean (the Disembark shape).</summary>
        [BehaviorResolver(BehaviorNames.OperatorControl)]
        public static void Resolve(in NoParams authored, ref OperatorControlBlackboard block, EntityRepository world, Entity self)
            => block.Control = default;

        [Behavior(BehaviorNames.OperatorControl, Params = typeof(NoParams))]   // ⭐ CE-2135 (P3) — it takes nothing (P1: NoParams)
        [BTreeDefinition(BehaviorNames.OperatorControl, Curated = true, Group = "Operator", ParamsType = typeof(OperatorControlBlackboard))]
        public static BTreeBuilder<OperatorControlBlackboard, BTreeContext> BuildOperatorControlTree()
            => new BTreeBuilder<OperatorControlBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Control, bb => bb.ControlState, ManualControl);
    }
}
