using System;
using System.IO;
using System.Linq;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Reference <see cref="IScenarioLoader"/> implementation that returns the first
    /// staged JSON file for a scenario.
    /// </summary>
    public sealed class ReferenceScenarioLoader : IScenarioLoader
    {
        private readonly IScenarioStorageProvider _storageProvider;

        public ReferenceScenarioLoader(IScenarioStorageProvider storageProvider)
        {
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        }

        public string? TryLoadScenarioJson(string scenarioId)
        {
            if (string.IsNullOrWhiteSpace(scenarioId)) return null;

            var fileName = _storageProvider.EnumerateScenarioFiles(scenarioId).FirstOrDefault();
            if (fileName == null) return null;

            using var stream = _storageProvider.OpenScenarioFile(scenarioId, Path.GetFileName(fileName));
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
