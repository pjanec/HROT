using Fdp.Core;
namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// Toolkit-local component ID registry for <c>FDP.Toolkit.Replay</c>.
    /// Follows the per-toolkit registry pattern established in Phase 5.
    /// IDs 84–85 are reserved for episode recording/replay tag components.
    /// </summary>
    // ⛔ ALIASES ONLY (CE-4628 follow-up, user 2026-11-19: "all component ids ... written at one single place"): every id lives in
    //   Fdp.Core GlobalComponentIds (values unchanged); a NEW id is added THERE, never here. Rail: ComponentIdCatalogueTests.
    public static class ReplayComponentIds
    {
        /// <summary>Component ID for <c>EpisodeTag</c>.</summary>
        public const byte EpisodeTag        = GlobalComponentIds.EpisodeTag;

        /// <summary>Component ID for <c>EpisodeReplayTag</c>.</summary>
        public const byte EpisodeReplayTag  = GlobalComponentIds.EpisodeReplayTag;
    }
}
