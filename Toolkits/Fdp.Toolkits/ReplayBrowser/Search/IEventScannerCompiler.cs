using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Invoked once per simulation frame to scan event bus data and append results.
    /// </summary>
    /// <param name="bus">The event bus containing data for the current frame.</param>
    /// <param name="frame">Zero-based frame index.</param>
    /// <param name="ticks">Wall-clock ticks for the frame (UTC).</param>
    /// <param name="results">Mutable output list; append SearchResultDto entries here.</param>
    public delegate void EventScannerDelegate(
        FdpEventBus bus,
        int frame,
        long ticks,
        List<SearchResultDto> results);

    /// <summary>
    /// Compiles a <see cref="TransientEventPredicateDto"/> into an
    /// <see cref="EventScannerDelegate"/> that reads the event bus for a single frame.
    /// </summary>
    public interface IEventScannerCompiler
    {
        /// <summary>
        /// Compiles the predicate into a scanner delegate.
        /// The delegate is stateless and can be reused across frames and threads.
        /// </summary>
        EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate);
    }
}
