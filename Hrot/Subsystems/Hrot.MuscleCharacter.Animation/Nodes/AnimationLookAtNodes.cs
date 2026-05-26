using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.MuscleCharacter.Animation.Nodes
{
    /// <summary>
    /// Blueprint action node for aiming at a fixed world-space point.
    /// Emits a LookAtPointParams command to the LookAtChannel.
    /// Capability-gated: requires CanAim capability on target character (Phase 3 DD-1 §20.6).
    /// (ANC-P5-04, DD-5 §5.1)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LookAtPointNode
    {
        /// <summary>Target entity to aim on (typically the character actor).</summary>
        public uint TargetCharacter;

        /// <summary>World-space target point to aim at.</summary>
        public float TargetPointX;
        public float TargetPointY;
        public float TargetPointZ;

        /// <summary>Time in seconds to blend aim weight from 0 to full (default 0.1).</summary>
        public float BlendInTime;

        /// <summary>Priority for conflict arbitration (higher = more important; default 0).</summary>
        public byte Priority;
    }

    /// <summary>
    /// Blueprint action node for aiming at a target entity.
    /// Emits a LookAtEntityParams command to the LookAtChannel.
    /// The entity position is resolved at runtime by the LookAtRuntimeBridgeSystem.
    /// Capability-gated: requires CanAim capability on target character (Phase 3 DD-1 §20.6).
    /// (ANC-P5-04, DD-5 §5.2)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LookAtEntityNode
    {
        /// <summary>Target entity to aim on (typically the character actor).</summary>
        public uint TargetCharacter;

        /// <summary>Entity to aim at (resolved via network entity map at runtime).</summary>
        public uint TargetEntity;

        /// <summary>Local offset from target entity's origin (e.g., chest height ~1.5m).</summary>
        public float OffsetFromTargetX;
        public float OffsetFromTargetY;
        public float OffsetFromTargetZ;

        /// <summary>Time in seconds to blend aim weight from 0 to full (default 0.1).</summary>
        public float BlendInTime;

        /// <summary>Priority for conflict arbitration (default 0).</summary>
        public byte Priority;
    }

    /// <summary>
    /// Blueprint action node for releasing aim (blend-out).
    /// Emits a ReleaseLookParams command to the LookAtChannel.
    /// Can be called with or without a prior look-at action (ANIM009 warning if not).
    /// (ANC-P5-04, DD-5 §5.3)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReleaseLookNode
    {
        /// <summary>Target entity to release aim on.</summary>
        public uint TargetCharacter;

        /// <summary>Time in seconds to blend aim weight back to 0 (default 0.2).</summary>
        public float BlendOutTime;
    }
}
