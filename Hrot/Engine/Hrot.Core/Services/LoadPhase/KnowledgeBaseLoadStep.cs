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
/// <para>⛔⛔ <b><c>R-4018</c> — the name comes from the FILE SYNC, never from the scenario or the load command.</b>
/// There is one TKB per deployment; the orchestrator syncs it from its NAS master copy before every scenario load and
/// leaves a <see cref="StagedTkb"/> record naming the archive it copied. This step loads that archive — once: an
/// unchanged archive (same name, length, mtime) is a no-op, so in practice the TKB is loaded on the first load and
/// kept. 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §0.</para>
///
/// <para>⭐ <b>No record = no master copy</b> — legal and silent, the default deployment today: the node runs on the
/// built-in catalogue (<see cref="HrotEnvironment.RegisterBuiltInTkb"/>). A file TKB that was resident goes back to
/// the built-in one, so a fresh node and an old one can never hold different TKBs. ⚠ A record whose archive is
/// missing is LOUD — the sync wrote the record after the copy, so that is a broken staging directory.</para>
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
        string? requested = StagedTkb.Read(_localTkbStagingRoot);

        if (string.IsNullOrWhiteSpace(requested))
        {
            if (_lastLoadedTkbName != null)
            {
                // ⭐ The master copy is gone: the file TKB this node holds goes back to the built-in one, so every node
                //   agrees — a node started now would get the built-in catalogue.
                _tkbDb.Clear();
                HrotEnvironment.RegisterBuiltInTkb((TkbDatabase)_tkbDb);
                FdpLog<KnowledgeBaseLoadStep>.Info(
                    "[LoadPhase/KnowledgeBase] TKB '{0}' is no longer staged — back to the built-in catalogue.",
                    _lastLoadedTkbName);
            }
            else if (!_tkbDb.GetAll().Any())
            {
                HrotEnvironment.RegisterBuiltInTkb((TkbDatabase)_tkbDb);
            }

            _lastLoadedTkbName   = null;
            _lastLoadedLength    = -1;
            _tkbDb.ActiveTkbName = null;
            return Task.CompletedTask;
        }

        string localPath = Path.Combine(
            _localTkbStagingRoot, requested + OrchestrationConstants.TkbArtifactExtension);

        // ⭐⭐⭐ L8 — no wait: the load step is dispatched only after every node acknowledged its files, so an archive
        //    missing at this instant is genuinely missing. 📄 docs/DESIGN_Cluster_Load_Phase.md §7.3 ④.
        var info = new FileInfo(localPath);
        DateTime currentFileTime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        long     currentLength   = info.Exists ? info.Length           : -1;

        if (_lastLoadedTkbName == requested
         && _lastLoadedTimestamp == currentFileTime
         && _lastLoadedLength == currentLength)
            return Task.CompletedTask;   // already resident and unchanged — idempotent no-op

        if (!info.Exists)
            throw new FileNotFoundException(
                $"[LoadPhase/KnowledgeBase] TKB archive not found at '{localPath}', although the TKB sync recorded "
              + $"'{requested}' as staged ({StagedTkb.FileName}) — the staging directory is broken.",
                localPath);

        _tkbDb.Clear();
        using var loader = new TkbUnifiedLoader(localPath);
        var deserializer = new TkbDeserializer();
        foreach (var entityFile in loader.EnumerateEntityFiles())
            deserializer.ParseAndRegister(entityFile, _tkbDb);
        // ⭐ CE-3230 (B-8, H-1; DESIGN_Civilians_And_Crowd_Control §7.9) — every person a file names has hands, as the built-in ones do
        Fdp.Toolkit.Combat.PersonHands.EnsureOnPersons(_tkbDb);

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
}
