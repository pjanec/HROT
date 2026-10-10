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
            _                               => "",
        };

        /// <summary>True for the reasons that mean "wants to act and cannot" — the gizmo draws them amber.</summary>
        public static bool IsHold(ActionReason r) => r is ActionReason.HoldNotSeen or ActionReason.HoldFriendlyOnLine
            or ActionReason.HoldRoe or ActionReason.OutOfAmmo or ActionReason.Failed;

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
