using System;

namespace Fbt
{
    /// <summary>Per-instance control flags stored in BehaviorTreeState.</summary>
    [Flags]
    public enum BehaviorInstanceFlags : byte
    {
        None   = 0,
        Paused = 1,
    }
}
