using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.NED.Messages;
using Hrot.Orchestrator.Translators.Payloads;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NedClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpClusterState = FDP.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator.Translators;

/// <summary>
/// Anti-Corruption Layer translator for the cluster-level orchestration traffic.
/// <para>Ingress: reads <see cref="ClusterOpRequest"/> from DDS, deserialises
/// <c>PayloadJson</c>, and publishes typed intent events on the bus.</para>
/// <para>Egress: drains <see cref="ClusterOpCompletedEvent"/> from the bus and
/// writes <see cref="ClusterOpStatus"/> to DDS.</para>
/// </summary>
public sealed class ClusterOpMasterTranslator
{
    private readonly DdsReader<ClusterOpRequest>  _requestReader;
    private readonly DdsWriter<ClusterOpStatus>   _statusWriter;
    private readonly FdpEventBus                  _bus;
    private readonly JsonSerializerOptions        _jsonOptions;

    /// <summary>Initialises a new <see cref="ClusterOpMasterTranslator"/>.</summary>
    public ClusterOpMasterTranslator(
        DdsReader<ClusterOpRequest>  requestReader,
        DdsWriter<ClusterOpStatus>   statusWriter,
        FdpEventBus                  bus,
        JsonSerializerOptions?       jsonOptions = null)
    {
        _requestReader = requestReader ?? throw new ArgumentNullException(nameof(requestReader));
        _statusWriter  = statusWriter  ?? throw new ArgumentNullException(nameof(statusWriter));
        _bus           = bus           ?? throw new ArgumentNullException(nameof(bus));
        _jsonOptions   = jsonOptions ?? OrchestrationJsonOptions.Default;
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
        foreach (var ev in _bus.ConsumeManaged<ClusterOpCompletedEvent>())
        {
            _statusWriter.Write(new ClusterOpStatus
            {
                RequestId  = ev.RequestId,
                StatusCode = ev.StatusCode,
                ResultJson = ev.ResultPayload is string s ? s : string.Empty,
            });
        }

        // ── Egress: Bus StorageOpCompletedEvent → DDS ClusterOpStatus ────────
        foreach (var ev in _bus.ConsumeManaged<StorageOpCompletedEvent>())
        {
            _statusWriter.Write(new ClusterOpStatus
            {
                RequestId  = ev.RequestId,
                StatusCode = ev.StatusCode,
                ResultJson = string.Empty,
            });
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
                _bus.PublishManaged(new TransitionStateIntent
                {
                    TransactionId   = req.RequestId,
                    TargetState     = (FdpClusterState)(int)dto.TargetState.Value,
                    ScenarioId      = dto.ScenarioId,
                    ExerciseId      = dto.ExerciseId,
                    TimeMode        = dto.TimeMode,
                    TargetWallTicks = 0,
                });
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
                SeekReplayPayloadDto? seekDto = TryDeserialize<SeekReplayPayloadDto>(req.PayloadJson);
                _bus.PublishManaged(new SeekReplayIntent
                {
                    RequestId       = req.RequestId,
                    TargetWallTicks = seekDto?.TargetWallTicks ?? 0,
                });
                break;
            }

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
                    ExerciseId = archDto?.ExerciseId,
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
                    ExerciseId = impDto?.ExerciseId,
                });
                break;
            }

            case NedClusterOpType.SaveScenario:
            {
                _bus.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.SaveScenario,
                    ExerciseId = null,
                });
                break;
            }

            case NedClusterOpType.TakeCheckpoint:
            {
                _bus.PublishManaged(new TakeCheckpointIntent
                {
                    RequestId = req.RequestId,
                });
                break;
            }

            case NedClusterOpType.LoadZone:
            {
                ArchivePayloadDto? zoneDto = TryDeserialize<ArchivePayloadDto>(req.PayloadJson);
                _bus.PublishManaged(new LoadZoneIntent
                {
                    RequestId = req.RequestId,
                    ZoneId    = zoneDto?.ExerciseId,
                });
                break;
            }

            // Time control and other ops are not translated here — handled by
            // HandleClusterOpRequest injection in ClusterMaster if needed.
            default:
                break;
        }
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
