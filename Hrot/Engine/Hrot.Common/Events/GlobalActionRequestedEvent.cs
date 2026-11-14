using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Common.Events
{
    // Published by ContextActionIngressSystem when a ContextActionTriggered managed
    // event arrives with a parsable integer action name. GlobalActionDispatchSystem
    // reads these and dispatches to the registered handler.
    [EventId(8059)]
    [DataPolicy(DataPolicy.NoReplay)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GlobalActionRequestedEvent
    {
        // Numeric action identifier (see GlobalActionIds).
        public int    ActionId;
        // Local entity that is the target of the action, or Entity.Null for canvas actions.
        public Entity Target;
        // ⭐ CE-3178 — 1 when the click was made on another node and relayed here (ContextActionTriggered.Relayed);
        //   GlobalActionRegistry.RegisterLocal handlers do not run for it.
        public byte   Relayed;
    }
}
