using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>What a turret is told to do (DESIGN_Weapon_Layers W-2, W-11).</summary>
    public enum WeaponTaskMode : byte
    {
        /// <summary>No order: the turret holds its pose.</summary>
        Idle = 0,
        /// <summary>Keep the gun on <see cref="WeaponTask.Target"/> (or <see cref="WeaponTask.Point"/>) without firing.</summary>
        Track = 1,
        /// <summary>Track, and fire up to <see cref="WeaponTask.RoundsGranted"/> rounds from <see cref="WeaponTask.MountIndex"/> (WL-3).</summary>
        Engage = 2,
    }

    /// <summary>Where the weapon muscle is with a task (W-3).</summary>
    public enum WeaponTaskState : byte
    {
        None = 0,
        Slewing = 1,
        OnTarget = 2,
        Firing = 3,
        Done = 4,
        Failed = 5,
    }

    /// <summary>Why a task failed (W-3, W-8).</summary>
    public enum WeaponTaskFailure : byte
    {
        /// <summary>Not failed. ⚠ Not <c>None</c>: the DDS code generator puts this file's enums in ONE IDL scope, where it would
        /// collide with <see cref="WeaponTaskState.None"/>.</summary>
        NoFailure = 0,
        /// <summary>The aim needs an angle outside the turret's TKB limits.</summary>
        OutOfArc = 1,
        /// <summary>The target is gone (dead, despawned, or never resolved on this node).</summary>
        NoTarget = 2,
        /// <summary>The unit's type declares no such turret.</summary>
        NoTurret = 3,
    }

    /// <summary>
    /// ⭐ WL-2 (docs/DESIGN_Weapon_Layers.md W-2, W-11 / R-267) — the weapon brain's standing order to ONE turret, on the turret's part
    /// child. Written by the Brain (the weapon brain, WL-3), read by the weapon muscle (<c>WeaponTaskDispatcherSystem</c>). A new
    /// <see cref="Epoch"/> is a new order; the muscle echoes it in <see cref="WeaponTaskStatus.Epoch"/>.
    /// </summary>
    [ComponentId(GlobalComponentIds.WeaponTask)]
    public struct WeaponTask
    {
        public WeaponTaskMode Mode;
        /// <summary>The target entity; <see cref="Entity.Null"/> = aim at <see cref="Point"/>.</summary>
        public Entity Target;
        /// <summary>The aim point when there is no target entity (world metres).</summary>
        public Vector3 Point;
        /// <summary>The mount that fires (Engage) — its warhead decides direct or indirect aim.</summary>
        public byte MountIndex;
        /// <summary>Rounds the brain grants for this order (W-4); the muscle fires at most these.</summary>
        public ushort RoundsGranted;
        /// <summary>The ammunition to load (W-6); 0 = the mount's own.</summary>
        public ulong AmmoGuid;
        public uint Epoch;
    }

    /// <summary>⭐ WL-2 (W-3) — the weapon muscle's report on the turret's current <see cref="WeaponTask"/>, on the same part.</summary>
    [ComponentId(GlobalComponentIds.WeaponTaskStatus)]
    public struct WeaponTaskStatus
    {
        /// <summary>The <see cref="WeaponTask.Epoch"/> this report is about.</summary>
        public uint Epoch;
        public WeaponTaskState State;
        public WeaponTaskFailure Reason;
        public ushort RoundsFired;
    }
}
