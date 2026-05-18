using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Headless search service that replays a recording file and returns matching results.
    /// Each call creates its own isolated EntityRepository and PlaybackController.
    /// </summary>
    public interface IRecordingSearchService
    {
        /// <summary>
        /// Executes a search over all frames of the recording at <paramref name="fdpPath"/>.
        /// Returns one <see cref="SearchResultDto"/> per matching (frame, entity) pair.
        /// </summary>
        IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root);

        /// <summary>
        /// Scans the recording for entity birth/death ranges that match <paramref name="criteria"/>.
        /// </summary>
        IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(
            string fdpPath,
            LifecyclePredicateDto criteria);
    }
}
