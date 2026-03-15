namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// Toolkit-local component ID registry for <c>FDP.Toolkit.Replay</c>.
    /// Follows the per-toolkit registry pattern established in Phase 5.
    /// IDs 84–85 are reserved for story recording/replay tag components.
    /// </summary>
    public static class ReplayComponentIds
    {
        /// <summary>Component ID for <c>StoryTag</c>.</summary>
        public const byte StoryTag        = 84;

        /// <summary>Component ID for <c>StoryReplayTag</c>.</summary>
        public const byte StoryReplayTag  = 85;
    }
}
