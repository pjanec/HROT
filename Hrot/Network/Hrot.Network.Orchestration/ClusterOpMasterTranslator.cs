using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.NED.Messages;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NedClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using Fdp.Core.Serialization;

namespace Hrot.Network.Orchestration;

/// <summary>
/// Anti-Corruption Layer translator for the cluster-level orchestration traffic.
/// <para>Ingress: reads <see cref="ClusterOpRequest"/> from DDS, deserialises
/// <c>PayloadJson</c>, and publishes typed intent events on the bus.</para>
/// <para>Egress: drains <see cref="ClusterOpCompletedEvent"/> from the bus and
/// writes <see cref="ClusterOpStatus"/> to DDS.</para>
/// </summary>
public sealed class ClusterOpMasterTranslator
{
    private readonly DdsReader<ClusterOpRequest>    _requestReader;
    private readonly DdsWriter<ClusterOpStatus>     _statusWriter;
    private readonly DdsWriter<AssetInventoryTopic>? _inventoryWriter;
    private readonly DdsWriter<ClusterStateTopic>?   _clusterStateWriter;
    private readonly DdsWriter<ReplaySessionTopic>?  _sessionWriter;   // ⭐ CE-3329 rows 32, 45 — the session layer
    private readonly DdsWriter<GeoReference>?       _geoReferenceWriter;   // ⭐ AQ107 C — the NED wire's frame origin
    private readonly FdpEventBus                    _bus;
    private readonly JsonSerializerOptions          _jsonOptions;

    /// <summary>Initialises a new <see cref="ClusterOpMasterTranslator"/>.</summary>
    public ClusterOpMasterTranslator(
        DdsReader<ClusterOpRequest>    requestReader,
        DdsWriter<ClusterOpStatus>     statusWriter,
        FdpEventBus                    bus,
        JsonSerializerOptions?         jsonOptions = null,
        DdsWriter<AssetInventoryTopic>? inventoryWriter = null,
        DdsWriter<ClusterStateTopic>?   clusterStateWriter = null,
        DdsWriter<ReplaySessionTopic>?  sessionWriter = null,
        DdsWriter<GeoReference>?        geoReferenceWriter = null)
    {
        _requestReader     = requestReader     ?? throw new ArgumentNullException(nameof(requestReader));
        _statusWriter      = statusWriter      ?? throw new ArgumentNullException(nameof(statusWriter));
        _bus               = bus               ?? throw new ArgumentNullException(nameof(bus));
        _jsonOptions       = jsonOptions       ?? OrchestrationJsonOptions.Default;
        _inventoryWriter   = inventoryWriter;
        _clusterStateWriter = clusterStateWriter;
        _sessionWriter      = sessionWriter;
        _geoReferenceWriter = geoReferenceWriter;
    }

    /// <summary>Processes one frame: ingests DDS requests and publishes completed statuses.</summary>
    public void Tick()
    {
        // ── Ingress: DDS ClusterOpRequest → Bus typed intents ────────────────
        using var scope = _requestReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            ProcessRequest(sample.Data);
        }

