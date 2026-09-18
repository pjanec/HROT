using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Vfs;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐⭐ <c>L3</c> — the knowledge-base (TKB) step. ⭐ Required on <b>every ECS node</b>, because every
/// ECS node composes the full genesis pipeline and a node that can be asked to create an entity must be
/// able to resolve its template (<c>Q65-A′</c>). 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.1.
///
/// <para>🔴 <b>Before the chain, only SimHost had one.</b> CGF and IG built a catalogue locally and would
/// have ignored a scenario's <c>TkbName</c> entirely — an entity created there from a scenario-supplied
/// template would have resolved against the wrong knowledge base, silently.</para>
///
/// <para>⭐⭐ <b>The name now comes from the load MESSAGE</b>, with the staged header kept as a fallback for
/// one release (an older orchestrator that does not send it). ⛔ The header alone was a channel that RACED
/// this step: measured, the step was dispatched 6 ms after the file copy started and 49 ms before the
/// files were fanned out, and this loader does not retry — so it silently concluded <i>"this scenario
/// names no TKB"</i>, which is indistinguishable from the truth.</para>
///
/// <para>⚠ <b>Absent NAME versus absent ARTIFACT.</b> No name anywhere is legal and silent: the hard-coded
/// catalogue is registered when the database is empty, which is the supported path while no scenario ships
/// a TKB. A name whose artifact is missing is LOUD — that is a broken deployment, not a TKB-less scenario.</para>
/// </summary>
public sealed class KnowledgeBaseLoadStep : ILoadPartProvider
{
    private readonly ITkbDatabase _tkbDb;
    private readonly string _localTkbStagingRoot;

    // ⭐ The differential cache key is (name, LENGTH, mtime) — literally the same predicate the
    //   orchestrator's IsAlreadyCurrent evaluates, which is what lets the two skips COMPOSE: the
    //   orchestrator skipping a copy leaves this file untouched, which is exactly what makes this fire.
    private string?  _lastLoadedTkbName;
    private DateTime _lastLoadedTimestamp;
    private long     _lastLoadedLength = -1;   // -1 = nothing loaded yet, distinct from a zero-length file

    /// <param name="tkbDb">The live database shared with the spawning and blueprint systems.</param>
    /// <param name="localStagingRoot">
    /// ⭐ <b>THIS NODE'S OWN staging root</b> — <c>{base}/nodes/node-N</c>, which every host bootstrap
    /// already computes. Artifacts are read from <c>TKB/</c> beneath it.
    /// </param>
    public KnowledgeBaseLoadStep(ITkbDatabase tkbDb, string localStagingRoot)
    {
        _tkbDb = tkbDb ?? throw new ArgumentNullException(nameof(tkbDb));
        _localTkbStagingRoot = OrchestrationConstants.GetTkbStagingRoot(localStagingRoot);
    }

    /// <inheritdoc/>
    public LoadPart Part => LoadPart.KnowledgeBase;

    /// <inheritdoc/>
    public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
    {
        // ⭐⭐ The message first, the staged header only as a fallback.
        string? requested = !string.IsNullOrWhiteSpace(context.TkbName)
            ? context.TkbName
            : PeekStagedHeader("TkbName");

        if (string.IsNullOrWhiteSpace(requested))
        {
            // ⭐ Legal and silent. The hard-coded catalogue is registered only when the database is empty,
            //   so a TKB loaded by a previous round is not thrown away.
            if (!_tkbDb.GetAll().Any())
                NedTkbCatalog.RegisterAll((TkbDatabase)_tkbDb);

            _lastLoadedTkbName = null;
            _lastLoadedLength  = -1;
            _tkbDb.ActiveTkbName = null;
            return Task.CompletedTask;
        }

        string localPath = Path.Combine(
            _localTkbStagingRoot, requested + OrchestrationConstants.TkbArtifactExtension);

        var info = new FileInfo(localPath);
        DateTime currentFileTime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        long     currentLength   = info.Exists ? info.Length           : -1;

        if (_lastLoadedTkbName == requested
         && _lastLoadedTimestamp == currentFileTime
         && _lastLoadedLength == currentLength)
            return Task.CompletedTask;   // already resident and unchanged — idempotent no-op

        if (!info.Exists)
            throw new FileNotFoundException(
                $"[LoadPhase/KnowledgeBase] TKB artifact not found at '{localPath}'. The scenario names "
              + $"TKB '{requested}', so this is a broken deployment rather than a TKB-less scenario — "
              + "ensure the artifact is staged before the load transition.",
                localPath);

        _tkbDb.Clear();
        using var loader = new TkbUnifiedLoader(localPath);
        var deserializer = new TkbDeserializer();
        foreach (var entityFile in loader.EnumerateEntityFiles())
            deserializer.ParseAndRegister(entityFile, _tkbDb);

        _lastLoadedTkbName   = requested;
        _lastLoadedTimestamp = currentFileTime;
        _lastLoadedLength    = currentLength;
        _tkbDb.ActiveTkbName = requested;

        FdpLog<KnowledgeBaseLoadStep>.Info(
            "[LoadPhase/KnowledgeBase] Loaded TKB '{0}' ({1} entities).",
            requested, _tkbDb.GetAll().Count());

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>No-op: the database is not ECS, so the whole load completes in <c>PrepareAsync</c>.</remarks>
    public void Commit(LoadPhaseContext context, EntityRepository? world) { }

    /// <inheritdoc/>
    /// <remarks>
    /// No-op. ⚠ The database survives <c>Idle</c> and is cached across transitions on purpose; rolling it
    /// back would invalidate the differential cache for a round that may simply be retried.
    /// </remarks>
    public void Abort(LoadPhaseContext context) { }

    /// <summary>
    /// ⚠ Fallback only — the staged sidecar header the orchestrator writes during the file copy. ⛔ It
    /// races this step (see the class remarks), which is why the message is preferred.
    /// </summary>
    private string? PeekStagedHeader(string pascalName)
    {
        string headerPath = Path.Combine(_localTkbStagingRoot, "ScenarioHeader.json");
        if (!File.Exists(headerPath)) return null;

        try
        {
            var reader = new Utf8JsonReader(File.ReadAllBytes(headerPath));
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.ValueTextEquals(pascalName))
                {
                    reader.Read();
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
            }
        }
        catch (Exception ex)
        {
            FdpLog<KnowledgeBaseLoadStep>.Error(
                "[LoadPhase/KnowledgeBase] Could not read the staged header at '{0}' ({1}).",
                headerPath, ex.Message);
        }
        return null;
    }
}
