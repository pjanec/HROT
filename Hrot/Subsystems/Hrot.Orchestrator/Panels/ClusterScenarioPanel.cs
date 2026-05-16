using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using Hrot.Orchestrator;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using ImGuiNET;
using FdpClusterOpType        = Fdp.Toolkit.Orchestration.ClusterOpType;
using FdpClusterState         = Fdp.Toolkit.Orchestration.ClusterState;
using TransitionStateIntent   = Fdp.Toolkit.Orchestration.TransitionStateIntent;
using ManageEpisodeIntent     = Fdp.Toolkit.Orchestration.ManageEpisodeIntent;
using ExecuteStorageOpIntent  = Fdp.Toolkit.Orchestration.ExecuteStorageOpIntent;
using StorageOpType           = Fdp.Toolkit.Orchestration.StorageOpType;
using TakeCheckpointIntent    = Fdp.Toolkit.Orchestration.TakeCheckpointIntent;
using SeekReplayIntent        = Fdp.Toolkit.Orchestration.SeekReplayIntent;
using CancelOperationIntent   = Fdp.Toolkit.Orchestration.CancelOperationIntent;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// ImGui scenario and episode control panel for the Orchestrator subsystem (CGF1-S0506).
///
/// <para>CQRS read-model: reads all cluster state from <see cref="ClusterUiCache"/>;
/// emits commands via <see cref="DdsWriter{T}"/>. No direct reference to
/// <see cref="ClusterMaster"/> or any local service.</para>
///
/// Renders: Bootstrap banner, Node Health table, Time Control, 2PC History,
/// Status Banner, Cluster Control, Checkpoint, Scenario, Replay, Episodes, Archive.
/// </summary>
public sealed class ClusterScenarioPanel
{
    // ── Dependencies ──────────────────────────────────────────────────────
    // One of these two is always non-null depending on construction path:
    //  - _master: Orchestrator's internal panel  — direct binding to ClusterMaster
    //  - _bus:    remote client (ExCon)          — sends commands over FdpEventBus
    private readonly ClusterMaster? _master;
    private readonly FdpEventBus?   _bus;
    private readonly ClusterUiCache _uiCache;

