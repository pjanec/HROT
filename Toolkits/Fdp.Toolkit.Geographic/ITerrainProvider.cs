using Fdp.Kernel.Collections;
using Fdp.Modules.Geographic.Components;

namespace Fdp.Modules.Geographic
{
    /// <summary>
    /// Abstraction over the IG engine's terrain sampling API.
    /// Implemented by the concrete IG integration (e.g. a VBS4 or COTS engine adapter).
    /// <para>
    /// Consumed by <c>TerrainQuerySolverSystem</c>, which holds this reference and calls
    /// <see cref="QueryBatch"/> once per frame after the request list has been populated
    /// by <c>TerrainQuerySubmitSystem</c>.
    /// </para>
    /// </summary>
    public interface ITerrainProvider
    {
        /// <summary>
        /// Fills <paramref name="results"/> with terrain-height hits for the first
        /// <paramref name="count"/> entries in <paramref name="requests"/>.
        ///
        /// <para>
        /// Implementations must write exactly one <see cref="TerrainQueryResult"/> per request
        /// at the matching array index.  Results for indices &gt;= <paramref name="count"/> may
        /// be left uninitialised.
        /// </para>
        /// </summary>
        /// <param name="requests">Pre-filled request array (read-only from the provider's perspective).</param>
        /// <param name="count">Number of valid entries to process.</param>
        /// <param name="results">Pre-allocated results array to fill (parallel to <paramref name="requests"/>).</param>
        void QueryBatch(
            NativeArray<TerrainQueryRequest> requests,
            int count,
            NativeArray<TerrainQueryResult> results);
    }
}
