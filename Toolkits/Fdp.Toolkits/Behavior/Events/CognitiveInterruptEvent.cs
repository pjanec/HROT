using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Multiplexed diagnostic event published when an edge-triggered interrupt
    /// fires on the entity's blackboard.
    /// </summary>
    [EventId(BehaviorConstants.EventId_CognitiveInterrupt)]
    [StructLayout(LayoutKind.Sequential)]
    public struct CognitiveInterruptEvent
    {
        /// <summary>The entity that received the interrupt.</summary>
        public Entity Entity;

        /// <summary>The specific interrupt that was triggered.</summary>
        public CognitiveInterruptType InterruptType;
    }
}