    // ── Helper: send a request via whichever channel is available ─────────
    private void SendRequest(ClusterOpRequest req)
    {
        if (_master != null)
        {
            _master.HandleClusterOpRequest(req);
            return;
        }

        // Bus path (remote/ExCon): publish typed intents consumed by ClusterOpEgressTranslator.
        // HEXAG2-S012: zero ClusterOpIntent references; each operation type is mapped directly.
        switch ((FdpClusterOpType)(int)req.OperationType)
        {
            case FdpClusterOpType.PauseTime:
                _bus!.PublishManaged(new PauseTimeIntent());
                break;

            case FdpClusterOpType.ResumeTime:
                _bus!.PublishManaged(new ResumeTimeIntent());
                break;

            case FdpClusterOpType.StepTime:
            {
                StepTimePayloadDto? dto = null;
                if (!string.IsNullOrWhiteSpace(req.PayloadJson))
                {
                    try { dto = JsonSerializer.Deserialize<StepTimePayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default); }
                    catch { }
                }
                float delta = dto != null && dto.FixedDelta > 0f ? dto.FixedDelta : 1f / 60f;
                _bus!.PublishManaged(new StepTimeIntent { DeltaSeconds = delta });
                break;
            }

            case FdpClusterOpType.SetTimeScale:
            {
                SetTimeScalePayloadDto? dto = null;
                if (!string.IsNullOrWhiteSpace(req.PayloadJson))
                {
                    try { dto = JsonSerializer.Deserialize<SetTimeScalePayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default); }
                    catch { }
                }
                float scale = dto != null && dto.TimeScale > 0f ? dto.TimeScale : 1f;
                _bus!.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });
                break;
            }

            case FdpClusterOpType.TransitionState:
            {
                var intent = ParseTransitionStateIntent(req);
                _bus!.PublishManaged(intent);
                break;
            }

            case FdpClusterOpType.ManageEpisode:
            {
                var intent = ParseManageEpisodeIntent(req);
                _bus!.PublishManaged(intent);
                break;
            }

            case FdpClusterOpType.SaveScenario:
                _bus!.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.SaveScenario,
                    ExerciseId = ExtractGuidField(req.PayloadJson),
                });
                break;

            case FdpClusterOpType.ExportArchive:
                _bus!.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.Export,
                    ExerciseId = ExtractGuidField(req.PayloadJson),
                });
                break;

            case FdpClusterOpType.ImportArchive:
                _bus!.PublishManaged(new ExecuteStorageOpIntent
                {
                    RequestId  = req.RequestId,
                    Operation  = StorageOpType.Import,
                    ExerciseId = ExtractGuidField(req.PayloadJson),
                });
                break;

            case FdpClusterOpType.TakeCheckpoint:
                _bus!.PublishManaged(new TakeCheckpointIntent { RequestId = req.RequestId });
                break;

            case FdpClusterOpType.ReplaySeek:
            {
                long ticks = TryParseWallTicks(req.PayloadJson);
                _bus!.PublishManaged(new SeekReplayIntent { RequestId = req.RequestId, TargetWallTicks = ticks });
                break;
            }

            case FdpClusterOpType.CancelOperation:
                _bus!.PublishManaged(new CancelOperationIntent
                {
                    TargetRequestId = Guid.TryParse(req.PayloadJson, out var tid) ? tid : req.RequestId,
                });
                break;
        }
    }

    // ── Payload parsing helpers (bus path only) ────────────────────────────

    private static long TryParseWallTicks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        if (long.TryParse(json, out var v)) return v;
        try
        {
            var dto = JsonSerializer.Deserialize<SeekReplayPayloadDto>(json, OrchestrationJsonOptions.Default);
            return dto?.TargetWallTicks ?? 0;
        }
        catch { }
        return 0;
    }

    private static Guid ExtractGuidField(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Guid.Empty;
        try
        {
            var dto = JsonSerializer.Deserialize<ArchivePayloadDto>(json, OrchestrationJsonOptions.Default);
            return dto?.ExerciseId ?? Guid.Empty;
        }
        catch { }
        return Guid.Empty;
    }

    private static TransitionStateIntent ParseTransitionStateIntent(ClusterOpRequest req)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<TransitionPayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default);
            return new TransitionStateIntent
            {
                TransactionId = req.RequestId,
                TargetState   = dto?.TargetState.HasValue == true ? (FdpClusterState)(int)dto.TargetState.Value : default,
                ScenarioId    = dto?.ScenarioId,
                ExerciseId    = dto?.ExerciseId ?? Guid.Empty,
                TimeMode      = dto?.TimeMode,
            };
        }
        catch { }
        return new TransitionStateIntent { TransactionId = req.RequestId };
    }

    private static ManageEpisodeIntent ParseManageEpisodeIntent(ClusterOpRequest req)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ManageEpisodePayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default);
            return new ManageEpisodeIntent
            {
                TransactionId = req.RequestId,
                IsStart       = dto?.IsStart ?? false,
                EpisodeId     = dto?.EpisodeId ?? Guid.Empty,
                ScenarioId    = dto?.ScenarioId,
            };
        }
        catch { }
        return new ManageEpisodeIntent { TransactionId = req.RequestId };
    }

    // ── Adapter properties: use _master when present, else fall back to _uiCache ─
    private bool         IsBootstrapped    => _master?.BootstrapComplete     ?? _uiCache.IsBootstrapped;
    private bool         HasFlight         => _master?.HasInFlightTransaction ?? _uiCache.HasInFlightTransaction;
    private ClusterState EffectiveState    => _master?.CurrentClusterState     ?? _uiCache.CurrentState;
    private Guid         EffectiveExerciseId => _master?.ActiveExerciseId      ?? _uiCache.ActiveExerciseId;
    private DistributedTransaction? EffectiveActiveTx
        => _master?.ActiveTransaction ?? _uiCache.ActiveTransaction;
    private IReadOnlyList<DistributedTransaction> EffectiveTxHistory
        => (IReadOnlyList<DistributedTransaction>?)_master?.TransactionHistory ?? _uiCache.TxHistory;
    private IReadOnlyList<ClusterState> EffectiveReachable
        => _master?.GetReachableTargets() ?? _uiCache.ReachableTargets;
    private IReadOnlyCollection<Guid> EffectiveEpisodes
        => _uiCache.ActiveEpisodes;

    // ── Scenario section state ────────────────────────────────────────────
    private string _saveScenarioId  = string.Empty;

    // ── Asset combo state (S0504 / S0506) ────────────────────────────────
    private int _selectedLoadScenarioIdx = -1;
    private int _selectedExerciseIdx        = -1;
    private bool _startPaused = false;
    private int _selectedEpisodeIdx        = -1;

    // ── Replay section state ──────────────────────────────────────────────
    private float  _seekSliderValue = 0f;

    // ── Seek debounce (S0503) ─────────────────────────────────────────────
    private float _seekDebounceTimer = 0f;
    private bool  _seekPending       = false;

    // ── Archive Management state (S0505) ──────────────────────────────────
    private int  _selectedArchiveIdx     = -1;
    private int  _selectedUnarchivedIdx  = -1;
    private Guid _activeArchiveOpId      = Guid.Empty;

    // ── Step size for deterministic stepping ─────────────────────────────
    private float _stepDeltaSeconds = 1f / 60f;

    // ── Child window sizes ────────────────────────────────────────────────
    private static readonly Vector2 AutoSize = Vector2.Zero;

    /// <param name="clusterMaster">ClusterMaster — source of true internal state and command target (Orchestrator path).</param>
    /// <param name="uiCache">Network cache — used for asset inventory and time data.</param>
    public ClusterScenarioPanel(ClusterMaster clusterMaster, ClusterUiCache uiCache)
    {
        _master  = clusterMaster ?? throw new ArgumentNullException(nameof(clusterMaster));
        _uiCache = uiCache       ?? throw new ArgumentNullException(nameof(uiCache));
        _bus     = null;
    }

    /// <param name="bus">Event bus for publishing typed intent commands (remote/ExCon path).</param>
    /// <param name="uiCache">Network projection cache to read cluster state from.</param>
    public ClusterScenarioPanel(FdpEventBus bus, ClusterUiCache uiCache)
    {
        _bus     = bus     ?? throw new ArgumentNullException(nameof(bus));
        _uiCache = uiCache ?? throw new ArgumentNullException(nameof(uiCache));
        _master  = null;
    }

    /// <summary>Advances the seek debounce timer. Call once per frame from the subsystem Update().</summary>
    public void Update(float dt)
    {
        if (!_seekPending) return;
        _seekDebounceTimer -= dt;
        if (_seekDebounceTimer > 0f) return;

        _seekPending = false;
        long wallTicks = (long)(_seekSliderValue * 10_000_000L);
        SendRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = JsonSerializer.Serialize(new SeekReplayPayloadDto(TargetWallTicks: wallTicks), OrchestrationJsonOptions.Default),
        });
    }

    /// <summary>
    /// Renders all panel sections. Must be called from within an active ImGui frame.
    /// </summary>
    /// <param name="cache">Cache snapshot for this frame (same instance as constructed with).</param>
    /// <param name="disableAll">When <c>true</c>, all interactive controls are disabled.</param>
    public void Render()
    {
        // Compute disable flag from ClusterMaster's true internal state.
        // HasInFlightTransaction is reset to false immediately after the fan-out
        // completes, so buttons re-enable as soon as the transition is dispatched.
        bool disableAll = !IsBootstrapped || HasFlight;

        // ── Bootstrap banner ───────────────────────────────────────────────
        if (!IsBootstrapped)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));
            ImGui.TextWrapped("Cluster not bootstrapped — waiting for mandatory nodes.");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        // ── Node Health table ──────────────────────────────────────────────
        RenderNodeHealthTable();

        // ── Time Control section (S0503) ───────────────────────────────────
        RenderTimeControl(disableAll);

        // ── 2PC History table (S0501) ──────────────────────────────────────
        RenderTxHistory();

        ImGui.Separator();

        // ── 1. Status Banner (always enabled) ─────────────────────────────
        RenderStatusBanner();

        // ── 2. Cluster Control ───────────────────────────────────────────────
        RenderClusterControl(disableAll);

        // ── 3. Checkpoint ─────────────────────────────────────────────────
        RenderCheckpointSection(EffectiveState, disableAll);

        // ── 4. Scenario ────────────────────────────────────────────────────
        RenderScenarioSection(disableAll);

        // ── 5. Replay ──────────────────────────────────────────────────────
        RenderReplaySection(disableAll);

        // ── 6. Episodes ─────────────────────────────────────────────────────
        RenderEpisodesSection(disableAll);

        // ── 7. Archive Management ──────────────────────────────────────────
        RenderArchiveSection(disableAll);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sections moved from OrchestratorSubsystem.DrawUI()
    // ─────────────────────────────────────────────────────────────────────────

    private void RenderNodeHealthTable()
    {
        if (!ImGui.CollapsingHeader("Node Health", ImGuiTreeNodeFlags.DefaultOpen)) return;

        double nowSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        if (ImGui.BeginTable("NodeHealth", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("NodeId");
            ImGui.TableSetupColumn("Subsystem");
            ImGui.TableSetupColumn("Last HB (ms ago)");
            ImGui.TableSetupColumn("State");
            ImGui.TableSetupColumn("CPU %");
            ImGui.TableSetupColumn("RAM (MB)");
            ImGui.TableHeadersRow();

            if (_master != null)
            {
                foreach (var kv in _master.NodeRoster.ActiveNodes)
                {
                    var p     = kv.Value;
                    var msAgo = p.LastHeartbeatUtcSeconds > 0
                        ? (int)((nowSec - p.LastHeartbeatUtcSeconds) * 1000.0)
                        : -1;
                    var ramMb = p.RamUsedBytes / (1024.0 * 1024.0);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(p.NodeId.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(p.SubsystemName ?? "—");
                    ImGui.TableNextColumn(); ImGui.Text(msAgo >= 0 ? msAgo.ToString() : "—");
                    ImGui.TableNextColumn(); ImGui.Text(p.LocalClusterState.ToString());
                    ImGui.TableNextColumn(); ImGui.Text($"{p.CpuUsagePercent:F1}");
                    ImGui.TableNextColumn(); ImGui.Text($"{ramMb:F1}");
                }
            }
            else
            {
                foreach (var kv in _uiCache.ActiveNodes)
                {
                    var hb    = kv.Value;
                    long lastSeenMs = _uiCache.GetNodeLastSeenMs(hb.NodeId);
                    var msAgo = lastSeenMs > 0
                        ? (int)((nowSec * 1000.0) - lastSeenMs)
                        : -1;
                    var ramMb = hb.RamUsedBytes / (1024.0 * 1024.0);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(hb.NodeId.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(hb.SubsystemName ?? "—");
                    ImGui.TableNextColumn(); ImGui.Text(msAgo >= 0 ? msAgo.ToString() : "—");
                    ImGui.TableNextColumn(); ImGui.Text(hb.LocalClusterState.ToString());
                    ImGui.TableNextColumn(); ImGui.Text($"{hb.CpuUsagePercent:F1}");
                    ImGui.TableNextColumn(); ImGui.Text($"{ramMb:F1}");
                }
            }
            ImGui.EndTable();
        }
    }

    private void RenderTimeControl(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen)) return;

        long   wallTicks   = DateTimeOffset.UtcNow.Ticks;
        string wallTimeStr = new DateTime(wallTicks, DateTimeKind.Utc).ToString("HH:mm:ss.fff");
        ImGui.Text($"Wall Time: {wallTimeStr}");

        var   simSpan  = TimeSpan.FromSeconds(_uiCache.MasterSimTime);
        string simStr  = $"{(int)simSpan.TotalHours:D2}:{simSpan.Minutes:D2}:{simSpan.Seconds:D2}.{simSpan.Milliseconds:D3}";
        string status  = _uiCache.IsPaused ? "PAUSED" : "RUNNING";
        ImGui.Text($"Sim Time: {simStr} [{status}]");

        if (disableAll) ImGui.BeginDisabled();

        float timeScale = _uiCache.MasterTimeScale;
        bool isPaused = _uiCache.IsPaused;
        if (ImGui.Button(isPaused ? "Resume##OrcResume" : "Pause##OrcPause"))
            SendRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = isPaused ? ClusterOpType.ResumeTime : ClusterOpType.PauseTime,
                PayloadJson   = string.Empty,
            });

        ImGui.SameLine();
        if (!isPaused) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputFloat("Step (s)##OrcStepDelta", ref _stepDeltaSeconds, 0f, 0f, "%.4f");
        if (_stepDeltaSeconds <= 0f) _stepDeltaSeconds = 1f / 60f;
        ImGui.SameLine();
        if (ImGui.Button("Step##OrcStep"))
        {
            SendRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.StepTime,
                PayloadJson   = JsonSerializer.Serialize(
                    new StepTimePayloadDto(FixedDelta: _stepDeltaSeconds),
                    OrchestrationJsonOptions.Default),
            });
        }
        if (!isPaused) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderFloat("Speed##OrcSpeed", ref timeScale, 0.1f, 10.0f, "%.1fx"))
            SendRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.SetTimeScale,
                PayloadJson   = JsonSerializer.Serialize(
                    new SetTimeScalePayloadDto(TimeScale: timeScale),
                    OrchestrationJsonOptions.Default),
            });

        if (disableAll) ImGui.EndDisabled();
    }

    private void RenderTxHistory()
    {
        if (!ImGui.CollapsingHeader("2PC History")) return;

        var history    = EffectiveTxHistory;
        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        if (ImGui.BeginTable("TxHistory", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0, rowHeight * 11.5f)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("TransactionId");
            ImGui.TableSetupColumn("Transition");
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("ACKs");
            ImGui.TableSetupColumn("Payload");
            ImGui.TableHeadersRow();

            foreach (var tx in history)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                string shortId = tx.TransactionId.ToString();
                bool open = ImGui.TreeNodeEx(shortId, ImGuiTreeNodeFlags.SpanFullWidth);

                if (ImGui.BeginPopupContextItem($"ctx_{tx.TransactionId}"))
                {
                    string statusStr = tx.IsAborted ? "Aborted" : "OK";
                    string line = $"{tx.TransactionId} | {tx.SourceDsmState}->{tx.TargetDsmState} | {statusStr} | {tx.PayloadJson}";
                    if (ImGui.MenuItem("Copy line to clipboard"))
                        ImGui.SetClipboardText(line);
                    ImGui.EndPopup();
                }

                ImGui.TableNextColumn();
                ImGui.Text($"{tx.SourceDsmState} -> {tx.TargetDsmState}");

                ImGui.TableNextColumn();
                if (tx.IsAborted)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
                    ImGui.Text("Aborted");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1f));
                    ImGui.Text("OK");
                    ImGui.PopStyleColor();
                }

                ImGui.TableNextColumn();
                ImGui.Text(tx.NodeResponses.Count == 0 ? "-" : tx.NodeResponses.Values.Sum(d => d.Count).ToString());

                ImGui.TableNextColumn();
                ImGui.TextWrapped(tx.PayloadJson);
                if (!string.IsNullOrWhiteSpace(tx.PayloadJson) && ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(OrchestratorSubsystem.FormatPrettyJson(tx.PayloadJson));
                    ImGui.EndTooltip();
                }

                if (open)
                {
                    foreach (var nodeEntry in tx.NodeResponses)
                    {
                        foreach (var opEntry in nodeEntry.Value)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.TreeNodeEx($"-> Node {nodeEntry.Key} [{opEntry.Key}]",
                                ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen |
                                ImGuiTreeNodeFlags.SpanFullWidth);
                            ImGui.TableNextColumn(); ImGui.Text("-");
                            ImGui.TableNextColumn(); ImGui.Text("-");
                            ImGui.TableNextColumn(); ImGui.Text("-");
                            ImGui.TableNextColumn();
                            ImGui.TextWrapped(opEntry.Value);
                        }
                    }
                    ImGui.TreePop();
                }
            }
            ImGui.EndTable();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sections from OrchestratorScenarioPanel (adapted to use ClusterUiCache)
    // ─────────────────────────────────────────────────────────────────────────

    private void RenderStatusBanner()
    {
        var activeTx     = EffectiveActiveTx;
        var hasInFlight  = HasFlight;
        var currentState = EffectiveState;
        var currentExerciseId = EffectiveExerciseId;

        if (ImGui.BeginChild("##OrcStatusBanner", new Vector2(-1, 74), ImGuiChildFlags.Borders))
        {
            if (hasInFlight && activeTx != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));
                ImGui.Text($"State: {activeTx.SourceDsmState} → {activeTx.TargetDsmState}");
                ImGui.PopStyleColor();
                ImGui.SameLine(); ImGui.Text("|"); ImGui.SameLine();
                ImGui.Text($"TX {activeTx.TransactionId.ToString()[..8]}... in flight");
            }
            else
            {
                ImGui.Text($"State: {currentState}");
                ImGui.SameLine(); ImGui.Text("|"); ImGui.SameLine();
                ImGui.Text(IsBootstrapped ? "idle" : "NOT BOOTSTRAPPED");
            }

            if (currentExerciseId != Guid.Empty)
                ImGui.Text($"Exercise Id: {currentExerciseId}");
        }
        ImGui.EndChild();
    }

    private void RenderClusterControl(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Cluster Control", ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (ImGui.BeginChild("##OrcClusterControl", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();
            ImGui.Checkbox("Start Paused (Live/Preview)##OrcStartPaused", ref _startPaused);
            ImGui.Spacing();

            var reachable = EffectiveReachable;
            if (reachable.Count == 0)
            {
                ImGui.TextDisabled("No reachable transitions from current state.");
            }
            else
            {
                foreach (var target in reachable)
                {
                    string? timeMode = null;
                    if (_startPaused && (target == ClusterState.OperatingLive || target == ClusterState.OperatingPreview))
                        timeMode = "Deterministic";

                    if (ImGui.Button(target.ToString()))
                        SendRequest(new ClusterOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = ClusterOpType.TransitionState,
                            PayloadJson   = JsonSerializer.Serialize(
                                new TransitionPayloadDto(TargetState: target, ScenarioId: null, ExerciseId: Guid.NewGuid(), TimeMode: timeMode),
                                OrchestrationJsonOptions.Default),
                        });
                    ImGui.SameLine();
                }
                ImGui.NewLine();
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderCheckpointSection(ClusterState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Checkpoint")) return;

        if (ImGui.BeginChild("##OrcCheckpoint", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            bool checkpointDisabled = disableAll || currentState != ClusterState.OperatingLive;
            if (checkpointDisabled) ImGui.BeginDisabled();

            if (ImGui.Button("Take Checkpoint"))
                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TakeCheckpoint,
                    PayloadJson   = string.Empty,
                });

            if (checkpointDisabled) ImGui.EndDisabled();

            if (currentState != ClusterState.OperatingLive)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(requires RunningLive)");
            }
        }
        ImGui.EndChild();
    }

    private void RenderScenarioSection(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Scenario")) return;

        if (ImGui.BeginChild("##OrcScenario", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            if (_selectedLoadScenarioIdx >= _uiCache.AvailableScenarios.Length)
                _selectedLoadScenarioIdx = -1;

            // Save Scenario
            ImGui.InputText("Save Scenario ID##OrcSaveId", ref _saveScenarioId, 128);
            ImGui.SameLine();
            if (ImGui.Button("Save Scenario##OrcBtn") && !string.IsNullOrWhiteSpace(_saveScenarioId))
                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.SaveScenario,
                    PayloadJson   = JsonSerializer.Serialize(
                        new ArchivePayloadDto(ExerciseId: Guid.TryParse(_saveScenarioId, out var g) ? g : Guid.Empty),
                        OrchestrationJsonOptions.Default),
                });

            ImGui.Spacing();

            // Load Scenario
            ImGui.Combo("Select Scenario##OrcLoadId", ref _selectedLoadScenarioIdx,
                _uiCache.AvailableScenarios, _uiCache.AvailableScenarios.Length);
            ImGui.SameLine();
            if (ImGui.Button("Load into Edit##OrcLoadEdit") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = _uiCache.AvailableScenarios[_selectedLoadScenarioIdx];
                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TransitionState,
                    PayloadJson   = JsonSerializer.Serialize(
                        new TransitionPayloadDto(TargetState: ClusterState.OperatingEdit, ScenarioId: scenId, ExerciseId: Guid.Empty, TimeMode: null),
                        OrchestrationJsonOptions.Default),
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Load into Live##OrcLoadLive") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = _uiCache.AvailableScenarios[_selectedLoadScenarioIdx];
                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TransitionState,
                    PayloadJson   = JsonSerializer.Serialize(
                        new TransitionPayloadDto(TargetState: ClusterState.OperatingLive, ScenarioId: scenId, ExerciseId: Guid.NewGuid(), TimeMode: _startPaused ? "Deterministic" : null),
                        OrchestrationJsonOptions.Default),
                });
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderReplaySection(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Replay")) return;

        if (ImGui.BeginChild("##OrcReplay", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            if (_selectedExerciseIdx >= _uiCache.AvailableExercises.Length) _selectedExerciseIdx = -1;

            // Load Replay
            var available = _uiCache.AvailableExercises;
            string preview = _selectedExerciseIdx >= 0 && _selectedExerciseIdx < available.Length
                ? FormatExerciseLabel(available[_selectedExerciseIdx])
                : "Select Exercise...";

            if (ImGui.BeginCombo("Select Exercise##OrcReplayId", preview))
            {
                for (int i = 0; i < available.Length; i++)
                {
                    bool isSelected = _selectedExerciseIdx == i;
                    string label = FormatExerciseLabel(available[i]);
                    if (ImGui.Selectable(label, isSelected))
                        _selectedExerciseIdx = i;

                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Load Replay##OrcReplayBtn") && _selectedExerciseIdx >= 0)
            {
                Guid exerciseId = available[_selectedExerciseIdx].ExerciseId;

                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TransitionState,
                    PayloadJson   = JsonSerializer.Serialize(
                        new TransitionPayloadDto(TargetState: ClusterState.OperatingReplay, ScenarioId: null, ExerciseId: exerciseId, TimeMode: null),
                        OrchestrationJsonOptions.Default),
                });
            }

            // Seek slider — only when RunningReplay
            if (EffectiveState == ClusterState.OperatingReplay)
            {
                float currentExerciseTime = (float)_uiCache.MasterSimTime;
                if (!_seekPending)
                    _seekSliderValue = currentExerciseTime;

                ImGui.Spacing();
                ImGui.Text("Seek (s):");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(300f);
                if (ImGui.SliderFloat("##OrcSeek", ref _seekSliderValue, 0f, _uiCache.ReplayDuration))
                {
                    _seekPending       = true;
                    _seekDebounceTimer = 0.5f;
                }
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderEpisodesSection(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Episodes")) return;

        if (ImGui.BeginChild("##OrcEpisodes", new Vector2(-1, 180), ImGuiChildFlags.Borders))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Active episodes from ClusterMaster directly
            var activeEpisodes = EffectiveEpisodes;
            if (activeEpisodes.Count == 0)
            {
                ImGui.TextDisabled("No active episodes.");
            }
            else
            {
                foreach (var episodeId in activeEpisodes)
                {
                    string shortId = episodeId.ToString()[..8] + "...";
                    ImGui.Text(shortId);
                    ImGui.SameLine();
                    if (ImGui.Button($"Unload##OrcUnload{episodeId}"))
                        SendRequest(new ClusterOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = ClusterOpType.ManageEpisode,
                            PayloadJson   = JsonSerializer.Serialize(
                                new ManageEpisodePayloadDto(IsStart: false, EpisodeId: episodeId, ScenarioId: null),
                                OrchestrationJsonOptions.Default),
                        });
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Inject Episode:");

            if (_selectedEpisodeIdx >= _uiCache.AvailableScenarios.Length) _selectedEpisodeIdx = -1;

            ImGui.Combo("Episode Package##OrcInjectScen", ref _selectedEpisodeIdx,
                _uiCache.AvailableScenarios, _uiCache.AvailableScenarios.Length);
            if (ImGui.Button("Inject Episode##OrcInjectBtn") && _selectedEpisodeIdx >= 0)
            {
                string scenId     = _uiCache.AvailableScenarios[_selectedEpisodeIdx];
                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.ManageEpisode,
                    PayloadJson   = JsonSerializer.Serialize(
                        new ManageEpisodePayloadDto(IsStart: true, EpisodeId: Guid.NewGuid(), ScenarioId: scenId),
                        OrchestrationJsonOptions.Default),
                });
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderArchiveSection(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Archive Management##OrcArchive")) return;

        // Auto-clear archive op ID once the operation has completed (SysOpStatus terminal arrived).
        if (_activeArchiveOpId != Guid.Empty && !_uiCache.HasInFlightTransaction)
            _activeArchiveOpId = Guid.Empty;

        // — Unarchived Local Exercises —
        if (_selectedUnarchivedIdx >= _uiCache.UnarchivedLocalExercises.Length) _selectedUnarchivedIdx = -1;

        ImGui.Text("Unarchived Local:");
        var unarchived = _uiCache.UnarchivedLocalExercises;
        string unarchivedPreview = _selectedUnarchivedIdx >= 0 && _selectedUnarchivedIdx < unarchived.Length
            ? FormatExerciseLabel(unarchived[_selectedUnarchivedIdx])
            : "Select Exercise...";
        if (ImGui.BeginCombo("##UnarchivedCombo", unarchivedPreview))
        {
            for (int i = 0; i < unarchived.Length; i++)
            {
                bool isSelected = _selectedUnarchivedIdx == i;
                string label = FormatExerciseLabel(unarchived[i]);
                if (ImGui.Selectable(label, isSelected))
                    _selectedUnarchivedIdx = i;
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (disableAll || _selectedUnarchivedIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.BeginDisabled();
        if (ImGui.Button("Export to NAS ▶##OrcExport")
            && _selectedUnarchivedIdx >= 0
            && _activeArchiveOpId == Guid.Empty)
        {
            var exerciseId = unarchived[_selectedUnarchivedIdx].ExerciseId;
            var requestId = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            SendRequest(new ClusterOpRequest
            {
                RequestId     = requestId,
                OperationType = ClusterOpType.ExportArchive,
                PayloadJson   = JsonSerializer.Serialize(new ArchivePayloadDto(ExerciseId: exerciseId), OrchestrationJsonOptions.Default),
            });
        }
        if (disableAll || _selectedUnarchivedIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.EndDisabled();

        ImGui.Separator();

        // — Archived NAS Exercises —
        if (_selectedArchiveIdx >= _uiCache.ArchivedExercises.Length) _selectedArchiveIdx = -1;

        ImGui.Text("Archived on NAS:");
        var archived = _uiCache.ArchivedExercises;
        string archivedPreview = _selectedArchiveIdx >= 0 && _selectedArchiveIdx < archived.Length
            ? FormatExerciseLabel(archived[_selectedArchiveIdx])
            : "Select Exercise...";
        if (ImGui.BeginCombo("##ArchivedCombo", archivedPreview))
        {
            for (int i = 0; i < archived.Length; i++)
            {
                bool isSelected = _selectedArchiveIdx == i;
                string label = FormatExerciseLabel(archived[i]);
                if (ImGui.Selectable(label, isSelected))
                    _selectedArchiveIdx = i;
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (disableAll || _selectedArchiveIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.BeginDisabled();
        if (ImGui.Button("Import from NAS ◄##OrcImport")
            && _selectedArchiveIdx >= 0
            && _activeArchiveOpId == Guid.Empty)
        {
            var exerciseId = archived[_selectedArchiveIdx].ExerciseId;
            var requestId = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            SendRequest(new ClusterOpRequest
            {
                RequestId     = requestId,
                OperationType = ClusterOpType.ImportArchive,
                PayloadJson   = JsonSerializer.Serialize(new ArchivePayloadDto(ExerciseId: exerciseId), OrchestrationJsonOptions.Default),
            });
        }
        if (disableAll || _selectedArchiveIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.EndDisabled();

        // — Progress / Cancel —
        if (_activeArchiveOpId != Guid.Empty)
        {
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0f, 1f));
            ImGui.Text("Archive operation in progress...");
            ImGui.PopStyleColor();
            ImGui.ProgressBar(-1f * (float)ImGui.GetTime(), new Vector2(-1, 0), "");
            if (ImGui.Button("CANCEL OPERATION##OrcCancelArchive"))
            {
                SendRequest(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.CancelOperation,
                    PayloadJson   = _activeArchiveOpId.ToString(),
                });
                _activeArchiveOpId = Guid.Empty;
            }
        }
    }

    private static string FormatExerciseLabel(Fdp.Toolkit.Orchestration.ExerciseInventoryItem item)
        => $"{item.StartTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} | {item.ExerciseId}";
}
