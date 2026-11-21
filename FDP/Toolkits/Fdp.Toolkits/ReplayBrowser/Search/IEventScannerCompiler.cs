using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Invoked once per simulation frame to scan the frame's events and append results.
    /// </summary>
    /// <param name="repo">The world whose events are scanned — read through its repository reads, never its bus
    /// (CE-3293 S3, R-3036 rule 4: the live data breakpoints run in a step-only system).</param>
    /// <param name="frame">Zero-based frame index.</param>
    /// <param name="ticks">Wall-clock ticks for the frame (UTC).</param>
    /// <param name="results">Mutable output list; append SearchResultDto entries here.</param>
    public delegate void EventScannerDelegate(
        EntityRepository repo,
        int frame,
        long ticks,
        List<SearchResultDto> results,
        TargetEntityFilter? entityFilter);

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

        /// <summary>
        /// ⭐ R-4077 extension (docs/DESIGN_Director_Timeline_Editor.md §9c ③) — the same scan, ONE result per matching event (an
        /// <c>AnyOccurrence</c> predicate included), each carrying what <paramref name="projection"/> keeps. Without a projection it is
        /// <see cref="CompileScanner(TransientEventPredicateDto)"/>.
        /// </summary>
        EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate, ResultProjectionDto? projection)
            => projection == null ? CompileScanner(predicate) : throw new NotSupportedException($"{GetType().Name} cannot project results.");
    }
}
