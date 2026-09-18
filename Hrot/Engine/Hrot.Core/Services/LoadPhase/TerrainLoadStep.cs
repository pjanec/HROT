using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐ <c>L3</c> — the terrain step. ⭐ <b>Role-derived</b>: only <c>MuscleGround</c> and
/// <c>NavigationSolver</c> require it, because they are the only roles with a measured consumer of the
/// road graph. 🔒 <i>"load nothing where nothing reads it"</i> (user, <c>2026-09-18</c>).
///
/// <para>⭐ The step is glue. The work — and the idempotency branch, and the unload seam — lives in
/// <see cref="TerrainResidency"/>, so the future operator-driven preload calls the same code without a new
/// cluster operation. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.1a, <c>L3</c>/<c>L5</c>.</para>
///
/// <para>⭐⭐ The terrain NAME comes from the load MESSAGE, with the staged header as a one-release fallback
/// — see <see cref="KnowledgeBaseLoadStep"/> for the race that made the header alone unsafe.</para>
/// </summary>
public sealed class TerrainLoadStep : ILoadPartProvider
{
    private readonly TerrainResidency _residency;
    private readonly string _stagingRoot;

    // Staged per transaction, so two overlapping rounds cannot clobber each other.
    private readonly Dictionary<Guid, TerrainResidency.Staged> _staged = new();
    private readonly object _stagedGate = new();

    public TerrainLoadStep(TerrainResidency residency, string localStagingRoot)
    {
        _residency   = residency ?? throw new ArgumentNullException(nameof(residency));
        _stagingRoot = localStagingRoot ?? throw new ArgumentNullException(nameof(localStagingRoot));
    }

    /// <inheritdoc/>
    public LoadPart Part => LoadPart.Terrain;

    /// <inheritdoc/>
    public async Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
    {
        string? requested = !string.IsNullOrWhiteSpace(context.TerrainName)
            ? context.TerrainName
            : ScenarioTerrainName.Read(_stagingRoot);

        // ⭐⭐ L6 — the staging copy may still be delivering the definition: the content step is dispatched
        //    milliseconds after the copy STARTS. ⛔ Without the wait, the loud "definition not found" below
        //    would be correct in form and spurious in fact. ⚠ Bounded, and not a substitute for ordering.
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string definitionPath = System.IO.Path.Combine(
                _stagingRoot, "Terrain", $"{requested}.json");
            if (!System.IO.File.Exists(definitionPath))
                await StagedArtifactWait.ForFileAsync(definitionPath, ct).ConfigureAwait(false);
        }

        var staged = _residency.Prepare(requested);
        if (staged.HasWork)
            lock (_stagedGate) _staged[context.TransactionId] = staged;
    }

    /// <inheritdoc/>
    public void Commit(LoadPhaseContext context, EntityRepository? world)
        => _residency.Commit(world, Take(context.TransactionId));

    /// <inheritdoc/>
    public void Abort(LoadPhaseContext context)
        => _residency.AbortStaged(Take(context.TransactionId));

    /// <inheritdoc/>
    /// <remarks>⭐ Terrain finishes inside <see cref="Commit"/>; there is nothing that drains over frames.</remarks>
    public bool IsResolved(EntityRepository? world) => true;

    private TerrainResidency.Staged? Take(Guid transactionId)
    {
        lock (_stagedGate)
        {
            if (!_staged.TryGetValue(transactionId, out var staged)) return null;
            _staged.Remove(transactionId);
            return staged;
        }
    }
}
