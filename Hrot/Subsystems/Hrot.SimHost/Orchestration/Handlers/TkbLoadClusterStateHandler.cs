using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Orchestration;
using Fdp.Interfaces;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Vfs;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.SimHost.Orchestration.Handlers;

/// <summary>
/// Cluster state handler that intercepts <see cref="NodeOpType.PrepareLive"/> and
/// <see cref="NodeOpType.PrepareEdit"/> to load the correct TKB artifact from the
/// node's local staging area before the scenario is deserialized.
///
/// <para>
/// Uses a differential cache keyed on (TkbName, ZIP file timestamp) to avoid
/// unnecessary <see cref="ITkbDatabase.Clear"/> and re-ingestion when the same TKB
/// is loaded for consecutive transitions.
/// </para>
/// <para>
/// If the locally staged scenario header contains no <c>TkbName</c>, the handler
/// falls back to <see cref="NedTkbCatalog.RegisterAll"/> (called only when the
/// database is empty, to preserve any catalog already loaded by a previous
/// successful load).
/// </para>
/// </summary>
public sealed class TkbLoadClusterStateHandler : IClusterStateHandler
{
    private readonly ITkbDatabase _tkbDb;
    private readonly string _localTkbStagingRoot;

    private string? _lastLoadedTkbName;
    private DateTime _lastLoadedTimestamp;

    /// <param name="tkbDb">
    /// The live TKB database shared with <c>NetworkSpawningSystem</c>,
    /// <c>BlueprintApplicationSystem</c>, and <c>GhostPromotionSystem</c>.
    /// </param>
    /// <param name="localStagingRoot">
    /// Root of the node's local staging area (e.g. <c>C:\FDP_Temp</c>).
    /// TKB artifacts are expected under <c>{localStagingRoot}/TKB/</c>.
    /// </param>
    public TkbLoadClusterStateHandler(ITkbDatabase tkbDb, string localStagingRoot)
    {
        _tkbDb = tkbDb ?? throw new ArgumentNullException(nameof(tkbDb));
        _localTkbStagingRoot = Path.Combine(localStagingRoot, "TKB");
    }

    /// <inheritdoc/>
    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareLive || operation == NodeOpType.PrepareEdit;

    /// <inheritdoc/>
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        // Read TkbName from the node's own locally staged scenario header file.
        string? requestedTkb = ExtractTkbNameFromLocalScenario(_localTkbStagingRoot);

        if (string.IsNullOrWhiteSpace(requestedTkb))
        {
            // No TkbName in local scenario -> use hardcoded fallback catalog.
            // NedTkbCatalog.RegisterAll() is called only if the db is empty to avoid
            // overwriting a previously loaded TKB catalog.
            if (!_tkbDb.GetAll().Any())
                NedTkbCatalog.RegisterAll((TkbDatabase)_tkbDb);
            return Task.FromResult<object?>(null);
        }

        string localPath = Path.Combine(_localTkbStagingRoot, $"{requestedTkb}.zip");

        // Differential cache check using file modification time.
        DateTime currentFileTime = File.Exists(localPath)
            ? File.GetLastWriteTimeUtc(localPath)
            : DateTime.MinValue;

        if (_lastLoadedTkbName == requestedTkb && _lastLoadedTimestamp == currentFileTime)
            return Task.FromResult<object?>(null); // Cache hit -- no reload needed.

        if (!File.Exists(localPath))
            throw new FileNotFoundException(
                $"[TkbLoad] TKB artifact not found at '{localPath}'. " +
                "Ensure the TKB file is staged before transitioning to Live/Edit.",
                localPath);

        _tkbDb.Clear();
        using var loader = new TkbUnifiedLoader(localPath);
        var deserializer = new TkbDeserializer();
        foreach (var entityFile in loader.EnumerateEntityFiles())
            deserializer.ParseAndRegister(entityFile, _tkbDb);

        _lastLoadedTkbName = requestedTkb;
        _lastLoadedTimestamp = currentFileTime;
        _tkbDb.ActiveTkbName = requestedTkb;

        FdpLog<TkbLoadClusterStateHandler>.Info(
            "[TkbLoad] Loaded TKB '{0}' ({1} entities).",
            requestedTkb, _tkbDb.GetAll().Count());

        return Task.FromResult<object?>(null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No-op: TKB load is fully committed during <see cref="PrepareAsync"/>.
    /// </remarks>
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <inheritdoc/>
    /// <remarks>
    /// No-op: TKB survives <c>Idle</c> state and is cached across transitions.
    /// A rollback would invalidate the differential cache unnecessarily.
    /// </remarks>
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <summary>
    /// Peeks the <c>TkbName</c> from the node's locally staged scenario header file
    /// using a forward-only <see cref="Utf8JsonReader"/> -- no DOM allocation.
    /// Returns <c>null</c> when the file is absent or does not contain a
    /// <c>TkbName</c> string property.
    /// </summary>
    private static string? ExtractTkbNameFromLocalScenario(string localStagingRoot)
    {
        string headerPath = Path.Combine(localStagingRoot, "ScenarioHeader.json");
        if (!File.Exists(headerPath)) return null;
        var bytes = File.ReadAllBytes(headerPath);
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.ValueTextEquals("TkbName"))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : null;
            }
        }
        return null;
    }
}
