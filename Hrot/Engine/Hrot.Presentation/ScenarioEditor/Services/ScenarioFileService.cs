using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Fdp.Interfaces;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;
using Hrot.Common.Events;

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
    private readonly ITkbDatabase? _tkbDb;
    private readonly MigrationServices? _migrationServices;
    private Action? _worldResetObservers;

    private MigrationLoadResult? _lastLoadResult;
    private string? _lastLoadPath;

    /// <summary>
    /// The <see cref="MigrationLoadResult"/> from the most recent
    /// <see cref="LoadScenario"/> call that went through the persistent adapter,
    /// or <c>null</c> if no migration-aware load has occurred.
    /// </summary>
    public MigrationLoadResult? LastLoadResult => _lastLoadResult;

    public ScenarioFileService(
        ScenarioSerializer serializer,
        FdpEventBus? bus = null,
        IZoneManagerService? zoneService = null,
        ITkbDatabase? tkbDb = null,
        MigrationServices? migrationServices = null)
    {
        _serializer        = serializer  ?? throw new ArgumentNullException(nameof(serializer));
        _bus               = bus;
        _zoneService       = zoneService;
        _tkbDb             = tkbDb;
        _migrationServices = migrationServices;
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
        _bus?.Publish(new WorldResetEvent()); // bus event survives because it's after ClearAll
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

        var header  = new ScenarioHeader("Hrot.Scenario", TkbName: _tkbDb?.ActiveTkbName);
        var fdpDom  = _serializer.Serialize(repo, header); // stamps $meta

        var activeZones = _zoneService?.GetActiveZones();
        if (activeZones != null && activeZones.Count > 0)
            fdpDom["Zones"] = System.Text.Json.JsonSerializer
                .SerializeToNode(activeZones, HrotSerializerOptions.HrotJsonOptions)!;

        if (_migrationServices != null
            && _lastLoadResult != null
            && string.Equals(filePath, _lastLoadPath, StringComparison.OrdinalIgnoreCase))
        {
            // Use the persistent adapter so that any round-trip journal is applied
            // (restoring higher-version-only fields) and cleaned up on success.
            _migrationServices.Persistent
                .SaveAsync(filePath, fdpDom, _lastLoadResult)
                .GetAwaiter().GetResult();
            _lastLoadResult = null;  // consumed; next load will refresh
            _lastLoadPath   = null;
        }
        else
        {
            // Direct write path for fresh saves or saves without a prior load result.
            var minifiedOptions = new System.Text.Json.JsonSerializerOptions(HrotSerializerOptions.HrotJsonOptions)
            {
                WriteIndented = false,
            };
            var minifiedJson = System.Text.Json.JsonSerializer.Serialize(fdpDom, minifiedOptions);
            File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(minifiedJson));
        }
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

        JsonObject? dom = null;

        if (_migrationServices != null)
        {
            // Phase 4 path: use the persistent adapter so that snapshots are written
            // before up-migration and journals are written on down-migration.
            // MigrationLoadResult carries WasMigrated / IsDegraded for UI alerts.
            var result = _migrationServices.Persistent
                .LoadAndMigrateAsync(filePath)
                .GetAwaiter().GetResult();
            _lastLoadResult = result;
            _lastLoadPath   = filePath;
            dom = result.Dom;
        }
        else
        {
            _lastLoadResult = null;
            _lastLoadPath   = null;
            // Legacy path: validate subsystem type header before destructively clearing.
            ValidateSubsystemType(jsonText);
        }

        _worldResetObservers?.Invoke();   // synchronous callbacks BEFORE clear
        repo.SoftClear();                 // also clears Bus — so we publish bus event AFTER this
        // Reset simulation time: SoftClear() does not touch singletons.
        if (repo.HasSingletonUnmanaged<GlobalTime>())
            repo.SetSingletonUnmanaged(default(GlobalTime));
        _bus?.Publish(new WorldResetEvent()); // bus event survives because it's after ClearAll

        if (_zoneService != null)
        {
            // Single JSON parse discipline: parse once, use DOM for both DTOs and FDP serializer.
            dom ??= JsonNode.Parse(jsonText)?.AsObject();
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
            if (dom != null)
                _serializer.Deserialize(repo, dom);
            else
                _serializer.Deserialize(repo, jsonText);
        }
    }

    /// <summary>
    /// Returns sidecar files (snapshots and journals) for the most recently
    /// loaded file. Returns an empty list when no migration-aware load has
    /// occurred or when no migration services are configured.
    /// </summary>
    public async Task<IReadOnlyList<SidecarFileInfo>> GetSidecarsForLastLoadAsync(
        CancellationToken ct = default)
    {
        if (_migrationServices == null || _lastLoadPath == null)
            return Array.Empty<SidecarFileInfo>();
        return await _migrationServices.Persistent
            .ListSidecarsAsync(_lastLoadPath, ct)
            .ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void FireWorldReset()
    {
        _worldResetObservers?.Invoke();
        _bus?.Publish(new WorldResetEvent());
    }

    private static void ValidateSubsystemType(string jsonText)
    {
        // Quick header peek: check $meta.docType (Phase 2) or Header.SubsystemType (legacy).
        // Support both PascalCase (FDP serializer output) and camelCase (HrotSerializerOptions output).
        using var doc = System.Text.Json.JsonDocument.Parse(jsonText);

        // Phase 2: $meta.docType
        if (doc.RootElement.TryGetProperty("$meta", out var metaElem))
        {
            System.Text.Json.JsonElement docTypeElem;
            if (!metaElem.TryGetProperty("docType", out docTypeElem))
                throw new InvalidOperationException(
                    "[ScenarioFileService] File '$meta' is missing 'docType'.");

            var subsystemType = docTypeElem.GetString() ?? string.Empty;
            if (Array.IndexOf(AcceptedSubsystemTypes, subsystemType) < 0)
                throw new InvalidOperationException(
                    $"[ScenarioFileService] Unrecognized docType '{subsystemType}'. " +
                    $"Accepted: {string.Join(", ", AcceptedSubsystemTypes)}.");
            return;
        }

        // Legacy: Header.SubsystemType
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

        var legacySubsystemType = typeElem.GetString() ?? string.Empty;
        if (Array.IndexOf(AcceptedSubsystemTypes, legacySubsystemType) < 0)
            throw new InvalidOperationException(
                $"[ScenarioFileService] Unrecognized SubsystemType '{legacySubsystemType}'. " +
                $"Accepted: {string.Join(", ", AcceptedSubsystemTypes)}.");
    }

}
