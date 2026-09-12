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
/// Provides local scenario file operations: <see cref="NewScenario"/> and <see cref="SaveScenario"/>.
///
/// <para><see cref="NewScenario"/> publishes a <see cref="WorldResetEvent"/> synchronously BEFORE calling
/// <c>repo.SoftClear()</c> so consumers (selection managers, active tools, the network id map) can flush
/// cached <see cref="Entity"/> handles before the repository is wiped.</para>
///
/// <para>⚠ It used to carry a third operation, <c>LoadScenario</c> — a direct file→repo load that bypassed
/// the genesis pipeline. ⛔ Removed `2026-08-24` (HN-037 Part B); see the note where it stood.</para>
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
    /// Comparison to use for filesystem path equality: case-insensitive on Windows
    /// (NTFS default), case-sensitive (Ordinal) elsewhere, since case-differing paths
    /// are distinct files on Linux.
    /// </summary>
    private static StringComparison PlatformPathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The <see cref="MigrationLoadResult"/> from the most recent migration-aware load, or <c>null</c>.
    /// <para>⚠⚠ <b>Always <c>null</c> since HN-037 Part B, and it was already inert before it.</b> Only the
    /// removed <c>LoadScenario</c> ever set it — and 📐 measured, production builds this service with
    /// <c>migrationServices: null</c> (<c>EditorSubsystem</c>), so the persistent adapter never ran in the
    /// editor anyway. ⇒ <see cref="SaveScenario"/>'s round-trip-journal branch is unreachable in production
    /// and was before this change. ⛔ Left standing rather than cascaded into a deletion: re-wiring the
    /// migration adapter to the genesis load path is a design question, not a mechanical consequence of
    /// removing a facade. Filed as an open finding.</para>
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
    /// <c>repo.SoftClear()</c> in <see cref="NewScenario"/>. Use this to flush cached entity handles.
    /// <para>⭐ <c>HN-037</c>: the editor registers the <c>NetworkEntityMap</c> clear here — that map IS
    /// cached entity handles, and nothing else clears it at a world boundary.</para>
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
            && string.Equals(filePath, _lastLoadPath, PlatformPathComparison))
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

    // ⛔⛔ `LoadScenario(EntityRepository, string)` was REMOVED `2026-08-24` (HN-037 Part B).
    //    📐 Measured: zero production callers but the IEditorLogic facade, which went with it.
    //    ⛔ Why it was a trap, not merely unused: it bypassed the EntityLifecycleModule handshake (no
    //    Constructing phase, no AuthorityMask), left the transient genesis Intents
    //    (InitialVehicleIntent/InitialPassengersIntent/...) dangling because GenesisMaterializationSystem
    //    never ran, and never synced the id allocator — so a later spawn could collide. ⭐ It is exactly the
    //    class of bug the allocator unification exists to prevent.
    //    ⭐ Nothing is lost: LoadScenarioByName IS the editor's load (the genesis pipeline through
    //    HrotEditLoadHandler), and a raw deserialize belongs to ScenarioSerializer.Deserialize.
    //    ⚠ ValidateSubsystemType survives below as a PUBLIC static — the header check was the one capability
    //    that lived only here in throwing form, and routing it beats losing it.

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

    /// <summary>
    /// ⭐ Throws unless the file's <c>$meta.docType</c> (or legacy <c>Header.SubsystemType</c>) is one this
    /// application accepts.
    /// <para>⭐⭐ Public since <c>HN-037</c> Part B: it used to be a private step of <c>LoadScenario</c>, and
    /// keeping it is what makes that removal lossless. ⚠ The genesis load path answers the same question in
    /// a different SHAPE — <c>HrotScenarioLoader</c> SKIPS a non-matching file rather than throwing — so this
    /// remains the only throwing form, which is why it was routed rather than deleted.</para>
    /// </summary>
    public static void ValidateSubsystemType(string jsonText)
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
