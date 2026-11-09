using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// ⭐ G-7 / CE-4354 S5 (docs/UX/DESIGN_Review_Perspective.md §6.4 I6, §6.12 S5) — <b>the federated search</b>: the ONE <see cref="IRecordingSearchService"/> runs per participant FILE; a hit is kept only for a
    /// component that node <b>owned at that frame</b> (<see cref="OwnershipSource.OwnedComponents"/> — the merge's rule, I8; a replica's value is what that node BELIEVED, not the truth) and the node's hits
    /// are UNIONED by network id: the same unit at the same moment of the primary node's timeline with the same context is ONE result, whichever nodes owned the components that matched.
    /// <para>A hit carries its <see cref="SearchResultDto.NodeId"/> and <see cref="SearchResultDto.NetworkId"/>; its <c>FrameIndex</c> is a frame of the PRIMARY node's recording (the host's seek unit — each
    /// node's wall stamp is shifted by its offset, as the shown merge shifts it). A search with no component to attribute (an event or lifecycle search) keeps every hit; a hit on an entity with no
    /// network id (a node's LOCAL entity) is kept for the node it came from.</para>
    /// </summary>
    public sealed class FederatedSearch
    {
        private readonly IRecordingSearchService _inner;
        private readonly IPredicateCompiler _compiler;
        private readonly OwnershipSource _ownership;

        public FederatedSearch(IRecordingSearchService inner, IPredicateCompiler compiler, OwnershipSource ownership)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        }

        /// <param name="parts">The participant files (node, path).</param>
        /// <param name="offsetTicks">Each node's manual wall-stamp offset (the shown merge's); a missing node is 0.</param>
        /// <param name="primaryNode">The node whose recording the host seeks (its frame index is a result's <c>FrameIndex</c>).</param>
        public IReadOnlyList<SearchResultDto> Execute(IReadOnlyList<(int Node, string Path)> parts, IReadOnlyDictionary<int, long> offsetTicks, int primaryNode,
                                                       SearchPredicateDto root, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (root == null) throw new ArgumentNullException(nameof(root));
            long Off(int node) => offsetTicks != null && offsetTicks.TryGetValue(node, out long v) ? v : 0L;

            var mandatory = _compiler.ExtractMandatoryComponents(root);
            var typeIds = new List<int>();
            for (int i = 0; i < mandatory.Count; i++) { int id = ComponentTypeRegistry.GetId(mandatory[i]); if (id >= 0) typeIds.Add(id); }

            string? primaryPath = null;
            foreach (var p in parts) if (p.Node == primaryNode) primaryPath = p.Path;
            primaryPath ??= parts.Count > 0 ? parts[0].Path : null;
            using var primary = new ReplayBrowserContext();
            if (primaryPath != null) primary.LoadRecording(primaryPath);

            var kept = new Dictionary<(long net, int frame, string context), SearchResultDto>();
            var noId = new List<SearchResultDto>();
            foreach (var (node, path) in parts)
            {
                if (ct.IsCancellationRequested) break;
                var hits = new List<SearchResultDto>(_inner.ExecuteSearch(path, root, entityFilter, ct));
                if (hits.Count == 0) continue;
                hits.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));

                using var reader = new ReplayBrowserContext();   // a throwaway reader over the node's file: its repository at each hit's frame answers who owned what
                reader.LoadRecording(path);
                int idType = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
                foreach (var h in hits)
                {
                    if (ct.IsCancellationRequested) break;
                    long net = 0;
                    if (!h.Entity.IsNull && reader.Playback != null)
                    {
                        if (reader.CurrentFrame != h.FrameIndex) reader.SeekToFrame(h.FrameIndex, suppressHistory: true);
                        var repo = reader.SandboxRepo;
                        if (repo.IsAlive(h.Entity) && idType >= 0 && repo.GetComponentMask(h.Entity.Index).IsSet(idType))
                        {
                            net = repo.GetComponent<NetworkIdentity>(h.Entity).Value;
                            // ⭐ I6 — only a component THIS node owned at that frame counts (a networked entity); no component to attribute ⇒ the hit stands
                            if (typeIds.Count > 0)
                            {
                                var owned = _ownership.OwnedComponents(repo, h.Entity, node);
                                bool any = false;
                                for (int k = 0; k < typeIds.Count && !any; k++) any = owned.IsSet(typeIds[k]);
                                if (!any) continue;
                            }
                        }
                    }

                    long wall = h.WallClockTicks - Off(node) + Off(primaryNode);
                    int frame = PrimaryFrameAt(primary, wall, h.FrameIndex, node == primaryNode);
                    var hit = h with { FrameIndex = frame, WallClockTicks = wall, NodeId = node, NetworkId = net };
                    if (net == 0) noId.Add(hit);                                    // a hit with no id is its node's own — never merged with another's
                    else kept.TryAdd((net, frame, h.ContextMessage), hit);          // ⭐ the union by network id
                }
            }

            var results = new List<SearchResultDto>(kept.Values);
            results.AddRange(noId);
            results.Sort((a, b) => a.FrameIndex != b.FrameIndex ? a.FrameIndex.CompareTo(b.FrameIndex) : a.NetworkId.CompareTo(b.NetworkId));
            return results;
        }

        /// <summary>The frame of the primary node's recording at or just before <paramref name="wallTicks"/> (a hit of the primary file keeps its own frame).</summary>
        private static int PrimaryFrameAt(ReplayBrowserContext primary, long wallTicks, int ownFrame, bool isPrimary)
        {
            if (isPrimary || primary.Playback is not { TotalFrames: > 0 } pb) return ownFrame;
            int lo = 0, hi = pb.TotalFrames - 1, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (pb.GetFrameMetadata(mid).WallClockTicks <= wallTicks) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }
    }
}
