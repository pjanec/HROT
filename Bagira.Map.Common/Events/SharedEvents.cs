using Fdp.Kernel;

namespace Bagira.Map.Common.Events
{
    public static class SharedEventIds
    {
        public const int FireInteractionEventId = 3001;
    }

    /// <summary>
    /// Fired when a combat interaction occurs.
    /// Positions are in FDP world-space metres (X = east, Y = north).
    /// </summary>
    [EventId(SharedEventIds.FireInteractionEventId)]
    public struct FireInteractionEvent
    {
        public float ShooterX;
        public float ShooterY;
        public float TargetX;
        public float TargetY;
    }
}
