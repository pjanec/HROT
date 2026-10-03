using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Assets;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>Where an authoring node's tree of one kind stands against NAS (design §5).</summary>
public enum AssetStaleness
{
    InSync = 0,
    /// <summary>The author has changes NAS does not — WARN "unpublished changes", the load continues.</summary>
    Ahead = 1,
    /// <summary>NAS has changes the author does not — WARN "refresh available", the load continues, ⛔ nothing transfers.</summary>
    Behind = 2,
}

/// <summary>One probe result: a node, a kind, and how its tree compares with NAS.</summary>
public sealed record AssetProbeFinding(int NodeId, string Kind, AssetStaleness State, AssetTreeSummary Node, AssetTreeSummary Nas);

/// <summary>
/// ⭐⭐ <b>Increment C — the explicit node↔NAS operations and the staleness probe</b> (docs/DESIGN_Asset_Management.md §5,
/// §7.3c, §7.5; tasks <c>C1</c>–<c>C5</c>). The NAS→node sync on a load is <c>B</c> (inside the prefetch saga); THIS is
/// what a user does, any time, and the cheap check a load runs.
/// <list type="bullet">
///   <item><b>Publish</b> (<c>C1</c>) — an author's tree → <c>{nas}/assets/&lt;kind&gt;</c>, through the existing
///     <see cref="StorageGatewayModule.PullToNasAsync"/>; only files that are new or NEWER on the author move (§10 D6).</item>
///   <item><b>Refresh</b> (<c>C5</c>) — NAS → the author's own folder: <see cref="PreviewRefresh"/> NAMES the files it will
///     replace, <see cref="RefreshFromNasAsync"/> applies the same plan. ⛔ The only route an UPDATE reaches an author.</item>
///   <item><b>Probe</b> (<c>C2</c>/<c>C3</c>) — a stat-only summary per authoring node and kind, both directions; it
///     WARNS and never transfers. An offline author is not in the active roster, so it is not probed — railed.</item>
/// </list>
/// ⛔ Nothing here is automatic: publish and refresh run only when a user asks (<c>Q72-I</c>).
/// </summary>
public sealed class AssetSyncService
{
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;
    private readonly Func<IReadOnlyDictionary<int, string[]>> _activeNodeCapabilities;

