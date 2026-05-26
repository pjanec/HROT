using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.MuscleCharacter.Animation.Nodes
{
    /// <summary>
    /// Blueprint getter node for reading animation montage queue progress.
    /// Returns the current queue index, elapsed time, and active status.
    /// Read-only; does not mutate state.
    /// Used in Blueprints to implement conditional logic based on queue playback state.
    /// Can also be used in BTree conditions and HSM transition guards via AiPrimitive dispatch.
    /// (ANC-P5-05, DD-5 §6.1)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GetMontageQueueProgressNode
    {
        /// <summary>Target entity to read the queue from.</summary>
        public uint TargetCharacter;
    }

    /// <summary>
    /// Blueprint getter node for reading current stance status.
    /// Returns the current stance, transition phase, and blend weight.
    /// Read-only; does not mutate state.
    /// Used in Blueprints to implement conditional stance-based logic.
    /// Can also be used in BTree conditions and HSM transition guards via AiPrimitive dispatch.
    /// (ANC-P5-05, DD-5 §6.2)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GetCurrentStanceNode
    {
        /// <summary>Target entity to read the stance from.</summary>
        public uint TargetCharacter;
    }
}
