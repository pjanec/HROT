using System;
using System.Text.Json.Nodes;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Presentation.DebugApi;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ RB-6 / RB-7 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — the timeline index's reads and a user's lane, for every host that
    /// keeps an index (the Replay Browser today); and <c>POST /selection</c> on every host, through the UI's own request path.
    /// </summary>
    public sealed partial class DebugApiService
    {
        private TimelineAccess Timeline => _dispatcher?.Timeline ?? throw NotSupportedHere(DebugCapabilities.TimelineRead);

        /// <summary><c>GET /timeline/lanes</c> — the lanes that apply here, each with its event count, and the index's state.</summary>
        public JsonNode GetTimelineLanes()
        {
            var t = Timeline;
            var source = t.Source();
            var index = source?.Index ?? TimelineIndex.Empty;
            var lanes = new JsonArray();
            foreach (var lane in t.Catalog.Lanes(t.Context))
                lanes.Add(new JsonObject
                {
                    ["id"] = lane.Id, ["name"] = lane.DisplayName, ["group"] = lane.Group, ["priority"] = lane.Priority,
                    ["glyph"] = lane.Glyph.ToString(), ["indexed"] = index.Has(lane.Id),
                    ["count"] = index.Has(lane.Id) ? index.Lane(lane.Id).Count : null,
                    ["error"] = index.LaneErrors.TryGetValue(lane.Id, out var e) ? e : null,
                });
            return new JsonObject
            {
                ["recording"] = source is RecordingTimelineSource r ? r.Path : null,
                ["progress"] = source is null ? null : Math.Round(source.Progress, 3),
                ["complete"] = source?.IsComplete,
                ["error"] = source?.Error,
                ["frames"] = index.TotalFrames,
                ["lanes"] = lanes,
            };
        }

        /// <summary><c>GET /timeline/events?lane=&amp;from=&amp;to=&amp;entity=&amp;limit=</c> — one lane's events in a frame range, optionally one unit's.</summary>
        public (JsonNode? Result, string? Error) GetTimelineEvents(string? lane, int? from, int? to, long? entity, int limit)
        {
            if (string.IsNullOrWhiteSpace(lane)) return (null, "lane is required (GET /timeline/lanes lists them).");
            var index = Timeline.Source()?.Index ?? TimelineIndex.Empty;
            if (!index.Has(lane)) return (null, $"Lane '{lane}' is not in the index (yet) — GET /timeline/lanes shows its state.");
            int f = from ?? 0, l = to ?? int.MaxValue;
            var events = new JsonArray();
            int matched = 0;
            foreach (var ev in index.Events(lane, f, l))
            {
                if (entity is { } id && ev.Subject != id && ev.Other != id) continue;
                matched++;
                if (events.Count >= limit) continue;
                events.Add(new JsonObject
                {
                    ["frame"] = ev.Frame, ["subject"] = ev.Subject, ["other"] = ev.Other == 0 ? null : ev.Other,
                    ["x"] = ev.X, ["y"] = ev.Y, ["value"] = ev.Value,
                });
            }
            return (new JsonObject { ["lane"] = lane, ["count"] = matched, ["truncated"] = matched > limit, ["events"] = events }, null);
        }

        /// <summary><c>POST /timeline/lanes {Lane: {Id, DisplayName, ...}, When: {condition}, Payload?}</c> — a user's lane: registered in
        /// the catalogue (its existing <c>Register</c>), the index then scans it in the background.</summary>
        public (JsonNode? Result, string? Error) AddTimelineLane(JsonNode? body)
        {
            if (body is null) return (null, "The lane is required: {\"Lane\": {\"Id\": \"my-lane\", \"DisplayName\": \"My lane\"}, \"When\": {condition}}.");
            IndexedLaneDefinition? lane;
            try { lane = System.Text.Json.JsonSerializer.Deserialize<IndexedLaneDefinition>(body.ToJsonString(), SearchPredicateJsonOptions); }
            catch (Exception ex) { return (null, $"Invalid lane: {ex.Message}"); }
            if (lane?.Lane is null || string.IsNullOrWhiteSpace(lane.Lane.Id) || lane.When is null)
                return (null, "Lane.Id and When are required.");
            var t = Timeline;
            if (t.Catalog.Find(lane.Lane.Id) is not null) return (null, $"A lane '{lane.Lane.Id}' exists already.");
            t.Catalog.Register(lane);
            return (new JsonObject { ["registered"] = lane.Lane.Id, ["note"] = "The index scans it in the background: GET /timeline/lanes shows its progress." }, null);
        }

        /// <summary>
        /// ⭐ RB-7 (R-4083) — <c>POST /selection {entity}</c>: points the operator's panels at a unit, through the UI's own request path —
        /// a <c>SelectEntityCommand</c> on the shown world, applied by its <c>SelectionRequestSystem</c> on the next frame (the click's path).
        /// ⛔ Never a direct <c>SelectionState</c> write (AV-4). <c>GET /selection</c> reads the result.
        /// </summary>
        public (JsonNode? Result, string? Error) SelectEntity(JsonNode? body)
        {
            if (body?["entity"] is not JsonNode node) return (null, "entity is required: {\"entity\": 1042} (a network id).");
            long id = Long(node);
            if (!_entityMap.TryGetEntity(id, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {id} not found in this world.");
            if (!_world.Bus.IsRegistered<Hrot.Common.Events.SelectEntityCommand>())
                return (null, "This world runs no selection (SelectEntityCommand is not registered here).");
            _world.Bus.Publish(new Hrot.Common.Events.SelectEntityCommand { NetworkId = id });
            return (new JsonObject { ["requested"] = id, ["note"] = "Applied on the next frame by the host's SelectionRequestSystem: GET /selection." }, null);
        }
    }
}
