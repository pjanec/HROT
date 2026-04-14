using System;
using System.IO;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Scenario;

/// <summary>
/// Hrot-specific <see cref="IScenarioLoader"/> implementation that selects scenario
/// files by matching <c>Header.SubsystemType</c> from the scenario envelope.
/// </summary>
public sealed class HrotScenarioLoader : IScenarioLoader
{
    private readonly IScenarioStorageProvider _storageProvider;
    private readonly string _targetSubsystemType;

    public HrotScenarioLoader(
        IScenarioStorageProvider storageProvider,
        string targetSubsystemType)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _targetSubsystemType = targetSubsystemType ?? throw new ArgumentNullException(nameof(targetSubsystemType));
    }

    public string? TryLoadScenarioJson(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId)) return null;

        foreach (var fileName in _storageProvider.EnumerateScenarioFiles(scenarioId))
        {
            try
            {
                using var stream = _storageProvider.OpenScenarioFile(scenarioId, Path.GetFileName(fileName));
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();

                var subsystemType = HrotScenarioEnvelope.PeekSubsystemType(text);
                if (HrotScenarioEnvelope.IsMatchingSubsystem(subsystemType, _targetSubsystemType))
                    return text;
            }
            catch
            {
                // Ignore malformed or unreadable candidates and continue scanning.
            }
        }

        return null;
    }
}
