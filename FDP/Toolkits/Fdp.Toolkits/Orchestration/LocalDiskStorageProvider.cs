using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// File-system implementation of <see cref="IScenarioStorageProvider"/> that stages
    /// scenario files under a configurable local root directory (default: see
    /// <see cref="OrchestrationConstants.ResolveStagingRoot"/>).
    ///
    /// <para>
    /// Each simulation node has a local staging root where the SMB-gateway prefetch copies
    /// scenario JSON files before the node handlers read them.
    /// </para>
    /// </summary>
    public sealed class LocalDiskStorageProvider : IScenarioStorageProvider
    {
        private readonly string _localTempRoot;

        /// <param name="localTempRoot">
        /// Root staging directory.  Defaults to <see cref="OrchestrationConstants.ResolveStagingRoot"/>.
        /// </param>
        public LocalDiskStorageProvider(string? localTempRoot = null)
        {
            localTempRoot ??= OrchestrationConstants.ResolveStagingRoot();
            if (string.IsNullOrWhiteSpace(localTempRoot))
                throw new ArgumentException("localTempRoot must not be null or whitespace.", nameof(localTempRoot));
            _localTempRoot = localTempRoot;
        }

        /// <inheritdoc />
        public Stream? OpenScenarioFile(string scenarioId, string fileName)
        {
            var path = Path.Combine(_localTempRoot, OrchestrationConstants.ScenariosDirectoryName, scenarioId, fileName);
            return File.Exists(path) ? File.OpenRead(path) : null;
        }

        /// <inheritdoc />
        public string EnsureStagingDirectory(string scenarioId)
        {
            var dir = Path.Combine(_localTempRoot, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <inheritdoc />
        public IEnumerable<string> EnumerateScenarioFiles(string scenarioId)
        {
            var dir = Path.Combine(_localTempRoot, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json")
                : Enumerable.Empty<string>();
        }
    }
}
