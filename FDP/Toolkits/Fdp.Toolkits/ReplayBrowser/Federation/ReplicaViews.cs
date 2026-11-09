using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>One node's copy of a component in <see cref="ReplicaView"/>: <c>Value</c> its leaf values joined by " · ", <c>Differs</c> beyond ε from the OWNER's, <c>Delta</c> the distance (numeric leaves) — 0 for the owner and for a copy that matches.</summary>
    public readonly record struct ReplicaRow(int NodeId, bool IsOwner, string Value, bool Differs, double Delta);

    /// <summary>⭐ G-7 S5 (I5) — what every node holding a replica of one component believes, owner first. <c>OwnerNode</c> is -1 when no node's record / mask supplies it (then no row carries the crown).</summary>
    public sealed record ReplicaView(long NetworkId, Type Component, int OwnerNode, IReadOnlyList<ReplicaRow> Rows)
    {
        /// <summary>Does any replica differ from the owner's value beyond ε?</summary>
        public bool AnyDiffers
        {
            get { for (int i = 0; i < Rows.Count; i++) if (Rows[i].Differs) return true; return false; }
        }
    }

    /// <summary>
    /// ⭐ G-7 / CE-4354 S5 (docs/UX/DESIGN_Review_Perspective.md §6.4 I5, M-4, M-5, §6.12 S5) — <b>the views lens</b>: for one networked entity, each replicated component's value at the OWNER
    /// (<see cref="OwnershipSource"/>'s answer — the merge's ONE rule, I8) and at every node holding a replica, with a replica marked as DIFFERING when it differs from the owner's beyond ε.
    /// ⭐ "Differs" is <see cref="ComponentDiffService.ComputeDiff"/> with ε = <see cref="Epsilon"/> over the scenario serializer's JSON of the component — the ONE differ (M-4), no second tolerance.
    /// A raw <see cref="Entity"/> in a value is shown as the network id it points at, so two nodes' handles compare by what they MEAN.
    /// <para>The Details ▸ component ▸ <i>By node</i> section reads <see cref="Compute"/> (cached by the host while the merged frame is unchanged); the Event Timeline's <i>Replica differs</i> lane is
    /// <see cref="ScanDifferingFrames"/> over the group's files (a background pass — the lane's data).</para>
    /// </summary>
    public sealed class ReplicaViews
    {
        /// <summary>The diff's ε — the existing <c>ComponentDiffService</c> default the frame diff's "ignore tiny" uses (design M-4: ONE tolerance).</summary>
        public const double Epsilon = 0.001;

        private readonly OwnershipSource _ownership;
        private readonly ScenarioSerializer _serializer;
        private readonly IComponentDiffService _diff;

        /// <param name="ownership">The merge's OWN ownership source (the same instance the <see cref="BinaryMerger"/> claims with) — ⛔ never a second one.</param>
        /// <param name="serializer">The host's scenario serializer.</param>
        /// <param name="diff">The ONE differ; default <see cref="ComponentDiffService"/>.</param>
        public ReplicaViews(OwnershipSource ownership, ScenarioSerializer serializer, IComponentDiffService? diff = null)
        {
            _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _diff = diff ?? new ComponentDiffService();
        }

        /// <summary>⭐ A component that differs per node BY CONSTRUCTION is never a "replica differs" finding: <see cref="NetworkAuthority"/> carries each node's OWN id (<c>LocalNodeId</c>). Shown in By node, never amber, never in the lane.</summary>
        public static bool IsPerNode(Type component) => component == typeof(NetworkAuthority);

        /// <summary>A key that moves whenever any node's shown frame does — a caller caches a <see cref="Compute"/> result until it changes (0 B).</summary>
        public static long StateKey(FederatedReplayManager manager)
        {
            long key = manager.NodeCount;
            for (int i = 0; i < manager.NodeCount; i++) key = key * 1_000_003L + manager.ContextAt(i).CurrentFrame + 1;
            return key;
        }

        // ── the entity across nodes ──────────────────────────────────────────────────────────

        private static void Holders(FederatedReplayManager manager, long netId, List<NodeEntity> into)
        {
            into.Clear();
            for (int i = 0; i < manager.NodeCount; i++)
            {
                var repo = manager.ContextAt(i).SandboxRepo;
                var e = Find(repo, netId);
                if (!e.IsNull) into.Add(new NodeEntity(manager.NodeIdAt(i), e));
            }
        }

        private static Entity Find(EntityRepository repo, long netId)
            => netId is > 0 and <= int.MaxValue ? Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(repo, (int)netId) : Entity.Null;   // ⭐ BP-508 — the ONE resolver

        /// <summary>The node whose record / mask supplies <paramref name="typeId"/> of the entity (the merge's rule), or -1. <paramref name="nodes"/> is reordered in place (primary owner first).</summary>
        private int OwnerOf(FederatedReplayManager manager, List<NodeEntity> nodes, int typeId, List<BitMask512> scratch)
        {
            _ownership.Claim(manager, nodes, scratch);
            for (int i = 0; i < nodes.Count; i++) if (scratch[i].IsSet(typeId)) return nodes[i].NodeId;
            return -1;
        }

        // ── the lens for one component (Details ▸ By node) ───────────────────────────────────

        /// <summary>
        /// The views of component <paramref name="component"/> of network id <paramref name="netId"/>: one row per node holding a copy, the owner first (then by node id). Null when fewer than one node holds it.
        /// Allocates — a host calls it when the merged frame, the selection or the open component changes, never per frame.
        /// </summary>
        public ReplicaView? Compute(FederatedReplayManager manager, long netId, Type component)
        {
            int typeId = ComponentTypeRegistry.GetId(component);
            if (typeId < 0) return null;
            var nodes = new List<NodeEntity>();
            Holders(manager, netId, nodes);
            var holders = new List<NodeEntity>();
            foreach (var n in nodes) if (manager.Contexts[n.NodeId].SandboxRepo.GetComponentMask(n.Entity.Index).IsSet(typeId)) holders.Add(n);
            if (holders.Count == 0) return null;

            int owner = OwnerOf(manager, nodes, typeId, new List<BitMask512>());
            var rows = new List<ReplicaRow>(holders.Count);
            JsonObject? ownerJson = null;
            if (owner >= 0)
                foreach (var h in holders) if (h.NodeId == owner) ownerJson = Json(manager, h, typeId);
            var ownerLeaves = ownerJson == null ? null : Leaves(ownerJson);

            foreach (var h in holders)
            {
                var json = h.NodeId == owner ? ownerJson : Json(manager, h, typeId);
                var leaves = h.NodeId == owner ? ownerLeaves : json == null ? null : Leaves(json);
                bool differs = h.NodeId != owner && ownerJson != null && json != null && !IsPerNode(component)
                    && _diff.ComputeDiff(component.Name, ownerJson, json, Epsilon)?.IsModified == true;
                rows.Add(new ReplicaRow(h.NodeId, h.NodeId == owner, leaves == null ? "—" : Text(leaves),
                    differs, differs && ownerLeaves != null && leaves != null ? Distance(ownerLeaves, leaves) : 0d));
            }
            rows.Sort((a, b) => a.IsOwner != b.IsOwner ? (a.IsOwner ? -1 : 1) : a.NodeId.CompareTo(b.NodeId));
            return new ReplicaView(netId, component, owner, rows);
        }

        private JsonObject? Json(FederatedReplayManager manager, NodeEntity h, int typeId)
        {
            var repo = manager.Contexts[h.NodeId].SandboxRepo;
            var mask = new BitMask512(); mask.SetBit(typeId);
            try
            {
                var resolver = new NetIdResolver(repo);
                var json = _serializer.SerializeEntity(repo, h.Entity, resolver, mask);
                // ⭐ the HEADER-native translators run whatever the mask (ScenarioSerializer.SerializeEntity) — their keys carry each node's OWN
                //   header data, so left in, every copy "differs" from the owner. 📐 measured on a real 4-node recording: 100 % of frames marked for
                //   every unit, NetworkIdentity and EntityGuid "differing" with Δ 0. Only the component's own keys are compared.
                foreach (var kv in _serializer.SerializeEntity(repo, h.Entity, resolver, default))
                    json.Remove(kv.Key);
                return json;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { return null; }
        }

        // ── the lane (Event Timeline ▸ Replica differs · <unit>) ─────────────────────────────

        /// <summary>
        /// Does any replicated component of <paramref name="netId"/> differ between its owner and a replica, at the manager's CURRENT frames? A component counts when ≥ 2 nodes hold it and
        /// it is recorded and not node-local; its owner is the merge's.
        /// </summary>
        public bool AnyReplicaDiffers(FederatedReplayManager manager, long netId, List<NodeEntity>? scratchNodes = null, List<BitMask512>? scratchMasks = null)
        {
            var nodes = scratchNodes ?? new List<NodeEntity>();
            Holders(manager, netId, nodes);
            if (nodes.Count < 2) return false;
            var union = new BitMask512();
            var common = new BitMask512();
            foreach (var n in nodes)
            {
                var m = manager.Contexts[n.NodeId].SandboxRepo.GetComponentMask(n.Entity.Index);
                var both = union; both.BitwiseAnd(m);
                common.BitwiseOr(both);
                union.BitwiseOr(m);
            }
            var masks = scratchMasks ?? new List<BitMask512>();
            for (int typeId = 0; typeId < FdpConfig.MAX_COMPONENT_TYPES; typeId++)
            {
                if (!common.IsSet(typeId)) continue;
                var type = ComponentTypeRegistry.GetType(typeId);
                if (type == null || IsPerNode(type) || DataPolicyAttribute.IsNodeLocal(type) || !ComponentTypeRegistry.IsRecordable(typeId)) continue;
                int owner = OwnerOf(manager, nodes, typeId, masks);
                if (owner < 0) continue;
                JsonObject? ownerJson = null;
                foreach (var n in nodes) if (n.NodeId == owner) ownerJson = Json(manager, n, typeId);
                if (ownerJson == null) continue;
                foreach (var n in nodes)
                {
                    if (n.NodeId == owner || !manager.Contexts[n.NodeId].SandboxRepo.GetComponentMask(n.Entity.Index).IsSet(typeId)) continue;
                    var json = Json(manager, n, typeId);
                    if (json != null && _diff.ComputeDiff(type.Name, ownerJson, json, Epsilon)?.IsModified == true) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ⭐ The <i>Replica differs</i> lane's data: the frames (of the PRIMARY node's recording) at which <see cref="AnyReplicaDiffers"/> holds for <paramref name="netId"/>. Runs over its OWN
        /// <see cref="FederatedReplayManager"/> on <paramref name="paths"/> (never the shown world's sandboxes), with the same per-node <paramref name="offsetTicks"/> the shown merge applies — so a frame
        /// here is a frame the Review shows. A background pass: cancel through <paramref name="ct"/> (the selection moved).
        /// </summary>
        public IReadOnlyList<int> ScanDifferingFrames(string[] paths, IReadOnlyDictionary<int, long> offsetTicks, int primaryNode, long netId, CancellationToken ct = default)
        {
            var frames = new List<int>();
            using var m = FederatedReplayManager.LoadGroup(paths);
            foreach (var kv in offsetTicks) if (m.Contexts.ContainsKey(kv.Key) && kv.Value != 0) m.SetNodeOffset(kv.Key, kv.Value);
            if (!m.Contexts.TryGetValue(primaryNode, out var primary) || primary.Playback is not { TotalFrames: > 0 } pb) return frames;
            var nodes = new List<NodeEntity>(); var masks = new List<BitMask512>();
            long lastKey = long.MinValue; bool lastResult = false;
            for (int f = 0; f < pb.TotalFrames; f++)
            {
                if (ct.IsCancellationRequested) break;
                m.SetBaseWallTicks(pb.GetFrameMetadata(f).WallClockTicks);
                long key = StateKey(m);
                if (key != lastKey) { lastKey = key; lastResult = AnyReplicaDiffers(m, netId, nodes, masks); }
                if (lastResult) frames.Add(f);
            }
            return frames;
        }

        // ── the replica WATCH scan (Review §6.13 RW-2, RW-3) ─────────────────────────────────

        private readonly struct WatchValue
        {
            public readonly bool IsNumber; public readonly double Number; public readonly string Text;
            public WatchValue(double n, string t) { IsNumber = true; Number = n; Text = t; }
            public WatchValue(string t) { IsNumber = false; Number = 0; Text = t; }
        }

        private sealed class ResolvedItem
        {
            public ReplicaWatchItem Item = null!; public int TypeId; public ushort AttributeId; public int Sub; public string? Field;
            // the open run of disagreement
            public bool Open; public int OwnerNodeAtStart; public int StartFrame; public long StartWall; public string OwnerText = ""; public List<(int, string)> Believers = new();
        }

        private static WatchValue? ReadWatch(EntityRepository repo, Entity e, ResolvedItem r, JsonObject? json)
        {
            if (r.Field == null)
            {
                if (!Fdp.Toolkit.Replication.Attributes.AttributeReaders.TryRead(repo, e, r.AttributeId, r.Sub, out var v)) return null;
                switch (v.Kind)
                {
                    case Fdp.Toolkit.Replication.Patching.AttributeValueKind.Bool: return new WatchValue(v.BoolValue ? 1 : 0, v.BoolValue ? "true" : "false");
                    case Fdp.Toolkit.Replication.Patching.AttributeValueKind.CsInt32: return new WatchValue(v.IntValue, v.IntValue.ToString(CultureInfo.InvariantCulture));
                    case Fdp.Toolkit.Replication.Patching.AttributeValueKind.CsInt64: return new WatchValue(v.LongValue, v.LongValue.ToString(CultureInfo.InvariantCulture));
                    case Fdp.Toolkit.Replication.Patching.AttributeValueKind.CsFloat32: return new WatchValue(v.FloatValue, v.FloatValue.ToString("0.###", CultureInfo.InvariantCulture));
                    case Fdp.Toolkit.Replication.Patching.AttributeValueKind.CsFloat64: return new WatchValue(v.DoubleValue, v.DoubleValue.ToString("0.###", CultureInfo.InvariantCulture));
                    default: return new WatchValue(v.Text.ToString());
                }
            }
            var node = json == null ? null : ComponentJsonPath.Get(json, r.Item.Path);
            if (node is not JsonValue jv) return node == null ? null : new WatchValue(node.ToJsonString());
            if (jv.TryGetValue<double>(out var d)) return new WatchValue(d, d.ToString("0.###", CultureInfo.InvariantCulture));
            return new WatchValue(jv.ToJsonString().Trim('"'));
        }

        private static bool WatchDiffers(in WatchValue owner, in WatchValue other, double tolerance)
            => owner.IsNumber && other.IsNumber ? Math.Abs(owner.Number - other.Number) > tolerance : owner.Text != other.Text;

        /// <summary>
        /// ⭐ The replica watch's data (RW-2, RW-3): over the group's files (its OWN <see cref="FederatedReplayManager"/>, the shown merge's node offsets) the SPANS during which some node's copy of a watched item differed
        /// from the OWNER's (the attribute's component's owner, <see cref="OwnershipSource"/>) beyond the item's tolerance for LONGER than <paramref name="thresholdSeconds"/> — a run of differing frames ends at the first
        /// frame that agrees again, so a one-frame blip lasts one frame interval. Items read through <c>AttributeReaders</c>; an <see cref="ReplicaWatchItem.Expert"/> one through <c>ComponentJsonPath</c> over the component's JSON
        /// (<c>Component.field</c>). A background pass: cancel through <paramref name="ct"/>.
        /// </summary>
        public IReadOnlyList<ReplicaSpan> ScanWatchSpans(string[] paths, IReadOnlyDictionary<int, long> offsetTicks, int primaryNode, long netId,
            IReadOnlyList<ReplicaWatchItem> items, double thresholdSeconds, CancellationToken ct = default)
        {
            var spans = new List<ReplicaSpan>();
            var resolved = new List<(ResolvedItem R, int Index)>();
            for (int i = 0; i < items.Count; i++)
            {
                var r = new ResolvedItem { Item = items[i], TypeId = -1 };
                if (!items[i].Expert && Fdp.Toolkit.Replication.Attributes.AttributeReaders.TryResolve(items[i].Path, out r.AttributeId, out var sub)
                    && Fdp.Toolkit.Replication.Attributes.AttributeReaders.DefinitionOf(r.AttributeId) is { } def)
                { r.Sub = items[i].SubIndex != 0 ? items[i].SubIndex : sub; r.TypeId = ComponentTypeRegistry.GetId(def.Component); }
                else if (items[i].Expert)
                {
                    int dot = items[i].Path.IndexOf('.');
                    string comp = dot > 0 ? items[i].Path.Substring(0, dot) : items[i].Path;
                    r.Field = dot > 0 ? items[i].Path.Substring(dot + 1) : "";
                    for (int t = 0; t < FdpConfig.MAX_COMPONENT_TYPES && r.TypeId < 0; t++) if (ComponentTypeRegistry.GetType(t)?.Name == comp) r.TypeId = t;
                }
                if (r.TypeId >= 0) resolved.Add((r, i));
            }
            if (resolved.Count == 0) return spans;

            using var m = FederatedReplayManager.LoadGroup(paths);
            foreach (var kv in offsetTicks) if (m.Contexts.ContainsKey(kv.Key) && kv.Value != 0) m.SetNodeOffset(kv.Key, kv.Value);
            if (!m.Contexts.TryGetValue(primaryNode, out var primary) || primary.Playback is not { TotalFrames: > 0 } pb) return spans;
            long threshold = (long)(thresholdSeconds * TimeSpan.TicksPerSecond);
            long wall0 = pb.GetFrameMetadata(0).WallClockTicks;
            var nodes = new List<NodeEntity>(); var masks = new List<BitMask512>();
            long lastKey = long.MinValue; long lastWall = wall0, prevWall = wall0, frameLen = 0;

            void Close(ResolvedItem r, int index, int endFrame, long endWall)
            {
                if (!r.Open) return;
                r.Open = false;
                if (endWall - r.StartWall > threshold)
                    spans.Add(new ReplicaSpan(index, r.Believers.Count > 0 ? r.OwnerNodeAtStart : -1, r.OwnerText, r.Believers.ToArray(), r.StartFrame, endFrame,
                        (r.StartWall - wall0) / (double)TimeSpan.TicksPerSecond, (endWall - wall0) / (double)TimeSpan.TicksPerSecond));
            }

            for (int f = 0; f < pb.TotalFrames; f++)
            {
                if (ct.IsCancellationRequested) return spans;
                long wall = pb.GetFrameMetadata(f).WallClockTicks;
                m.SetBaseWallTicks(wall);
                lastWall = wall;
                long key = StateKey(m);
                if (f > 0) frameLen = wall - prevWall;
                prevWall = wall;
                if (key == lastKey) continue;   // nothing moved: every open run stays as it is
                lastKey = key;
                Holders(m, netId, nodes);
                foreach (var (r, index) in resolved)
                {
                    int owner = nodes.Count < 2 ? -1 : OwnerOf(m, nodes, r.TypeId, masks);
                    List<(int, string)>? bad = null; string ownerText = "";
                    if (owner >= 0)
                    {
                        NodeEntity? own = null;
                        foreach (var n in nodes) if (n.NodeId == owner) own = n;
                        JsonObject? ownerJson = own != null && r.Field != null ? Json(m, own.Value, r.TypeId) : null;
                        var ov = own == null ? null : ReadWatch(m.Contexts[owner].SandboxRepo, own.Value.Entity, r, ownerJson);
                        if (ov is { } ownerValue)
                        {
                            ownerText = ownerValue.Text;
                            foreach (var n in nodes)
                            {
                                if (n.NodeId == owner) continue;
                                var repo = m.Contexts[n.NodeId].SandboxRepo;
                                var json = r.Field != null ? Json(m, n, r.TypeId) : null;
                                if (ReadWatch(repo, n.Entity, r, json) is { } nv && WatchDiffers(ownerValue, nv, r.Item.Tolerance)) (bad ??= new()).Add((n.NodeId, nv.Text));
                            }
                        }
                    }
                    if (bad == null) { Close(r, index, f - 1, wall); continue; }
                    if (!r.Open) { r.Open = true; r.StartFrame = f; r.StartWall = wall; r.OwnerText = ownerText; r.OwnerNodeAtStart = owner; r.Believers = new List<(int, string)>(); }
                    foreach (var b in bad) { bool seen = false; foreach (var x in r.Believers) if (x.Item1 == b.Item1) seen = true; if (!seen) r.Believers.Add(b); }
                }
            }
            foreach (var (r, index) in resolved) Close(r, index, pb.TotalFrames - 1, lastWall + frameLen);   // the last frame lasts one interval
            spans.Sort((a, b) => a.StartFrame != b.StartFrame ? a.StartFrame.CompareTo(b.StartFrame) : a.Item.CompareTo(b.Item));
            return spans;
        }

        // ── value text ───────────────────────────────────────────────────────────────────────

        private static List<(string path, string text, double? number)> Leaves(JsonNode node)
        {
            var list = new List<(string, string, double?)>();
            Walk(node, "", list);
            return list;
        }

        private static void Walk(JsonNode? node, string path, List<(string, string, double?)> into)
        {
            switch (node)
            {
                case JsonObject o: foreach (var kv in o) Walk(kv.Value, path + "/" + kv.Key, into); break;
                case JsonArray a: for (int i = 0; i < a.Count; i++) Walk(a[i], path + "/" + i, into); break;
                case JsonValue v:
                    if (v.TryGetValue<double>(out var d)) into.Add((path, d.ToString("0.###", CultureInfo.InvariantCulture), d));
                    else into.Add((path, v.ToJsonString().Trim('"'), null));
                    break;
            }
        }

        private static string Text(List<(string path, string text, double? number)> leaves)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < leaves.Count; i++) { if (i > 0) sb.Append(" · "); sb.Append(leaves[i].text); if (sb.Length > 120) { sb.Append('…'); break; } }
            return sb.Length == 0 ? "(empty)" : sb.ToString();
        }

        /// <summary>The distance between two copies over their NUMERIC leaves (what the Δ column shows); 0 when none are comparable.</summary>
        private static double Distance(List<(string path, string text, double? number)> a, List<(string path, string text, double? number)> b)
        {
            double sum = 0;
            for (int i = 0; i < a.Count && i < b.Count; i++)
                if (a[i].number is double x && b[i].number is double y) sum += (x - y) * (x - y);
            return Math.Sqrt(sum);
        }

        /// <summary>A raw handle is written as the NETWORK id it names (so two nodes' handles to the same unit read alike); an unnetworked / dead one as "null" / its handle.</summary>
        private sealed class NetIdResolver : IGuidResolver
        {
            private readonly EntityRepository _repo;
            public NetIdResolver(EntityRepository repo) => _repo = repo;
            public string Resolve(Entity entity)
            {
                if (entity == Entity.Null) return "null";
                if (_repo.IsAlive(entity) && _repo.HasComponent<NetworkIdentity>(entity)) return "net:" + _repo.GetComponent<NetworkIdentity>(entity).Value.ToString(CultureInfo.InvariantCulture);
                return $"[{entity.Index}, v{entity.Generation}]";
            }
            public Entity Resolve(string guidStr) => Entity.Null;
        }
    }
}
