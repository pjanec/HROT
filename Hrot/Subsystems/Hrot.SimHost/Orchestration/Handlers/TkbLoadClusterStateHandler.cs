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

    /// <summary>
    /// ⭐⭐⭐ <c>S3a</c> — the third component of the differential cache key.
    ///
    /// <para>🔒 The key is <c>(name, length, lastWriteTimeUtc)</c>, ruled by the user <c>2026-09-18</c> —
    /// ⛔ no hash, no sidecar. ⭐⭐ <b>Adding length is what makes this side evaluate LITERALLY the same
    /// predicate as the orchestrator's <c>IsAlreadyCurrent</c>.</b> 📐 That is what lets the two skips
    /// COMPOSE rather than merely coexist: the orchestrator skipping a copy leaves this file untouched,
    /// which is precisely the condition that makes this cache fire.</para>
    ///
    /// <para>⚠ <c>-1</c> means "nothing loaded yet", distinct from a real zero-length file.</para>
    /// </summary>
    private long _lastLoadedLength = -1;

    /// <param name="tkbDb">
    /// The live TKB database shared with <c>NetworkSpawningSystem</c>,
    /// <c>BlueprintApplicationSystem</c>, and <c>GhostPromotionSystem</c>.
    /// </param>
    /// <param name="localStagingRoot">
    /// ⭐⭐ <b>THIS NODE'S OWN staging root</b> — i.e. <c>{base}/nodes/node-N</c>, which is what every
    /// host bootstrap already computes and passes (<c>SimHostApp.cs:362</c>, <c>CgfSubsystem.cs:612</c>,
    /// <c>OrchestratorSubsystem.cs:137</c>). TKB artifacts are read from
    /// <see cref="OrchestrationConstants.GetTkbStagingRoot"/> beneath it.
    ///
    /// <para>⚠ <b>The old doc comment said <c>"e.g. C:\FDP_Temp"</c> — the BARE base root — and that was
    /// misleading:</b> no production caller passes the bare root, and if one did, this handler would
    /// read a directory the orchestrator never writes. 📐 <c>V1</c>, measured <c>2026-09-18</c>.</para>
    /// </param>
    public TkbLoadClusterStateHandler(ITkbDatabase tkbDb, string localStagingRoot)
    {
        _tkbDb = tkbDb ?? throw new ArgumentNullException(nameof(tkbDb));
        // ⭐ S1a — the ONE place the directory name comes from, shared with the orchestrator's writer.
        _localTkbStagingRoot = OrchestrationConstants.GetTkbStagingRoot(localStagingRoot);
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
            _lastLoadedTkbName = null;
            _lastLoadedLength = -1;
            _tkbDb.ActiveTkbName = null;
            return Task.FromResult<object?>(null);
        }

        string localPath = Path.Combine(
            _localTkbStagingRoot, requestedTkb + OrchestrationConstants.TkbArtifactExtension);

        // ⭐⭐ S3a — the differential cache key is (name, LENGTH, mtime). 📐 The same predicate the
        //    orchestrator's IsAlreadyCurrent evaluates, which is what makes the two skips compose.
        var info = new FileInfo(localPath);
        DateTime currentFileTime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        long     currentLength   = info.Exists ? info.Length           : -1;

        if (_lastLoadedTkbName == requestedTkb
         && _lastLoadedTimestamp == currentFileTime
         && _lastLoadedLength == currentLength)
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

        // ⭐ CE-259az is closed by DERIVATION, not by a convention applied here (2026-09-13):
        //   TkbTemplate.BirthCriticalComponents is now a read-only view of [BirthCritical] on the
        //   component type, so a file-loaded template carries it BY CONSTRUCTION — and so does every
        //   programmatic catalogue, which an app-layer convention here could never reach.
        //   📄 docs/designs/tkb-1/DESIGN.md §6.6a.

        _lastLoadedTkbName = requestedTkb;
        _lastLoadedTimestamp = currentFileTime;
        _lastLoadedLength = currentLength;
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