        // ── Egress: Bus ClusterOpCompletedEvent → DDS ClusterOpStatus ────────
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            _statusWriter.Write(new ClusterOpStatus
            {
                RequestId  = ev.RequestId,
                StatusCode = (int)ev.StatusCode,
                ResultJson = ev.ResultPayload is null ? string.Empty
                    : ev.ResultPayload is string s ? s
                    : JsonSerializer.Serialize(ev.ResultPayload, ev.ResultPayload.GetType(), _jsonOptions),
            });
        }

        // ── Egress: Bus StorageOpCompletedEvent → DDS ClusterOpStatus ────────
        foreach (var ev in _bus.ReadManaged<StorageOpCompletedEvent>())
        {
            _statusWriter.Write(new ClusterOpStatus
            {
                RequestId  = ev.RequestId,
                StatusCode = (int)ev.StatusCode,
                ResultJson = string.Empty,
            });
        }

        // ── Egress: Bus AssetInventoryUpdateEvent → DDS AssetInventoryTopic ──
        if (_clusterStateWriter != null)
        {
            foreach (var ev in _bus.ReadManaged<ClusterStateTransitionedEvent>())
            {
                _clusterStateWriter.Write(new ClusterStateTopic
                {
                    CurrentState     = (NedClusterState)(int)ev.NewStateId,
                    ExerciseId       = ev.ExerciseId,
                    SessionId        = ev.SessionId,          // ⭐ C2
                    TransactionEpoch = ev.TransactionEpoch,   // ⭐ C4 — telemetry
                    ReplayAxis       = (int)ev.ReplayAxis,    // ⭐ CE-3329 R-3058
                    RunNotRecorded   = ev.RunNotRecorded ? 1 : 0,   // ⭐ CE-4090 D14
                    RecordLiveRunsOff = ev.RecordLiveRunsOff ? 1 : 0,
                });
            }
        }

        // ── ⭐ CE-3329 — Egress: Bus ReplaySessionPublishedEvent → DDS ReplaySessionTopic (TransientLocal: a late joiner gets the newest)
        if (_sessionWriter != null)
            foreach (var ev in _bus.ReadManaged<ReplaySessionPublishedEvent>())
                if (ev.Session != null) _sessionWriter.Write(ReplaySessionWire.ToTopic(ev.Session));

        // ── ⭐ AQ107 C — Egress: Bus GeoReferencePublishedEvent → DDS GeoReference (retained: a late gateway gets the newest origin)
        if (_geoReferenceWriter != null)
            foreach (var ev in _bus.ReadManaged<GeoReferencePublishedEvent>())
                _geoReferenceWriter.Write(new GeoReference
                {
                    OriginLatitudeDeg  = ev.OriginLatitudeDeg,
                    OriginLongitudeDeg = ev.OriginLongitudeDeg,
                    OriginAltitudeM    = ev.OriginAltitudeM,
                    Frame              = NedWire.FrameEnuTangentPlane,
                    TerrainName        = ev.TerrainName ?? string.Empty,
                    Generation         = ev.Generation,
                    ProtocolVersion    = NedWire.ProtocolVersion,
                });

        if (_inventoryWriter != null)
        {
            foreach (var ev in _bus.ReadManaged<AssetInventoryUpdateEvent>())
            {
                _inventoryWriter.Write(new AssetInventoryTopic
                {
                    NodeId                       = 0,
                    LocalScenariosJson           = System.Text.Json.JsonSerializer.Serialize(ev.LocalScenarios, FdpJsonOptionsRegistry.DefaultRelaxed),
                    LocalExercisesJson           = System.Text.Json.JsonSerializer.Serialize(ev.LocalExercises, FdpJsonOptionsRegistry.DefaultRelaxed),
                    ArchivedExercisesJson        = System.Text.Json.JsonSerializer.Serialize(ev.ArchivedExercises, FdpJsonOptionsRegistry.DefaultRelaxed),
                    UnarchivedLocalExercisesJson = System.Text.Json.JsonSerializer.Serialize(ev.UnarchivedLocalExercises, FdpJsonOptionsRegistry.DefaultRelaxed),
                });
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ProcessRequest(ClusterOpRequest req)
    {
        switch (req.OperationType)
        {
            case NedClusterOpType.TransitionState:
            {
                TransitionPayloadDto? dto = TryDeserialize<TransitionPayloadDto>(req.PayloadJson);
                if (dto?.TargetState is null)
                {
                    WriteError(req.RequestId, (int)NedStatusCode.ValidationFailed);
                    break;
                }
                _bus.PublishManaged(dto.ToIntent(req.RequestId));   // ⭐ R-3055 — the ONE decoding (TargetWallTicks included)
                break;
            }

            case NedClusterOpType.ManageEpisode:
            {
                ManageEpisodePayloadDto? epDto = TryDeserialize<ManageEpisodePayloadDto>(req.PayloadJson);
                if (epDto?.EpisodeId is null)
                {
                    WriteError(req.RequestId, (int)NedStatusCode.ValidationFailed);
                    break;
                }
                _bus.PublishManaged(new ManageEpisodeIntent
                {
                    TransactionId = req.RequestId,
                    IsStart       = epDto.IsStart,
                    EpisodeId     = epDto.EpisodeId.Value,
                    ScenarioId    = epDto.ScenarioId,
                });
                break;
            }

            case NedClusterOpType.ReplaySeek:
            {
                _bus.PublishManaged(SeekReplayPayloadDto.Decode(req.RequestId, req.PayloadJson));   // ⭐ CE-3329 — the one decoding
                break;
            }

            case NedClusterOpType.SetReplayAxis:   // ⭐ CE-3329 R-3058
                _bus.PublishManaged(SetReplayAxisPayloadDto.Decode(req.RequestId, req.PayloadJson));
                break;

            case NedClusterOpType.SetRunSettings:   // ⭐ CE-4090 D14
                _bus.PublishManaged(SetRunSettingsPayloadDto.Decode(req.RequestId, req.PayloadJson));
                break;

            case NedClusterOpType.SetReplayLoop:   // ⭐ CE-3329 §12n
                _bus.PublishManaged(SetReplayLoopPayloadDto.Decode(req.RequestId, req.PayloadJson));
                break;

            case NedClusterOpType.CancelOperation:
            {
                _bus.PublishManaged(new CancelOperationIntent
                {
                    TargetRequestId = req.RequestId,
                });
                break;
            }

            case NedClusterOpType.ExportArchive:
            {
                ArchivePayloadDto? archDto = TryDeserialize<ArchivePayloadDto>(req.PayloadJson);
                _bus.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.Export,
                    ExerciseId = archDto?.ExerciseId ?? Guid.Empty,
                });
                break;
            }

            case NedClusterOpType.DeleteExercise:   // ⭐ CE-4090 D15
            {
                ArchivePayloadDto? delDto = TryDeserialize<ArchivePayloadDto>(req.PayloadJson);
                _bus.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.Delete,
                    ExerciseId = delDto?.ExerciseId ?? Guid.Empty,
                });
                break;
            }

            case NedClusterOpType.ImportArchive:
            {
                ArchivePayloadDto? impDto = TryDeserialize<ArchivePayloadDto>(req.PayloadJson);
                _bus.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.Import,
                    ExerciseId = impDto?.ExerciseId ?? Guid.Empty,
                });
                break;
            }

            // CE-278: NedClusterOpType.SaveScenario (=2) retired — no master-side case. A stray legacy
            // op=2 on the wire falls through to the default and is ignored/rejected rather than fanned out.

            // ⭐⭐ CE-277(c0, HTTP): a remote node's /scenario/save reached the master as this request. Reconstruct
            //    the intent WITH the relative name (carried as {"ScenarioName": ...} by the egress translator),
            //    so ClusterMaster.ProcessStorageOpIntent fans the JSON save out to every node. ⛔ Without the
            //    name the save would target null and produce nothing.
            case NedClusterOpType.SaveScenario:
            {
                string? scenarioName = null;
                if (!string.IsNullOrWhiteSpace(req.PayloadJson))
                {
                    try
                    {
                        var node = System.Text.Json.Nodes.JsonNode.Parse(req.PayloadJson);
                        scenarioName = node?["ScenarioName"]?.GetValue<string>();
                    }
                    catch (JsonException) { }
                }
                _bus.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId    = req.RequestId,
                    Operation    = StorageOpType.SaveScenario,
                    ExerciseId   = Guid.Empty,
                    ScenarioName = scenarioName,
                });
                break;
            }

            case NedClusterOpType.TakeCheckpoint:
            {
                _bus.PublishManaged(new TakeCheckpointIntent
                {
                    RequestId = req.RequestId,
                    Label     = ClusterMaster_LabelOf(req.PayloadJson),   // ⭐ CE-3299 B3
                });
                break;
            }

            case NedClusterOpType.CollectCheckpoint:   // ⭐ CE-3299 B3 (R-3044 "collect")
            {
                _bus.PublishManaged(new CollectCheckpointIntent
                {
                    RequestId = req.RequestId,
                    Label     = ClusterMaster_LabelOf(req.PayloadJson),
                });
                break;
            }

            case NedClusterOpType.RestoreCheckpoint:   // ⭐ CE-3299 B4
            {
                _bus.PublishManaged(new RestoreCheckpointIntent
                {
                    RequestId = req.RequestId,
                    Label     = ClusterMaster_LabelOf(req.PayloadJson),
                });
                break;
            }

            case NedClusterOpType.CheckUnsavedChanges:   // ⭐ CE-4093 D20
                _bus.PublishManaged(new CheckUnsavedIntent { RequestId = req.RequestId });
                break;

            case NedClusterOpType.LoadZone:
            {
                // ⭐ D2 — a purpose-built DTO. This arm used to deserialize ArchivePayloadDto and
                //   stringify its ExerciseId into ZoneId, which named the wrong domain and silently
                //   constrained zone ids to GUID shape.
                ZonePayloadDto? zoneDto = TryDeserialize<ZonePayloadDto>(req.PayloadJson);
                _bus.PublishManaged(new LoadZoneIntent
                {
                    RequestId = req.RequestId,
                    ZoneId    = zoneDto?.ZoneId,
                });
                break;
            }

            // Time control ops (HEXAG2-S010): publish typed intents to bus.
            // MasterSyncController.Update() drains them in Phase 3 after SwapBuffers.
            case NedClusterOpType.PauseTime:
                _bus.PublishManaged(new PauseTimeIntent());
                break;

            case NedClusterOpType.ResumeTime:
                _bus.PublishManaged(new ResumeTimeIntent());
                break;

            case NedClusterOpType.StepTime:
            {
                StepTimePayloadDto? dto = TryDeserialize<StepTimePayloadDto>(req.PayloadJson);
                float delta = dto != null && dto.FixedDelta > 0f ? dto.FixedDelta : 1f / 60f;
                _bus.PublishManaged(new StepTimeIntent { DeltaSeconds = delta });
                break;
            }

            case NedClusterOpType.SetTimeScale:
            {
                SetTimeScalePayloadDto? dto = TryDeserialize<SetTimeScalePayloadDto>(req.PayloadJson);
                float scale = dto != null && dto.TimeScale > 0f ? dto.TimeScale : 1f;
                _bus.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });
                break;
            }

            // ⭐ CE-3022 — the build op arrives from a remote panel (E4); without this arm it fell to the default.
            case NedClusterOpType.BuildTerrainAsset:
                _bus.PublishManaged(new BuildTerrainAssetIntent
                {
                    RequestId = req.RequestId,
                    Kinds     = TryDeserialize<TerrainAssetBuildPayloadDto>(req.PayloadJson)?.Kinds,
                });
                break;

            // ⭐ CE-3021 — publish / refresh (docs/DESIGN_Asset_Management.md §10 D7).
            case NedClusterOpType.PublishAssets:
            case NedClusterOpType.RefreshAssets:
            {
                AssetOpPayloadDto? dto = TryDeserialize<AssetOpPayloadDto>(req.PayloadJson);
                if (dto == null || string.IsNullOrWhiteSpace(dto.Kind))
                {
                    WriteError(req.RequestId, (int)NedStatusCode.ValidationFailed);
                    break;
                }
                _bus.PublishManaged(new AssetOpIntent
                {
                    RequestId = req.RequestId,
                    Refresh   = req.OperationType == NedClusterOpType.RefreshAssets,
                    Kind      = dto.Kind,
                    NodeId    = dto.NodeId,
                });
                break;
            }

            case NedClusterOpType.DumpDiagnostics:
            {
                _bus.PublishManaged(new ExecuteDiagnosticDumpIntent
                {
                    RequestId   = req.RequestId,
                    PayloadJson = req.PayloadJson ?? string.Empty,
                });
                break;
            }

            default:
                break;
        }
    }

    // ⭐ CE-3299 B3 — a checkpoint request's label: a JSON object's "label", else the payload text (the same rule as the master's).
    private static string? ClusterMaster_LabelOf(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        var text = payloadJson.Trim();
        if (!text.StartsWith("{")) return text.Trim('"');
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (string.Equals(p.Name, "label", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return p.Value.GetString();
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    private void WriteError(Guid requestId, int statusCode)
    {
        _statusWriter.Write(new ClusterOpStatus
        {
            RequestId  = requestId,
            StatusCode = statusCode,
            ResultJson = string.Empty,
        });
    }

    private T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, _jsonOptions); }
        catch { return null; }
    }

}
