using System;
using System.IO;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;
using Hrot.ScenarioEditor.Events;

namespace Hrot.ScenarioEditor.Services;

/// <summary>
/// Provides local scenario file operations: <see cref="NewScenario"/>,
/// <see cref="SaveScenario"/>, and <see cref="LoadScenario"/>.
///
/// <para>
/// All three operations that modify world state publish a <see cref="WorldResetEvent"/>
/// synchronously BEFORE calling <c>repo.Clear()</c> to let consumers (selection managers,
/// active tools) flush any cached <see cref="Entity"/> handles before the repository is wiped.
/// </para>
/// </summary>
public sealed class ScenarioFileService
{
    private static readonly string[] AcceptedSubsystemTypes =
    {
        "Hrot.Scenario",
        "Hrot.SimHost",
        "Hrot.CGF",
    };

    private readonly ScenarioSerializer _serializer;
    private Action? _worldResetObservers;

    public ScenarioFileService(ScenarioSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Register a synchronous callback that is invoked immediately before
    /// <c>repo.Clear()</c> in <see cref="NewScenario"/> and <see cref="LoadScenario"/>.
    /// Use this to flush cached entity handles.
    /// </summary>
    public void RegisterWorldResetObserver(Action callback)
    {
        _worldResetObservers += callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>
    /// Fires all registered reset observers, then clears the repository.
    /// </summary>
    public void NewScenario(EntityRepository repo)
    {
        if (repo == null) throw new ArgumentNullException(nameof(repo));
        FireWorldReset();
        repo.SoftClear();
    }

    /// <summary>
    /// Serializes the repository state to a JSON file at <paramref name="filePath"/>.
    /// </summary>
    public void SaveScenario(EntityRepository repo, string filePath)
    {
        if (repo == null)     throw new ArgumentNullException(nameof(repo));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        var header = new ScenarioHeader("Hrot.Scenario");
        var dom    = _serializer.Serialize(repo, header);
        File.WriteAllText(filePath, dom.ToJsonString());
    }

    /// <summary>
    /// Loads a scenario from a JSON file into <paramref name="repo"/>.
    /// Fires reset observers and clears repo before deserializing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When the file's <c>SubsystemType</c> header is not recognized.
    /// </exception>
    public void LoadScenario(EntityRepository repo, string filePath)
    {
        if (repo == null)     throw new ArgumentNullException(nameof(repo));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        var jsonText = File.ReadAllText(filePath);

        // Validate header before destructively clearing the repo.
        ValidateSubsystemType(jsonText);

        FireWorldReset();
        repo.SoftClear();

        _serializer.Deserialize(repo, jsonText);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void FireWorldReset()
    {
        _worldResetObservers?.Invoke();
    }

    private static void ValidateSubsystemType(string jsonText)
    {
        // Quick header peek: deserialize only enough to check SubsystemType.
        using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
        if (!doc.RootElement.TryGetProperty("Header", out var header))
            throw new InvalidOperationException(
                "[ScenarioFileService] File is missing the 'Header' section.");

        if (!header.TryGetProperty("SubsystemType", out var typeElem))
            throw new InvalidOperationException(
                "[ScenarioFileService] Header is missing 'SubsystemType'.");

        var subsystemType = typeElem.GetString() ?? string.Empty;
        if (Array.IndexOf(AcceptedSubsystemTypes, subsystemType) < 0)
            throw new InvalidOperationException(
                $"[ScenarioFileService] Unrecognized SubsystemType '{subsystemType}'. " +
                $"Accepted: {string.Join(", ", AcceptedSubsystemTypes)}.");
    }
}
