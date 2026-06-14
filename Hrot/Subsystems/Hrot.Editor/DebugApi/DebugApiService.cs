using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Time.Controllers;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Testable service layer for the AI Debug API. Holds references to the editor's
    /// already-constructed services and implements one method per endpoint, returning a
    /// <see cref="JsonNode"/> payload produced via the inspector-grade DTO path
    /// (<see cref="EntityStateExtractionService"/> / <see cref="EventSerializationHelper"/>)
    /// so payloads pass through the envelope verbatim — never re-cased by the host.
    ///
    /// <para>
    /// <b>Threading:</b> every method that touches <c>_world</c> / <c>NetworkEntityMap</c> /
    /// the time controller assumes it runs on the main thread. <see cref="DebugApiHost"/>
    /// marshals those calls via <see cref="MainThreadJobQueue"/>. Event-history and
    /// scenario-list reads are thread-safe and may run off-thread.
    /// </para>
    /// </summary>
    public sealed class DebugApiService
    {
        private readonly EntityRepository                _world;
        private readonly NetworkEntityMap                _entityMap;
        private readonly IEntityStateExtractionService   _extraction;
        private readonly ITimeTransportFacade            _time;
        private readonly IPreviewController              _preview;
        private readonly IEditorLogic                    _editor;
        private readonly IDiagnosticEventHistoryService  _eventHistory;
        private readonly MasterSyncController             _timeController;
        private readonly Func<ClusterState>              _clusterState;

        /// <summary>Default upper bound for event-history queries.</summary>
        public const int DefaultMaxEvents = 200;

        public DebugApiService(
            EntityRepository                world,
            NetworkEntityMap                entityMap,
            IEntityStateExtractionService   extraction,
            ITimeTransportFacade            time,
            IPreviewController              preview,
            IEditorLogic                    editor,
            IDiagnosticEventHistoryService  eventHistory,
            MasterSyncController            timeController,
            Func<ClusterState>              clusterState)
        {
            _world            = world            ?? throw new ArgumentNullException(nameof(world));
            _entityMap        = entityMap        ?? throw new ArgumentNullException(nameof(entityMap));
            _extraction       = extraction       ?? throw new ArgumentNullException(nameof(extraction));
            _time             = time             ?? throw new ArgumentNullException(nameof(time));
            _preview          = preview          ?? throw new ArgumentNullException(nameof(preview));
            _editor           = editor           ?? throw new ArgumentNullException(nameof(editor));
            _eventHistory     = eventHistory     ?? throw new ArgumentNullException(nameof(eventHistory));
            _timeController   = timeController   ?? throw new ArgumentNullException(nameof(timeController));
            _clusterState     = clusterState     ?? throw new ArgumentNullException(nameof(clusterState));
        }

        // ── Group A — Status ──────────────────────────────────────────────────

        /// <summary><c>GET /status</c> — full status payload (main thread).</summary>
        public JsonNode GetStatus()
        {
            return new JsonObject
            {
                ["scenario"]     = _editor.LoadedScenarioName,
                ["clusterState"] = CurrentClusterState().ToString(),
                ["simTime"]      = _time.TotalTime,
                ["timeScale"]    = _time.TimeScale,
                ["isPaused"]     = _time.IsPaused,
                ["inPreview"]    = _preview.IsInPreviewMode,
                ["entityCount"]  = _world.EntityCount,
                ["recording"]    = false,
            };
        }

        // ── Group B — Entity queries ───────────────────────────────────────────

        /// <summary><c>GET /entities</c> — list (networkId, name, component type names) (main thread).</summary>
        public JsonNode ListEntities()
        {
            var dumps = _extraction.ExtractEntities();
            var arr   = new JsonArray();
            foreach (var d in dumps)
            {
                var comps = new JsonArray();
                foreach (var name in d.Components.Keys)
                    comps.Add(name);

                arr.Add(new JsonObject
                {
                    ["networkId"]  = d.NetworkId,
                    ["name"]       = ExtractEntityName(d),
                    ["components"] = comps,
                });
            }
            return arr;
        }

        /// <summary>
        /// <c>GET /entities/{networkId}</c> — full component dump via the serializer-injected
        /// extraction service. Returns <c>null</c> when the id is unknown (host → 404) (main thread).
        /// </summary>
        public JsonNode? DumpEntity(long networkId)
        {
            // Resolve must go through the map so an unknown id is reported as 404,
            // not silently returned as an empty dump.
            if (!_entityMap.TryGetEntity(networkId, out _))
                return null;

            var dumps = _extraction.ExtractEntities(new List<long> { networkId });
            if (dumps.Count == 0) return null;

            return DumpToJsonNode(dumps[0]);
        }

        // ── Group C — Event history ────────────────────────────────────────────

        /// <summary>
        /// <c>GET /events</c> — event history for the given bus, optionally filtered by type /
        /// since-frame / max. History retrieval + DTO mapping are thread-safe (off-thread OK).
        /// </summary>
        public JsonNode GetEvents(string? bus = "world", string? type = null, uint since = 0, int max = DefaultMaxEvents)
        {
            string provider = string.Equals(bus, "orchestration", StringComparison.OrdinalIgnoreCase)
                ? "Orchestration"
                : "World";

            var history = _eventHistory.GetHistory(new[] { provider });

            IEnumerable<CapturedEventDto> filtered = history;
            if (!string.IsNullOrEmpty(type))
                filtered = filtered.Where(e => string.Equals(e.TypeName, type, StringComparison.OrdinalIgnoreCase));
            if (since > 0)
                filtered = filtered.Where(e => e.Frame >= since);

            if (max < 0) max = DefaultMaxEvents;
            // Most-recent-first, bounded.
            var page = filtered.Reverse().Take(max).ToList();

            var arr = new JsonArray();
            foreach (var e in page)
            {
                JsonNode? payload = null;
                try
                {
                    // EventSerializationHelper produces inspector-grade readable JSON; parse it
                    // back into a JsonNode so it passes through the envelope verbatim.
                    var json = EventSerializationHelper.SerializeToJson(e.RawEvent);
                    payload  = JsonNode.Parse(json);
                }
                catch
                {
                    payload = null; // unserializable payload — keep the metadata row.
                }

                arr.Add(new JsonObject
                {
                    ["frame"]     = e.Frame,
                    ["provider"]  = e.ProviderName,
                    ["type"]      = e.TypeName,
                    ["isManaged"] = e.IsManaged,
                    ["summary"]   = e.Summary,
                    ["payload"]   = payload,
                });
            }
            return arr;
        }

        // ── Group D — Sim / preview / time control ─────────────────────────────

        /// <summary><c>GET /sim/state</c> (main thread).</summary>
        public JsonNode GetSimState() => new JsonObject
        {
            ["isPaused"]  = _time.IsPaused,
            ["inPreview"] = _preview.IsInPreviewMode,
            ["totalTime"] = _time.TotalTime,
            ["timeScale"] = _time.TimeScale,
        };

        /// <summary><c>POST /sim/play</c> — explicit resume; idempotent (never blind-toggles) (main thread).</summary>
        public JsonNode Play()
        {
            // IsPaused is true when not-in-preview OR paused; toggling moves toward "running".
            if (_time.IsPaused)
                _time.TogglePlayPause();
            return GetSimState();
        }

        /// <summary><c>POST /sim/pause</c> — explicit pause; idempotent (main thread).</summary>
        public JsonNode Pause()
        {
            if (!_time.IsPaused)
                _time.TogglePlayPause();
            return GetSimState();
        }

        /// <summary><c>POST /sim/step {count?}</c> — discrete single-step(s) (main thread).</summary>
        public JsonNode Step(int count = 1)
        {
            if (count < 1) count = 1;
            for (int i = 0; i < count; i++)
                _time.Step();
            return GetSimState();
        }

        /// <summary><c>POST /sim/timescale {scale}</c> (main thread).</summary>
        public JsonNode SetTimeScale(float scale)
        {
            _time.SetTimeScale(scale);
            return GetSimState();
        }

        /// <summary><c>POST /preview/enter {startPaused?}</c> (main thread).</summary>
        public JsonNode EnterPreview(bool startPaused = false)
        {
            if (!_preview.IsInPreviewMode)
                _preview.EnterPreviewMode(startPaused);
            return GetSimState();
        }

        /// <summary><c>POST /preview/exit</c> (main thread).</summary>
        public JsonNode ExitPreview()
        {
            if (_preview.IsInPreviewMode)
                _preview.ExitPreviewMode();
            return GetSimState();
        }

        // ── Group E — Scenario list / load / save ──────────────────────────────

        /// <summary><c>GET /scenarios</c> — available scenario names (thread-safe enough; main thread used).</summary>
        public JsonNode ListScenarios()
        {
            var arr = new JsonArray();
            foreach (var s in _editor.AvailableScenarios)
                arr.Add(s);
            return arr;
        }

        /// <summary>
        /// <c>POST /scenario/load {name, waitForReady?}</c>. Initiates the load. When
        /// <paramref name="waitForReady"/> is false, returns immediately. The blocking wait
        /// (poll <c>ClusterStateUpdateEvent.CurrentState == OperatingEdit</c>) is performed by the
        /// caller via <see cref="PollClusterStateIsOperatingEdit"/> across kernel ticks (the job
        /// queue marshals one poll per drain); this method just kicks the load off.
        /// </summary>
        public void BeginLoadScenario(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));
            _editor.LoadScenarioByName(name);
        }

        /// <summary>
        /// Reads the orchestration bus for the latest cluster-state and returns true once it is
        /// <see cref="ClusterState.OperatingEdit"/>. <b>Must run on the main thread</b> — it both
        /// drives <c>IEditorLogic.Update()</c> (which consumes the orchestration events and advances
        /// the load state machine) and inspects the resulting state. <c>LoadedScenarioName</c> is
        /// deliberately NOT used as the completion signal (set at frame 0).
        /// </summary>
        public bool PollClusterStateIsOperatingEdit() => CurrentClusterState() == ClusterState.OperatingEdit;

        /// <summary><c>POST /scenario/save {name}</c> — persists the authored world (main thread).</summary>
        public JsonNode SaveScenario(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));
            _editor.SaveScenarioAs(name);
            return new JsonObject { ["saved"] = name };
        }

        // ── Group F — Commands + discovery + spawn ─────────────────────────────

        /// <summary>
        /// <c>GET /commands</c> — enumerate publishable FDP event types with field schemas.
        /// Thread-safe (registry is read-only after boot).
        /// </summary>
        public JsonNode ListCommands()
        {
            var types = EventType.GetAllRegistered();
            var arr   = new JsonArray();
            foreach (var t in types)
            {
                var fields = JsonShapeDescriber.Describe(t);
                var fa = new JsonArray();
                foreach (var f in fields)
                    fa.Add(new JsonObject { ["name"] = f.Name, ["type"] = f.Type });
                arr.Add(new JsonObject
                {
                    ["name"]   = t.Name,
                    ["fields"] = fa,
                });
            }
            return arr;
        }

        /// <summary>
        /// <c>GET /components</c> — enumerate registered ECS component types with field schemas.
        /// Thread-safe (registry is read-only after boot).
        /// </summary>
        public JsonNode ListComponents()
        {
            var types = ComponentTypeRegistry.GetAllTypes();
            var arr   = new JsonArray();
            foreach (var t in types)
            {
                var fields = JsonShapeDescriber.Describe(t);
                var fa = new JsonArray();
                foreach (var f in fields)
                    fa.Add(new JsonObject { ["name"] = f.Name, ["type"] = f.Type });
                arr.Add(new JsonObject
                {
                    ["name"]   = t.Name,
                    ["fields"] = fa,
                });
            }
            return arr;
        }

        /// <summary>
        /// <c>POST /entities/command {eventType, payload, wait?}</c> — deserialize the payload
        /// to the named event CLR type and publish on <c>_world.Bus</c>.
        ///
        /// <para>Wait-gating: if <paramref name="wait"/> is <c>true</c> and time is advancing
        /// (<c>InPreview &amp;&amp; !IsPaused</c>), blocks until a correlated ack
        /// (<c>MissionControlAckEvent</c> by <c>RequestId</c>) arrives or the timeout expires.
        /// Otherwise returns immediately with <c>awaited:false</c>.</para>
        ///
        /// <para>Must run on the main thread.</para>
        /// </summary>
        /// <returns>JsonNode with { awaited, reason? } or null on 400 (unknown type).</returns>
        public (JsonNode? result, string? error) SendCommand(string eventTypeName, JsonNode? payload, bool wait)
        {
            // Resolve event type by name across all registered types.
            var registeredTypes = EventType.GetAllRegistered();
            Type? clrType = registeredTypes.FirstOrDefault(t =>
                string.Equals(t.Name, eventTypeName, StringComparison.OrdinalIgnoreCase));

            if (clrType is null)
                return (null, $"Unknown eventType: '{eventTypeName}'");

            // Determine if time is advancing (InPreview && !Paused).
            bool timeAdvancing = _preview.IsInPreviewMode && !_time.IsPaused;

            // Deserialize the payload JSON to the target CLR type.
            // We use System.Text.Json (default options) for the payload fields since that's
            // what callers provide; the inspector-grade path is for output only.
            object? evt = null;
            if (payload != null)
            {
                try
                {
                    var json = payload.ToJsonString();
                    evt = JsonSerializer.Deserialize(json, clrType);
                }
                catch (Exception ex)
                {
                    return (null, $"Failed to deserialize payload for '{eventTypeName}': {ex.Message}");
                }
            }
            else
            {
                // Create a default instance.
                try { evt = Activator.CreateInstance(clrType); }
                catch { evt = null; }
            }

            // Publish via the appropriate bus method (unmanaged struct → Publish, managed → PublishManaged).
            try
            {
                PublishEventObject(clrType, evt);
            }
            catch (Exception ex)
            {
                return (null, $"Failed to publish event '{eventTypeName}': {ex.Message}");
            }

            // Wait-gating: only meaningful when time is advancing.
            if (!wait || !timeAdvancing)
            {
                return (new JsonObject
                {
                    ["awaited"] = false,
                    ["reason"]  = "sim not running",
                }, null);
            }

            // Time is advancing and wait==true: attempt correlated ack wait (best-effort).
            // For now: publish + return awaited:false with reason "ack-wait not supported for this type".
            // The MissionControlAckEvent correlated path is logged as debt (ADA-04-D01) —
            // it requires polling the bus across kernel ticks which the current synchronous
            // main-thread job does not support without a multi-tick continuation.
            return (new JsonObject
            {
                ["awaited"] = false,
                ["reason"]  = "ack-wait not yet supported; event published",
            }, null);
        }

        /// <summary>
        /// <c>POST /entities/spawn {tkbType, transform?, components?, attributesJson?}</c>
        /// — builds and publishes a <see cref="SpawnEntityCommand"/>. Returns <c>awaited</c>
        /// per the wait rule. Must run on the main thread.
        /// </summary>
        public JsonNode SpawnEntity(
            long     tkbType,
            JsonNode? transform      = null,
            JsonNode? components     = null,
            string?  attributesJson = null)
        {
            var cmd = new SpawnEntityCommand
            {
                TkbType             = tkbType,
                NetworkId           = 0,          // 0 = allocate a new ID
                OwnerNodeId         = 0,
                InitType            = ReliableInitType.None,
                InitialAttributesJson = attributesJson,
            };

            // Parse optional transform.
            if (transform != null)
            {
                try
                {
                    var simTransform = JsonSerializer.Deserialize<SimTransform>(transform.ToJsonString());
                    cmd.InitialTransform = simTransform;
                }
                catch { /* ignore malformed transform */ }
            }

            // Parse optional extra components list (array of { type, data } or typed objects).
            if (components != null && components is JsonArray compArr && compArr.Count > 0)
            {
                cmd.InitialComponents = new List<object>();
                foreach (var item in compArr)
                {
                    if (item is null) continue;
                    // Support { "type": "TypeName", "data": {...} } format.
                    var typeName = item["type"]?.GetValue<string>();
                    var data     = item["data"];
                    if (typeName != null)
                    {
                        var compType = ComponentTypeRegistry.GetAllTypes()
                            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
                        if (compType != null && data != null)
                        {
                            try
                            {
                                var compObj = JsonSerializer.Deserialize(data.ToJsonString(), compType);
                                if (compObj != null)
                                    cmd.InitialComponents.Add(compObj);
                            }
                            catch { /* ignore undeserializable component */ }
                        }
                    }
                }
            }

            _world.Bus.PublishManaged(cmd);

            bool timeAdvancing = _preview.IsInPreviewMode && !_time.IsPaused;
            return new JsonObject
            {
                ["spawned"]  = true,
                ["tkbType"]  = tkbType,
                ["awaited"]  = false,
                ["reason"]   = timeAdvancing ? null : (JsonNode?)"sim not running",
            };
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private ClusterState CurrentClusterState() => _clusterState();

        /// <summary>
        /// Publishes an event object to <c>_world.Bus</c> using reflection to call the
        /// appropriate generic <c>Publish&lt;T&gt;</c> / <c>PublishManaged&lt;T&gt;</c>.
        /// Unmanaged value types → <c>Publish</c>; everything else → <c>PublishManaged</c>.
        /// </summary>
        private void PublishEventObject(Type clrType, object? evt)
        {
            if (clrType.IsValueType)
            {
                // Unmanaged struct path — call Bus.Publish<T>(evt) via reflection.
                var method = typeof(FdpEventBus)
                    .GetMethod(nameof(FdpEventBus.Publish), BindingFlags.Public | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                // Box the value if null (create default).
                var value = evt ?? Activator.CreateInstance(clrType)!;
                method.Invoke(_world.Bus, new[] { value });
            }
            else
            {
                // Managed class/struct path — call Bus.PublishManaged<T>(evt) via reflection.
                var method = typeof(FdpEventBus)
                    .GetMethod(nameof(FdpEventBus.PublishManaged), BindingFlags.Public | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                var value = evt ?? Activator.CreateInstance(clrType)!;
                method.Invoke(_world.Bus, new[] { value });
            }
        }

        private static string? ExtractEntityName(EntityStateDumpDto dump)
        {
            if (dump.Components.TryGetValue("EntityInfo", out var infoObj) &&
                infoObj is JsonElement je && je.ValueKind == JsonValueKind.Object &&
                je.TryGetProperty("Name", out var nameEl) &&
                nameEl.ValueKind == JsonValueKind.String)
            {
                return nameEl.GetString();
            }
            return null;
        }

        private static JsonNode DumpToJsonNode(EntityStateDumpDto dump)
        {
            // Re-serialize the DTO through the relaxed (non-camel) options and parse into a
            // JsonNode so it embeds verbatim. The Components values are already JsonElements
            // produced by the serializer-injected extraction path.
            var json = JsonSerializer.Serialize(dump, FdpJsonOptionsRegistry.DefaultRelaxed);
            return JsonNode.Parse(json)!;
        }
    }
}
