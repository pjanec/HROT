using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Kernel;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
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
    private readonly FdpEventBus? _bus;
    private readonly IZoneManagerService? _zoneService;
    private Action? _worldResetObservers;

    public ScenarioFileService(
        ScenarioSerializer serializer,
        FdpEventBus? bus = null,
        IZoneManagerService? zoneService = null)
    {
        _serializer  = serializer  ?? throw new ArgumentNullException(nameof(serializer));
        _bus         = bus;
        _zoneService = zoneService;
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
        _worldResetObservers?.Invoke();   // synchronous callbacks BEFORE clear
        repo.SoftClear();                 // also clears Bus — so we publish bus event AFTER this
        // Reset simulation time: SoftClear() does not touch singletons.
        if (repo.HasSingletonUnmanaged<GlobalTime>())
            repo.SetSingletonUnmanaged(default(GlobalTime));
        _bus?.PublishManaged(new WorldResetEvent()); // bus event survives because it's after ClearAll
    }

    /// <summary>
    /// Serializes the repository state to a JSON file at <paramref name="filePath"/>.
    /// When an <see cref="IZoneManagerService"/> was supplied, the active zone definitions
    /// are included in the serialised envelope.
    /// </summary>
    public void SaveScenario(EntityRepository repo, string filePath)
    {
        if (repo == null)     throw new ArgumentNullException(nameof(repo));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        var header  = new ScenarioHeader("Hrot.Scenario");
        var fdpDom  = _serializer.Serialize(repo, header);

        var activeZones = _zoneService?.GetActiveZones();

        var envelope = new HrotScenarioEnvelopeDto
        {
            Header   = new ScenarioHeaderDto { SubsystemType = "Hrot.Scenario", SchemaVersion = "1.0" },
            Zones    = (activeZones != null && activeZones.Count > 0) ? activeZones : null,
            Entities = fdpDom["Entities"]?.AsObject() ?? fdpDom["entities"]?.AsObject(),
        };

        var json = JsonSerializer.Serialize(envelope, HrotSerializerOptions.HrotJsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a scenario from a JSON file into <paramref name="repo"/>.
    /// Fires reset observers and clears repo before deserializing.
    /// When an <see cref="IZoneManagerService"/> was supplied, zone data is loaded
    /// before entity deserialization using a single JSON parse (no double-parse).
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

        _worldResetObservers?.Invoke();   // synchronous callbacks BEFORE clear
        repo.SoftClear();                 // also clears Bus — so we publish bus event AFTER this
        // Reset simulation time: SoftClear() does not touch singletons.
        if (repo.HasSingletonUnmanaged<GlobalTime>())
            repo.SetSingletonUnmanaged(default(GlobalTime));
        _bus?.PublishManaged(new WorldResetEvent()); // bus event survives because it's after ClearAll

        if (_zoneService != null)
        {
            // Single JSON parse discipline: parse once, use DOM for both DTOs and FDP serializer.
            var dom      = JsonNode.Parse(jsonText)?.AsObject();
            var envelope = dom?.Deserialize<HrotScenarioEnvelopeDto>(HrotSerializerOptions.HrotJsonOptions);

            if (envelope?.Zones != null)
                _zoneService.LoadZones(repo, envelope.Zones);

            // Only call Deserialize when an Entities section is present.
            // Zone-only scenarios omit the Entities key (WhenWritingNull) and ScenarioSerializer
            // would throw on a missing Entities node, so we guard here.
            if (dom != null && (dom["Entities"] != null || dom["entities"] != null))
                _serializer.Deserialize(repo, dom);
        }
        else
        {
            _serializer.Deserialize(repo, jsonText);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void FireWorldReset()
    {
        _worldResetObservers?.Invoke();
        _bus?.PublishManaged(new WorldResetEvent());
    }

    private static void ValidateSubsystemType(string jsonText)
    {
        // Quick header peek: deserialize only enough to check SubsystemType.
        // Support both PascalCase (FDP serializer output) and camelCase (HrotSerializerOptions output).
        using var doc = System.Text.Json.JsonDocument.Parse(jsonText);

        System.Text.Json.JsonElement header;
        if (!doc.RootElement.TryGetProperty("Header", out header) &&
            !doc.RootElement.TryGetProperty("header", out header))
            throw new InvalidOperationException(
                "[ScenarioFileService] File is missing the 'Header' section.");

        System.Text.Json.JsonElement typeElem;
        if (!header.TryGetProperty("SubsystemType", out typeElem) &&
            !header.TryGetProperty("subsystemType", out typeElem))
            throw new InvalidOperationException(
                "[ScenarioFileService] Header is missing 'SubsystemType'.");

        var subsystemType = typeElem.GetString() ?? string.Empty;
        if (Array.IndexOf(AcceptedSubsystemTypes, subsystemType) < 0)
            throw new InvalidOperationException(
                $"[ScenarioFileService] Unrecognized SubsystemType '{subsystemType}'. " +
                $"Accepted: {string.Join(", ", AcceptedSubsystemTypes)}.");
    }
}
