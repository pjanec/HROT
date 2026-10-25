using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> — WHY a channel's action is (not) doing its thing this frame, as the executor last saw it.
    /// 📄 docs/DESIGN_Ai_Action_Status_Gizmo.md §2 T1.
    /// </summary>
    public enum ActionReason : byte
    {
        None = 0,
        /// <summary>Moving toward the destination (Progress = metres left).</summary>
        Moving,
        /// <summary>Aiming at a SEEN target (Progress of Needed seconds).</summary>
        Aiming,
        /// <summary>A round left this frame.</summary>
        Firing,
        /// <summary>Waiting for the weapon to cycle (Progress = seconds left).</summary>
        Cooldown,
        /// <summary>Holding: the target is not seen now (D4).</summary>
        HoldNotSeen,
        /// <summary>Holding: a friendly is on the line of fire (CE-321).</summary>
        HoldFriendlyOnLine,
        /// <summary>Holding: the rules of engagement forbid firing (CE-2075).</summary>
        HoldRoe,
        /// <summary>Changing the magazine (Progress of Needed seconds, D9).</summary>
        Reloading,
        /// <summary>No ammunition left.</summary>
        OutOfAmmo,
        /// <summary>The action ended successfully (target down, rounds fired, arrived).</summary>
        Done,
        /// <summary>The action failed.</summary>
        Failed,
        /// <summary>⭐ WL-3 — the turret is turning onto the target (DESIGN_Weapon_Layers §5e).</summary>
        Slewing,
        /// <summary>⭐ WL-3 — the target needs an angle outside the turret's limits (W-8).</summary>
        OutOfArc,
        /// <summary>⭐ WL-4 (W-12e) — the target is hidden: a grenade went to where it was last seen.</summary>
        Throwing,
        /// <summary>⭐ AQ89 A (R-269) — the target is hidden: suppressive bursts at where it was last seen.</summary>
        BlindFire,
        /// <summary>⭐ W-12c — a guided round is in flight: the launcher stays on the target and the next waits.</summary>
        Guiding,
        /// <summary>⭐ <c>CE-3195</c> (W-14a) — the target is beyond a person's twist: the round is held while his body turns to it.</summary>
        Turning,
        /// <summary>⭐ M4 (DESIGN_Missiles_And_Ammo §3) — the target is closer than the missile's minimum range: the weapon holds.</summary>
        TooClose,
        /// <summary>⭐ D-L (§4) — this weapon does not fire on the move (an ATGM, a mortar) and the unit is moving: the weapon holds.</summary>
        HoldMoving,
        /// <summary>⭐ Air Ops slice 4 — a bomb waits for its release cue (BombBallistics): Progress = seconds to release.</summary>
        AwaitingRelease,
        /// <summary>⭐ Weapon systems WS-3 (W-D) — the chosen weapon system is not this unit's to fire (another controls it, or nobody
        /// mans it): the weapon holds.</summary>
        NotControlled,
        /// <summary>⭐ WS-3 (W-I) — the weapon system's gunner (its station's operating seat, or its own gunner seat) is empty, down or
        /// dead: the gun is silent until somebody boards the seat.</summary>
        NoGunner,
        /// <summary>⭐ WS-3 (W-C) — the weapon system is stowed, deploying or packing.</summary>
        NotDeployed,
        /// <summary>⭐ Indirect fire IF-1 (I6, FM-4) — laid on the point, waiting for the fire time (<c>FireAtPointParams.FireAt</c>): time on
        /// target fires each gun at T − its time of flight.</summary>
        AwaitingFireTime,
        /// <summary>⭐ IF-1 (I1) — no charge reaches the point on an allowed arc (<c>FireRefusal.TooFar</c>).</summary>
        OutOfRange,
        /// <summary>⭐ IF-1 (I1) — a charge reaches, but the terrain or a building stops every allowed arc (<c>FireRefusal.Masked</c>).</summary>
        Masked,
        /// <summary>⭐ <c>CE-2187</c> (DESIGN_Missiles_And_Ammo §3c) — the loaded missile is for the other domain (a MANPADS at a truck, an
        /// ATGM at a flying helicopter): the weapon holds.</summary>
        WrongDomain,
        /// <summary>⭐ <c>CE-2233</c> (CIV-0; DESIGN_Civilians_And_Crowd_Control CV-8) — the target is a NEUTRAL (a civilian whose side has
        /// not flipped): the AI's weapon holds. An operator's (a human's) fire is not held.</summary>
        HoldNeutral,
        /// <summary>⭐ <c>AW-20</c> (R-2026; DESIGN_Civilians_And_Crowd_Control §5h) — a NEUTRAL person is on the line of fire or within the
        /// weapon's spread: the round is held (Running, nothing spent) until the line is clear.</summary>
        HoldNeutralOnLine,
        /// <summary>⭐ <c>CV-13a</c> (R-2024; DESIGN_Civilians_And_Crowd_Control §4) — the target shows HANDS UP: the AI's weapon holds, even
        /// at a Hostile. An operator's fire is not held.</summary>
        HoldSurrender,
        /// <summary>⭐ <c>CE-2239</c> part 1 (CV-8, §5j FL-2) — the unit's FORCE LEVEL forbids this round (Warn: any; NonLethal: a lethal
        /// one), and the target is no self-defence threat (AW-18): the round is held (Running, nothing spent).</summary>
        HoldForceLevel,
        /// <summary>⭐ <c>CE-2238</c> part 4 (AW-21, AW-22, §5h.7b CH-1) — CONDUCT: a lethal round at a civilian this unit neither sees armed
        /// nor knows as a combatant (non-lethal force only): held (Running, nothing spent).</summary>
        HoldConduct,
        /// <summary>⭐ <c>CE-2239</c> part 3 (B2, §5k EL-6) — the escalation ladder's WARN rung: the warning is given, no round (an armed man
        /// at ease, or a unit whose force level is Warn).</summary>
        HoldWarn,
        /// <summary>⭐ <c>CE-3255</c> (BR-2; DESIGN_Building_Clearing CB-6) — the SHOOTER is stunned (a flash-bang, <c>Effects.IsStunned</c>): no round
        /// leaves, whoever he is (Running, nothing spent), until the stun ends.</summary>
        HoldStunned,
        /// <summary>⭐ <c>CE-3256</c> (BR-4, CU-5) — the shooter is RESTRAINED (hands tied): no round leaves.</summary>
        HoldRestrained,
        /// <summary>⭐ <c>CE-2282</c> (DA-L) — the round would end inside a NoFire area of the shooter's chain (friends' rooms, the assault
        /// element): no round leaves, always — self-defence does not lift it.</summary>
        HoldNoFireArea,
        /// <summary>⭐ <c>CE-1358</c> (DESIGN_Weapon_Layers §5k) — the barrel is too hot: no round until it cools to half (Progress = heat,
        /// Needed = the share it fires again at). Appended.</summary>
        Overheated,
    }

    /// <summary>One channel's last status: the action, the reason, a progress pair, the target and when (sim seconds).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ActionStatusRow
    {
        public ushort       ActionId;
        public ActionReason Reason;
        public byte         Reserved;
        public float        Progress;
        public float        Needed;
        public Entity       Target;
        public double       At;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> (T1–T2, approved `2026-10-09`) — a brain unit's ACTION STATUS: one row per action channel, written by the
    /// EXECUTORS (the one place every behaviour's action passes), so the map can say <i>"W hold: not seen"</i> or <i>"W aiming
    /// 0.4/0.8 s"</i> for any behaviour without editing it. Provisioned with the channels at spawn (always present, T2 — not only
    /// while traced); recorded, never in a scenario, never on the wire (R-226). Drawn by <c>ActionStatusGizmo</c>, family
    /// <see cref="AiOverlayFlags.Channels"/>. 📄 docs/DESIGN_Ai_Action_Status_Gizmo.md.
    /// </summary>
    [ComponentId(GlobalComponentIds.ActionStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ActionStatus
    {
        public ActionStatusRow Locomotion;
        public ActionStatusRow Weapon;
        public ActionStatusRow Interaction;
    }

    /// <summary>The executors' one write path into <see cref="ActionStatus"/> (a no-op where the unit or the world has none).</summary>
    public static class ActionStatusOf
    {
        /// <summary>Sets <paramref name="unit"/>'s WEAPON row.</summary>
        public static void Weapon(EntityRepository world, Entity unit, ushort actionId, ActionReason reason, Entity target,
            float progress = 0f, float needed = 0f)
        {
            if (!world.IsComponentTypeRegistered<ActionStatus>() || !world.HasComponent<ActionStatus>(unit)) return;
            ref var s = ref world.GetComponentRW<ActionStatus>(unit);
            Write(ref s.Weapon, actionId, reason, target, progress, needed, Now(world));
        }

        /// <summary>⭐ <c>CE-2333</c> (AQ98 D, R-2070) — no weapon action runs: <paramref name="unit"/>'s WEAPON row says nothing
        /// (<see cref="ActionReason.None"/>) instead of keeping the last executor's word — 📐 it read "Aiming", stamped 10 s earlier, over a
        /// man with no fire action, and misled a diagnosis. A no-op when the row is already clear.</summary>
        public static void ClearWeapon(EntityRepository world, Entity unit)
        {
            if (!world.IsComponentTypeRegistered<ActionStatus>() || !world.HasComponent<ActionStatus>(unit)) return;
            if (world.GetComponentRO<ActionStatus>(unit).Weapon.Reason == ActionReason.None) return;
            ref var s = ref world.GetComponentRW<ActionStatus>(unit);
            Write(ref s.Weapon, 0, ActionReason.None, Entity.Null, 0f, 0f, Now(world));
        }

        /// <summary>Sets <paramref name="unit"/>'s LOCOMOTION row.</summary>
        public static void Locomotion(EntityRepository world, Entity unit, ushort actionId, ActionReason reason,
            float progress = 0f, float needed = 0f)
        {
            if (!world.IsComponentTypeRegistered<ActionStatus>() || !world.HasComponent<ActionStatus>(unit)) return;
            ref var s = ref world.GetComponentRW<ActionStatus>(unit);
            Write(ref s.Locomotion, actionId, reason, Entity.Null, progress, needed, Now(world));
        }

        /// <summary>A short label for <paramref name="r"/> (the gizmo's and the debug API's word for it).</summary>
        public static string Label(ActionReason r) => r switch
        {
            ActionReason.Moving             => "moving",
            ActionReason.Aiming             => "aiming",
            ActionReason.Firing             => "firing",
            ActionReason.Cooldown           => "cooldown",
            ActionReason.HoldNotSeen        => "hold: not seen",
            ActionReason.HoldFriendlyOnLine => "hold: friendly on line",
            ActionReason.HoldRoe            => "hold: ROE",
            ActionReason.Reloading          => "reloading",
            ActionReason.OutOfAmmo          => "out of ammo",
            ActionReason.Done               => "done",
            ActionReason.Failed             => "failed",
            ActionReason.Slewing            => "slewing",
            ActionReason.Throwing           => "throw: last seen",
            ActionReason.BlindFire          => "suppress: last seen",
            ActionReason.Guiding            => "guiding",
            ActionReason.Turning            => "turning",
            ActionReason.OutOfArc           => "out of arc",
            ActionReason.TooClose           => "hold: too close",
            ActionReason.HoldMoving         => "hold: moving",
            ActionReason.AwaitingRelease    => "release: waiting for the cue",
            ActionReason.WrongDomain        => "hold: wrong target domain",
            ActionReason.HoldNeutral        => "hold: neutral",              // ⭐ CE-2233 — a civilian
            ActionReason.HoldNeutralOnLine  => "hold: neutral on line",      // ⭐ AW-20 — a civilian in the line or the spread
            ActionReason.HoldSurrender      => "hold: hands up",             // ⭐ CV-13a — a surrender
            ActionReason.HoldForceLevel     => "hold: force level",          // ⭐ CE-2239 — the ROE caps the force
            ActionReason.HoldConduct        => "hold: unarmed civilian",     // ⭐ CE-2238 part 4 — non-lethal force only
            ActionReason.HoldWarn           => "hold: warning",              // ⭐ CE-2239 part 3 — the ladder's Warn rung
            ActionReason.HoldStunned        => "hold: stunned",              // ⭐ CE-3255 (BR-2) — a flash-bang
            ActionReason.HoldRestrained     => "hold: restrained",           // ⭐ CE-3256 (BR-4) — hands tied
            ActionReason.HoldNoFireArea     => "hold: no-fire area",         // ⭐ CE-2282 (DA-L) — a NoFire area of his chain
            ActionReason.NotControlled      => "hold: not controlled",       // ⭐ WS-3 — the weapon system
            ActionReason.NoGunner           => "hold: no gunner",
            ActionReason.NotDeployed        => "hold: not deployed",
            ActionReason.AwaitingFireTime   => "laid: waiting for the time",  // ⭐ IF-1
            ActionReason.OutOfRange         => "hold: out of range",
            ActionReason.Masked             => "hold: masked",               // ⭐ IF-1, CE-2218 — the terrain stops the round
            ActionReason.Overheated         => "hold: barrel too hot",       // ⭐ CE-1358 (§5k) — cooling to half
            _                               => "",
        };

        /// <summary>True for the reasons that mean "wants to act and cannot" — the gizmo draws them amber.</summary>
        public static bool IsHold(ActionReason r) => r is ActionReason.HoldNotSeen or ActionReason.HoldFriendlyOnLine
            or ActionReason.HoldRoe or ActionReason.OutOfAmmo or ActionReason.Failed or ActionReason.OutOfArc
            or ActionReason.TooClose or ActionReason.HoldMoving or ActionReason.WrongDomain
            or ActionReason.NotControlled or ActionReason.NoGunner or ActionReason.NotDeployed or ActionReason.OutOfRange
            or ActionReason.Masked or ActionReason.HoldNeutral or ActionReason.HoldNeutralOnLine
            or ActionReason.HoldSurrender or ActionReason.HoldForceLevel or ActionReason.HoldConduct or ActionReason.HoldWarn
            or ActionReason.HoldStunned or ActionReason.HoldRestrained or ActionReason.HoldNoFireArea
            or ActionReason.Overheated;   // ⭐ CE-3255 (BR-2) · CE-3256 (BR-4)   // ⭐ CE-2218 · ⭐ CE-2233 — the gizmo drew these unlabelled and not amber

        private static void Write(ref ActionStatusRow row, ushort actionId, ActionReason reason, Entity target, float progress,
            float needed, double now)
        {
            row.ActionId = actionId;
            row.Reason   = reason;
            row.Target   = target;
            row.Progress = progress;
            row.Needed   = needed;
            row.At       = now;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }
}
