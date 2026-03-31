using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDP.Toolkit.Orchestration;

namespace Hrot.Common.Orchestration
{
    /// <summary>
    /// File-system implementation of <see cref="IScenarioStorageProvider"/> that stages
    /// scenario files under <c>C:\FDP_Temp\&lt;scenarioId&gt;\</c> (or a configured root).
    ///
    /// <para>
    /// This is the canonical Hrot implementation — each simulation node has a local
    /// staging root (typically <c>C:\FDP_Temp</c>) where the SMB-gateway prefetch copies
    /// scenario JSON files before the node handlers read them.
    /// </para>
    /// </summary>
    public sealed class LocalDiskStorageProvider : IScenarioStorageProvider
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";
        private readonly string _localTempRoot;

        /// <param name="localTempRoot">
        /// Root staging directory.  Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public LocalDiskStorageProvider(string localTempRoot = DefaultLocalTempRoot)
        {
            if (string.IsNullOrWhiteSpace(localTempRoot))
                throw new ArgumentException("localTempRoot must not be null or whitespace.", nameof(localTempRoot));
            _localTempRoot = localTempRoot;
        }

        /// <inheritdoc />
        public Stream? OpenScenarioFile(string scenarioId, string fileName)
        {
            var path = Path.Combine(_localTempRoot, scenarioId, fileName);
            return File.Exists(path) ? File.OpenRead(path) : null;
        }

        /// <inheritdoc />
        public string EnsureStagingDirectory(string scenarioId)
        {
            var dir = Path.Combine(_localTempRoot, scenarioId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <inheritdoc />
        public IEnumerable<string> EnumerateScenarioFiles(string scenarioId)
        {
            var dir = Path.Combine(_localTempRoot, scenarioId);
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json")
                : Enumerable.Empty<string>();
        }
    }
}
