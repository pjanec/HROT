using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Core.Diagnostics
{
    /// <summary>
    /// Headless service that reads log files written by the current node, filters lines by
    /// severity and age, and writes the result to a single archive file.
    /// </summary>
    public interface ILogArchiveExtractionService
    {
        /// <summary>
        /// Scans the configured log directory, applies filters, and writes matching lines to
        /// <paramref name="targetFilePath"/>.
        /// </summary>
        /// <param name="targetFilePath">Destination file that will be created or overwritten.</param>
        /// <param name="severityThreshold">
        /// Minimum severity level to include.  Lines with a severity value below this threshold
        /// are skipped.  The value is compared against the numeric severity extracted from the
        /// log line prefix (e.g. <c>[3]</c> = WARNING).
        /// </param>
        /// <param name="maxAgeHours">
        /// Maximum age of a log line in hours relative to the time the call is made.
        /// Lines with a timestamp older than <c>now - maxAgeHours</c> are skipped.
        /// Use <see cref="float.MaxValue"/> to skip age filtering.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Number of lines written to the archive.</returns>
        Task<int> ExtractLogsAsync(
            string targetFilePath,
            int    severityThreshold,
            float  maxAgeHours,
            CancellationToken ct = default);
    }
}
