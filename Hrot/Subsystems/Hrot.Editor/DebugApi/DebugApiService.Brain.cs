using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Services;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-1248</c> / <c>CE-1249</c> (R-1029, AV-6) — <c>GET /entities/{networkId}/brain</c> and <c>/journal?since=</c>: the Brain tab's
    /// reader (<see cref="BrainSnapshot.Read"/>) and the brain journal, over a WORLD and its network map — the live world here, the replay
    /// sandbox for the <c>/replay</c> twins. 📄 docs/DESIGN_Agent_Shared_View.md §6.
    /// </summary>
    public sealed partial class DebugApiService
    {
        public JsonNode GetEntityBrain(long networkId) => BrainOf(_world, _entityMap, networkId);

        public JsonNode GetEntityJournal(long networkId, uint since) => JournalOf(_world, _entityMap, networkId, since);

        /// <summary>⭐ <c>CE-1329</c> (Brain View §12r BV-72) — <c>GET /entities/{networkId}/debug-settings</c>: the unit's switches in the ONE store
        /// the Brain tab and the map's right-click menu use — its pinned gizmo families, its trace (the user's switch; <c>traceOn</c> also counts
        /// a Brain tab showing it and an observe / breakpoint arming), and whether the camera follows it. ⛔ SUPERSEDED: <c>POST /entities/{id}/debug</c>.</summary>
        public (JsonNode? Result, string? Error) GetDebugSettings(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {networkId} not found.");
            return (DebugSettingsJson(networkId), null);
        }

        /// <summary>⭐ <c>CE-1329</c> — <c>POST /entities/{networkId}/debug-settings {pins?, trace?, follow?, reset?}</c>: sets what is given
        /// (<c>pins</c> replaces the pinned families; <c>reset</c> forgets everything remembered for the unit first) and answers as the GET does.
        /// Remembered per scenario, like the Brain tab's ticks.</summary>
        public (JsonNode? Result, string? Error) SetDebugSettings(long networkId, JsonNode? body)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {networkId} not found.");
            var store = UnitDebugSettings.Of(_world);
            if (body?["reset"]?.GetValue<bool>() == true)
            {
                store.ResetUnit(networkId);
                if (Hrot.UI.Common.Map3D.ViewControl.Of(_world) is { Following: { } f } v && f.Unit == entity) v.StopFollowing();
            }
            if (body?["pins"] is JsonArray pins)
            {
                var wanted = AiOverlayFlags.None;
                foreach (var p in pins)
                {
                    string name = p?.GetValue<string>() ?? "";
                    var family = Array.Find(Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All, f =>
                        string.Equals(f.ToString(), name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.Label(f), name, StringComparison.OrdinalIgnoreCase));
                    if (family == AiOverlayFlags.None)
                        return (null, $"Unknown gizmo family '{name}'. One of: {string.Join(", ", Array.ConvertAll(Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All, Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.Label))}.");
                    wanted |= family;
                }
                foreach (var f in Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All) store.SetPin(networkId, f, (wanted & f) != 0);
            }
            if (body?["trace"] is JsonValue trace && trace.TryGetValue<bool>(out var traceOn))
                Hrot.Common.Diagnostics.Gizmos.AiTraceActions.Set(_world, entity, BehaviorDebugFlags.EnableTraceBuffer, traceOn);
            if (body?["follow"] is JsonValue follow && follow.TryGetValue<bool>(out var followOn))
            {
                var view = Hrot.UI.Common.Map3D.ViewControl.Of(_world);
                if (followOn)
                {
                    if (view?.FollowUnit(entity) is { } refusal) return (null, refusal);
                    if (view == null) store.SetFollowed(networkId);   // headless: remembered, followed when a map draws
                }
                else if (store.Followed == networkId)
                {
                    if (view != null) view.StopFollowing();
                    store.SetFollowed(null);
                }
            }
            return (DebugSettingsJson(networkId), null);
        }

        private JsonObject DebugSettingsJson(long networkId)
        {
            var store = UnitDebugSettings.Peek(_world);
            var pins = new JsonArray();
            foreach (var f in Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All)
                if (store?.IsPinned(networkId, f) == true) pins.Add(Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.Label(f));
            return new JsonObject
            {
                ["networkId"] = networkId, ["pins"] = pins, ["trace"] = store?.IsTraceSet(networkId) == true,
                ["traceOn"] = store?.TraceOn(networkId) == true, ["follow"] = store?.Followed == networkId,
            };
        }

        /// <summary>⭐ <c>CE-1258</c> (BV-28) — <c>POST /entities/{networkId}/journal/break {kinds?}</c>: stop the sim when THIS unit's journal
        /// gains an entry (of one of <paramref name="kinds"/>, when given). Hits come through <c>GET /breakpoints/hits</c>.</summary>
        public (JsonNode? Result, string? Error) ArmJournalBreak(long networkId, string[]? kinds)
        {
            if (_bpManager is null)
                throw new Hrot.Presentation.DebugApi.NotSupportedHereException(Hrot.Presentation.DebugApi.DebugCapabilities.Breakpoints);
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {networkId} not found.");
            if (!_world.IsComponentTypeRegistered<BrainJournal>() || !_world.HasComponent<BrainJournal>(entity))
                return (null, $"Entity {networkId} has no brain journal on this node (not a brain unit here, or the journal is off).");
            var parsed = new System.Collections.Generic.List<BrainJournalKind>();
            foreach (var k in kinds ?? Array.Empty<string>())
            {
                if (!Enum.TryParse<BrainJournalKind>(k, ignoreCase: true, out var kind) || kind == BrainJournalKind.None)
                    return (null, $"Unknown journal kind '{k}'. One of: {string.Join(", ", Enum.GetNames<BrainJournalKind>()[1..])}.");
                parsed.Add(kind);
            }
            var id = _bpManager.AddBreakpoint(Fdp.Toolkit.ReplayBrowser.Search.BrainJournalBreak.Condition(parsed.ToArray()), entity, 1,
                $"journal #{networkId}" + (parsed.Count > 0 ? $" ({string.Join(", ", parsed)})" : ""));
            return (new JsonObject { ["breakpointId"] = id.ToString(), ["networkId"] = networkId }, null);
        }

        /// <summary>⭐ <c>CE-1258</c> (BV-29) — while paused on a hit: the hit unit's journal entries the tick wrote (the live world shows the
        /// start of the tick; the manager keeps the end of it).</summary>
        private JsonArray? JournalWrittenByTheHitTick(Entity entity)
        {
            if (_bpManager?.PostTickView is not EntityRepository after || !after.IsComponentTypeRegistered<BrainJournal>()) return null;
            if (!after.IsAlive(entity) || !after.HasComponent<BrainJournal>(entity)) return null;
            uint before = _world.HasComponent<BrainJournal>(entity) ? _world.GetComponentRO<BrainJournal>(entity).Seq : 0u;
            ref readonly var j = ref after.GetComponentRO<BrainJournal>(entity);
            string Name(int h) => h == 0 ? "nothing" : BehaviorNameOf(h) ?? $"#{h}";
            var list = new JsonArray();
            for (int i = 0; i < j.Count; i++)
            {
                var e = j.At(i);
                if (e.Seq > before)
                    list.Add(BrainSnapshot.ChangeJson(new BrainChange(e.Seq, e.At, e.Kind.ToString(), BrainJournalText.Describe(in e, Name, BehaviorDefinitionOf))));
            }
            return list.Count > 0 ? list : null;
        }

        private Fdp.Toolkit.Behavior.BehaviorDefinition? BehaviorDefinitionOf(int hash)
            => _behaviorRegistry is { } reg && reg.TryGetDefinition(hash, out var d) ? d : null;

        private string? BehaviorNameOf(int hash)
            => _behaviorRegistry is { } reg && reg.TryGetName(hash, out var n) ? n : null;

        private JsonNode BrainOf(EntityRepository world, NetworkEntityMap map, long networkId)
        {
            if (!map.TryGetEntity(networkId, out var entity) || !world.IsAlive(entity))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            var brain = BrainSnapshot.Read(world, entity, BehaviorNameOf, BehaviorDefinitionOf);
            if (brain is null)
                return new JsonObject { ["networkId"] = networkId, ["hasBrain"] = false, ["note"] = "No BehaviorState on this entity." };
            var json = brain.ToJson();
            json["networkId"] = networkId;
            json["hasBrain"] = true;
            if (!brain.OnBrainNode)
                json["note"] = "This node does not run the unit's brain (BV-6): only task / SOP / ROE are known. Switch to the brain's perspective.";
            return json;
        }

        private JsonNode JournalOf(EntityRepository world, NetworkEntityMap map, long networkId, uint since)
        {
            if (!map.TryGetEntity(networkId, out var entity) || !world.IsAlive(entity))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            if (!world.IsComponentTypeRegistered<BrainJournal>() || !world.HasComponent<BrainJournal>(entity))
                return new JsonObject { ["networkId"] = networkId, ["on"] = false,
                                        ["note"] = "No brain journal here: not a brain unit on this node, or the journal is switched off." };

            ref readonly var j = ref world.GetComponentRO<BrainJournal>(entity);
            string Name(int h) => h == 0 ? "nothing" : BehaviorNameOf(h) ?? $"#{h}";
            var entries = new JsonArray();
            uint oldestKept = j.Count == 0 ? j.Seq + 1 : j.At(0).Seq;
            for (int i = 0; i < j.Count; i++)   // oldest first
            {
                var e = j.At(i);
                if (e.Seq <= since) continue;
                entries.Add(BrainSnapshot.ChangeJson(new BrainChange(e.Seq, e.At, e.Kind.ToString(), BrainJournalText.Describe(in e, Name, BehaviorDefinitionOf))));
            }
            return new JsonObject
            {
                ["networkId"] = networkId, ["on"] = true, ["seq"] = j.Seq,
                // ⚠ more than the ring holds were written since `since` — the oldest of them are gone
                ["gap"] = since + 1 < oldestKept && j.Seq > since,
                ["entries"] = entries,
            };
        }

        // ── ⭐ CE-1265 / CE-1266 (AV-6…AV-9) — the same readers over ONE loaded recording (POST /replay/load first) ──────────────

        private string? _replayFdpPath;
        private Fdp.Toolkit.ReplayBrowser.Search.RecordingSearchService? _recordingSearch;

        private (EntityRepository World, NetworkEntityMap Map) ReplayWorld()
        {
            if (_replayContext is null) throw new InvalidOperationException("No replay loaded. Call /replay/load first.");
            var repo = _replayContext.SandboxRepo;
            return (repo, SandboxMap(repo));
        }

        private static NetworkEntityMap SandboxMap(EntityRepository repo)
        {
            var map = new NetworkEntityMap();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) return map;
            foreach (var e in repo.Query().With<Fdp.Toolkit.Replication.Components.NetworkIdentity>().Build())
            {
                long id = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value;
                if (!map.TryGetEntity(id, out _)) map.Register(id, e);
            }
            return map;
        }

        /// <summary><c>GET /replay/entities/{networkId}/brain</c> — the Brain tab's snapshot at the seeked frame.</summary>
        public JsonNode GetReplayEntityBrain(long networkId)
        {
            var (w, m) = ReplayWorld();
            var node = BrainOf(w, m, networkId);
            node["frame"] = _replayContext!.CurrentFrame;
            return node;
        }

        /// <summary><c>GET /replay/entities/{networkId}/journal?since=</c> — the ring as it was at the seeked frame.</summary>
        public JsonNode GetReplayEntityJournal(long networkId, uint since)
        {
            var (w, m) = ReplayWorld();
            var node = JournalOf(w, m, networkId, since);
            node["frame"] = _replayContext!.CurrentFrame;
            return node;
        }

        /// <summary><c>GET /replay/selection</c> — the operator's selection at the seeked frame (recorded: <c>SelectionState</c> is NoScenario).</summary>
        public JsonNode GetReplaySelection()
        {
            var (w, m) = ReplayWorld();
            var (primary, all) = SelectionOf(w, m);
            return new JsonObject { ["frame"] = _replayContext!.CurrentFrame, ["primary"] = primary, ["selection"] = all };
        }

        /// <summary>
        /// ⭐ <c>CE-1265</c> (AV-8) — <c>GET /replay/entities/{networkId}/journal/history?from=&amp;to=</c>: EVERY journal entry of one unit
        /// in the frame range, each once (keyed by its <c>Seq</c>), with the frame it was first seen at. A walk of the recording frame by
        /// frame (every entry dirties the unit's chunk, so the recorder wrote that frame); the seek is restored afterwards.
        /// </summary>
        public JsonNode GetReplayJournalHistory(long networkId, int? from, int? to)
        {
            if (_replayContext is null) throw new InvalidOperationException("No replay loaded. Call /replay/load first.");
            var ctx = _replayContext;
            int total = ctx.Playback?.TotalFrames ?? 0;
            int start = Math.Clamp(from ?? 0, 0, Math.Max(0, total - 1));
            int end = Math.Clamp(to ?? total - 1, start, Math.Max(0, total - 1));
            int restore = ctx.CurrentFrame;
            string Name(int h) => h == 0 ? "nothing" : BehaviorNameOf(h) ?? $"#{h}";

            var entries = new JsonArray();
            uint lastSeq = 0;
            bool gap = false, found = false;
            ctx.SeekToFrame(start, suppressHistory: true);
            Entity unit = Entity.Null;
            for (int frame = start; ; frame++)
            {
                var repo = ctx.SandboxRepo;
                if (unit == Entity.Null || !repo.IsAlive(unit)) unit = FindByNetworkId(repo, networkId);
                if (unit != Entity.Null && repo.IsComponentTypeRegistered<BrainJournal>() && repo.HasComponent<BrainJournal>(unit))
                {
                    found = true;
                    ref readonly var j = ref repo.GetComponentRO<BrainJournal>(unit);
                    if (j.Seq > lastSeq)
                    {
                        if (j.Count > 0 && j.At(0).Seq > lastSeq + 1 && lastSeq != 0) gap = true;
                        for (int i = 0; i < j.Count; i++)
                        {
                            var e = j.At(i);
                            if (e.Seq <= lastSeq) continue;
                            var row = BrainSnapshot.ChangeJson(new BrainChange(e.Seq, e.At, e.Kind.ToString(),
                                BrainJournalText.Describe(in e, Name, BehaviorDefinitionOf)));
                            row["frame"] = frame;
                            entries.Add(row);
                        }
                        lastSeq = j.Seq;
                    }
                }
                if (frame >= end || !ctx.StepForward(suppressHistory: true)) break;
            }
            ctx.SeekToFrame(restore, suppressHistory: true);
            _replayExtraction = BuildReplayExtractionService(ctx.SandboxRepo);

            return new JsonObject
            {
                ["networkId"] = networkId, ["from"] = start, ["to"] = end, ["found"] = found, ["gap"] = gap,
                ["count"] = entries.Count, ["entries"] = entries,
            };
        }

        /// <summary>⭐ <c>CE-1266</c> (AV-9) — <c>POST /replay/search {condition, networkId?, limit?}</c>: the frames (and units) of the loaded
        /// recording where <paramref name="body"/>'s condition holds — the same condition DTO breakpoints take.</summary>
        public (JsonNode? Result, string? Error) SearchReplay(JsonNode? body)
        {
            if (_replayContext is null || _replayFdpPath is null) return (null, "No replay loaded. Call /replay/load first.");
            if (body?["condition"] is not JsonNode conditionNode) return (null, "condition is required.");
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto condition;
            try
            {
                condition = System.Text.Json.JsonSerializer.Deserialize<Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto>(
                    conditionNode.ToJsonString(), SearchPredicateJsonOptions) ?? throw new ArgumentException("condition deserialized to null.");
            }
            catch (Exception ex) { return (null, $"Invalid condition: {ex.Message}"); }
            // ⛔ An unknown component name deserializes to null and used to match nothing, silently — a zero that reads like an answer.
            if (UnknownComponentIn(condition)) return (null, "Invalid condition: a ComponentType names no registered component (use its name, e.g. \"BehaviorState\", or its full name).");

            var filter = body["networkId"] is JsonNode n
                ? new Fdp.Toolkit.ReplayBrowser.Search.TargetEntityFilter { UseNetworkId = true, NetworkId = n.GetValue<long>() }
                : null;
            int limit = body["limit"]?.GetValue<int>() ?? 200;
            _recordingSearch ??= new Fdp.Toolkit.ReplayBrowser.Search.RecordingSearchService(
                new Fdp.Toolkit.ReplayBrowser.Search.PredicateCompiler(_componentEditSvc, _behaviorRegistry, _blueprintRegistry),
                new Fdp.Toolkit.ReplayBrowser.Search.EventScannerCompiler(_componentEditSvc));
            var hits = _recordingSearch.ExecuteSearch(_replayFdpPath, condition, filter);

            var map = SandboxMap(_replayContext.SandboxRepo);
            var list = new JsonArray();
            foreach (var h in hits)
            {
                if (list.Count >= limit) break;
                list.Add(new JsonObject
                {
                    ["frame"] = h.FrameIndex,
                    ["networkId"] = h.Entity != Entity.Null && map.TryGetNetworkId(h.Entity, out var id) ? id : null,
                    ["context"] = h.ContextMessage,
                });
            }
            return (new JsonObject { ["count"] = hits.Count, ["truncated"] = hits.Count > limit, ["hits"] = list }, null);
        }

        private static bool UnknownComponentIn(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto p) => p switch
        {
            Fdp.Toolkit.ReplayBrowser.Search.PropertyMatchDto m => m.ComponentType is null,
            Fdp.Toolkit.ReplayBrowser.Search.CompoundPredicateDto c => c.Conditions.Exists(UnknownComponentIn),
            _ => false,
        };

        // ⭐ BP-508 — the ONE resolver (ThereIsOneNetworkIdResolverTests): no inline network-id scan here.
        private static Entity FindByNetworkId(EntityRepository repo, long networkId)
            => NetworkIdResolver.FindEntityByNetworkId(repo, networkId);
    }
}
