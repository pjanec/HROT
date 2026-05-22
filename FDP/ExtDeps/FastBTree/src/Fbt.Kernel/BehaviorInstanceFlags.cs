using System;

namespace Fbt
{
    /// <summary>
    /// Per-instance control flags for a running behavior tree.
    /// </summary>
    [Flags]
    public enum BehaviorInstanceFlags : byte
    {
        None   = 0,
        Paused = 1 << 0,
    }
}