    /// <param name="activeNodeCapabilities">The ACTIVE nodes' advertised tokens (ClusterMaster's roster).</param>
    public AssetSyncService(
        StorageGatewayModule gateway, string nasBasePath, Func<IReadOnlyDictionary<int, string[]>> activeNodeCapabilities)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath = nasBasePath ?? throw new ArgumentNullException(nameof(nasBasePath));
        _activeNodeCapabilities = activeNodeCapabilities ?? throw new ArgumentNullException(nameof(activeNodeCapabilities));
    }

    /// <summary>
    /// ⭐ C1 — publish node <paramref name="nodeId"/>'s <paramref name="kind"/> tree to NAS. The node must be active and
    /// AUTHOR the kind (it advertised <c>authors</c> + a root) — publishing from a node that only receives the kind would
    /// copy NAS back onto itself. Only new and newer files move; nothing on NAS is deleted or rolled back.
    /// </summary>
    public async Task<GatewayResult> PublishToNasAsync(string kind, int nodeId)
    {
        var root = AuthorRoot(kind, nodeId);
        var nasRoot = OrchestrationConstants.GetNasAssetRoot(_nasBasePath, kind);
        var plan = AssetTreeSync.PlanUpdateNewer(root, nasRoot);

        var entries = plan.ToWrite.Select(e => new FileManifestEntry
        {
            SourceUnc    = Path.Combine(root, e.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            RelativeDest = Path.Combine(OrchestrationConstants.NasAssetsDirectoryName, kind,
                                        e.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Length       = e.Length,
            LastWriteUtc = e.LastWriteUtc,
        }).ToList();

        var result = await _gateway.PullToNasAsync(entries, _nasBasePath).ConfigureAwait(false);
        FdpLog<AssetSyncService>.Info(
            $"[AssetSync] Publish {kind} from node {nodeId}: {plan.ToAdd.Count} new, {plan.ToReplace.Count} newer, "
          + $"{plan.DestinationNewer.Count} left alone (NAS newer); {result.FailureCount} failed.");
        return result;
    }

    /// <summary>
    /// ⭐ C5 — what a refresh of <paramref name="kind"/> on <paramref name="nodeId"/> would do: the files it ADDS, the files it
    /// REPLACES (NAS newer — <b>these overwrite the author's copy</b>), and the author's files that are newer and stay.
    /// </summary>
    public AssetUpdatePlan PreviewRefresh(string kind, int nodeId)
        => AssetTreeSync.PlanUpdateNewer(OrchestrationConstants.GetNasAssetRoot(_nasBasePath, kind), AuthorRoot(kind, nodeId));

    /// <summary>
    /// ⭐⭐ C5 — refresh the author's own folder from NAS. ⚠ It OVERWRITES, so it logs the files it replaces first (the
    /// same list <see cref="PreviewRefresh"/> returns). A file the author changed more recently is never overwritten.
    /// </summary>
    public Task<AssetSyncResult> RefreshFromNasAsync(string kind, int nodeId)
    {
        var root = AuthorRoot(kind, nodeId);
        var nasRoot = OrchestrationConstants.GetNasAssetRoot(_nasBasePath, kind);
        var plan = PreviewRefresh(kind, nodeId);
        if (plan.ToReplace.Count > 0)
            FdpLog<AssetSyncService>.Warn(
                $"[AssetSync] Refresh {kind} on node {nodeId} REPLACES {plan.ToReplace.Count} file(s): "
              + string.Join(", ", plan.ToReplace.Select(e => e.RelativePath)));
        return Task.Run(() => new AssetTreeSync().Sync(nasRoot, root, AssetSyncMode.UpdateNewer));
    }

    /// <summary>
    /// ⭐ C2 — every ACTIVE authoring node, every kind it authors: a stat-only summary (count, newest mtime) of its tree and
    /// of NAS, compared both ways. Returns only the trees that are NOT in sync.
    /// </summary>
    public IReadOnlyList<AssetProbeFinding> ProbeAuthoringNodes()
        => Probe(_nasBasePath, _activeNodeCapabilities());

    /// <summary>The probe over an explicit roster — also what the prefetch saga runs on a load (C3).</summary>
    public static IReadOnlyList<AssetProbeFinding> Probe(string nasBasePath, IReadOnlyDictionary<int, string[]>? capabilities)
    {
        var findings = new List<AssetProbeFinding>();
        if (capabilities == null || string.IsNullOrWhiteSpace(nasBasePath)) return findings;

        foreach (var (nodeId, tokens) in capabilities)
        {
            var profile = AssetTokens.Parse(tokens);
            foreach (var kind in profile.Authors)
            {
                if (!profile.Roots.TryGetValue(kind, out var root)) continue;
                var node = AssetTreeSummary.Of(AssetManifest.Scan(root));
                var nas = AssetTreeSummary.Of(AssetManifest.Scan(OrchestrationConstants.GetNasAssetRoot(nasBasePath, kind)));
                var state = Compare(node, nas);
                if (state != AssetStaleness.InSync) findings.Add(new AssetProbeFinding(nodeId, kind, state, node, nas));
            }
        }
        return findings;
    }

    /// <summary>The summary comparison: the newer tree is ahead; equal newest times with different counts follow the count.</summary>
    public static AssetStaleness Compare(AssetTreeSummary node, AssetTreeSummary nas)
    {
        if (node.NewestUtc > nas.NewestUtc) return AssetStaleness.Ahead;
        if (nas.NewestUtc > node.NewestUtc) return AssetStaleness.Behind;
        if (node.Count > nas.Count) return AssetStaleness.Ahead;
        if (nas.Count > node.Count) return AssetStaleness.Behind;
        return AssetStaleness.InSync;
    }

    private string AuthorRoot(string kind, int nodeId)
    {
        if (!_activeNodeCapabilities().TryGetValue(nodeId, out var tokens))
            throw new InvalidOperationException($"[AssetSync] node {nodeId} is not active.");
        var profile = AssetTokens.Parse(tokens);
        if (!profile.Authors.Contains(kind) || !profile.Roots.TryGetValue(kind, out var root))
            throw new InvalidOperationException(
                $"[AssetSync] node {nodeId} does not author '{kind}' (it advertised no hrot.asset.authors.{kind} with a root).");
        return root;
    }
}
