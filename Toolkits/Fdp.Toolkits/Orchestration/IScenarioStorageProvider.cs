namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Abstracts the "where and how" of scenario file staging, replacing the raw
    /// <c>localTempRoot</c> string currently threaded through all handler
    /// constructors.
    ///
    /// <para>
    /// The Hrot reference implementation, <c>LocalDiskStorageProvider</c>,
    /// wraps <c>C:\FDP_Temp\{scenarioId}\</c> as the staging root.  Applications
    /// that mount their staging area elsewhere pass a different root path.
    /// </para>
    /// </summary>
    public interface IScenarioStorageProvider
    {
        /// <summary>
        /// Returns a read-only stream for the named file within a scenario's staging
        /// directory.  Returns <c>null</c> when the file does not exist.
        /// </summary>
        Stream? OpenScenarioFile(string scenarioId, string fileName);

        /// <summary>
        /// Ensures the staging directory for <paramref name="scenarioId"/> exists and
        /// returns its absolute path (used by reference handlers that need a local
        /// filesystem path, e.g. for recording file output).
        /// </summary>
        string EnsureStagingDirectory(string scenarioId);

        /// <summary>
        /// Enumerates all JSON files in the staging directory for
        /// <paramref name="scenarioId"/>.  Returns an empty sequence when the
        /// directory does not exist.
        /// </summary>
        IEnumerable<string> EnumerateScenarioFiles(string scenarioId);
    }
}
