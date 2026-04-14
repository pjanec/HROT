using Fdp.Core;

namespace Hrot.SimHost
{
    /// <summary>
    /// Stable event-type identifiers for SimHost events.
    /// </summary>
    public static class SimHostEventIds
    {
        /// <summary>Fired to request a perspective switch between IG and Sim views.</summary>
        public const int TogglePerspective = 6001;

        /// <summary>Published by MissionControlExecutionSystem to acknowledge a mission command.</summary>
        public const int MissionControlAck = 6002;
    }

}