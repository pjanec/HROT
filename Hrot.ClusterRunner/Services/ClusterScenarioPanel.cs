using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using CycloneDDS.Runtime;
using ImGuiNET;

namespace Hrot.ClusterRunner.Services;

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
    private readonly ClusterUiCache          _uiCache;
    private readonly DdsWriter<ClusterOpRequest> _sysOpWriter;

    // ── Scenario section state ────────────────────────────────────────────
    private string _saveScenarioId  = string.Empty;

    // ── Asset combo state (S0504 / S0506) ────────────────────────────────
    private int _selectedLoadScenarioIdx = -1;
    private int _selectedExerciseIdx        = -1;
    private int _selectedEpisodeIdx        = -1;

    // ── Replay section state ──────────────────────────────────────────────
    private float  _seekSliderValue = 0f;

    // ── Seek debounce (S0503) ─────────────────────────────────────────────
    private float _seekDebounceTimer = 0f;
    private bool  _seekPending       = false;
    private float _replayDuration    = 3600f;

    // ── Archive Management state (S0505) ──────────────────────────────────
    private int  _selectedArchiveIdx     = -1;
    private int  _selectedUnarchivedIdx  = -1;
    private Guid _activeArchiveOpId      = Guid.Empty;

    // ── Child window sizes ────────────────────────────────────────────────
    private static readonly Vector2 AutoSize = Vector2.Zero;

    /// <param name="sysOpWriter">DDS writer for ClusterOpRequest commands (S0502).</param>
    /// <param name="uiCache">Network projection cache to read cluster state from (S0506).</param>
    /// <param name="requestPause">Optional callback; reserved for future use.</param>
    public ClusterScenarioPanel(DdsWriter<ClusterOpRequest> sysOpWriter,
                                ClusterUiCache uiCache,
                                Action? requestPause = null)
    {
        _sysOpWriter = sysOpWriter ?? throw new ArgumentNullException(nameof(sysOpWriter));
        _uiCache     = uiCache     ?? throw new ArgumentNullException(nameof(uiCache));
    }

    /// <summary>Advances the seek debounce timer. Call once per frame from the subsystem Update().</summary>
    public void Update(float dt)
    {
        if (!_seekPending) return;
        _seekDebounceTimer -= dt;
        if (_seekDebounceTimer > 0f) return;

        _seekPending = false;
        long wallTicks = (long)(_seekSliderValue * 10_000_000L);
        _sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
        });
    }

    /// <summary>
    /// Reads the replay duration in seconds from the exercise's meta.json.
    /// Returns 3600 if the file is absent or malformed.
    /// </summary>
    internal static float GetReplayDuration(string metaJsonContent)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metaJsonContent);
            if (doc.RootElement.TryGetProperty("TotalFrames", out var el))
                return el.GetInt32() / 60f;
        }
        catch { }
        return 3600f;
    }

    /// <summary>
    /// Renders all panel sections. Must be called from within an active ImGui frame.
    /// </summary>
    /// <param name="cache">Cache snapshot for this frame (same instance as constructed with).</param>
    /// <param name="disableAll">When <c>true</c>, all interactive controls are disabled.</param>
    public void Render(ClusterUiCache cache, bool disableAll)
    {
        // ── Bootstrap banner ───────────────────────────────────────────────
        if (!cache.IsBootstrapped)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));
            ImGui.TextWrapped("Cluster not bootstrapped — waiting for mandatory nodes.");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        // ── Node Health table ──────────────────────────────────────────────
        RenderNodeHealthTable(cache);

        // ── Time Control section (S0503) ───────────────────────────────────
        RenderTimeControl(cache, disableAll);

        // ── 2PC History table (S0501) ──────────────────────────────────────
        RenderTxHistory(cache);

        ImGui.Separator();

        // ── 1. Status Banner (always enabled) ─────────────────────────────
        RenderStatusBanner(cache);

        // ── 2. Cluster Control ───────────────────────────────────────────────
        RenderClusterControl(cache, disableAll);

        // ── 3. Checkpoint ─────────────────────────────────────────────────
        RenderCheckpointSection(cache.CurrentState, disableAll);

        // ── 4. Scenario ────────────────────────────────────────────────────
        RenderScenarioSection(cache, disableAll);

        // ── 5. Replay ──────────────────────────────────────────────────────
        RenderReplaySection(cache, disableAll);

        // ── 6. Episodes ─────────────────────────────────────────────────────
        RenderEpisodesSection(cache, disableAll);

        // ── 7. Archive Management ──────────────────────────────────────────
        RenderArchiveSection(cache, disableAll);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sections moved from OrchestratorSubsystem.DrawUI()
    // ─────────────────────────────────────────────────────────────────────────

    private void RenderNodeHealthTable(ClusterUiCache cache)
    {
        if (!ImGui.CollapsingHeader("Node Health", ImGuiTreeNodeFlags.DefaultOpen)) return;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

            foreach (var kv in cache.ActiveNodes)
            {
                var p        = kv.Value;
                var lastSeen = cache.GetNodeLastSeenMs(p.NodeId);
                var msAgo    = lastSeen > 0 ? (nowMs - lastSeen) : -1L;
                var ramMb    = p.RamUsedBytes / (1024.0 * 1024.0);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(p.NodeId.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.SubsystemName ?? "—");
                ImGui.TableNextColumn(); ImGui.Text(msAgo >= 0 ? msAgo.ToString() : "—");
                ImGui.TableNextColumn(); ImGui.Text(p.LocalClusterState.ToString());
                ImGui.TableNextColumn(); ImGui.Text($"{p.CpuUsagePercent:F1}");
                ImGui.TableNextColumn(); ImGui.Text($"{ramMb:F1}");
            }
            ImGui.EndTable();
        }
    }

    private void RenderTimeControl(ClusterUiCache cache, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen)) return;

        long   wallTicks   = DateTimeOffset.UtcNow.Ticks;
        string wallTimeStr = new DateTime(wallTicks, DateTimeKind.Utc).ToString("HH:mm:ss.fff");
        ImGui.Text($"Wall Time: {wallTimeStr}");

        var   simSpan  = TimeSpan.FromSeconds(cache.MasterSimTime);
        string simStr  = $"{(int)simSpan.TotalHours:D2}:{simSpan.Minutes:D2}:{simSpan.Seconds:D2}.{simSpan.Milliseconds:D3}";
        string status  = cache.IsPaused ? "PAUSED" : "RUNNING";
        ImGui.Text($"Sim Time: {simStr} [{status}]");

        if (disableAll) ImGui.BeginDisabled();

        float timeScale = cache.MasterTimeScale;
        if (ImGui.Button(cache.IsPaused ? "Resume##OrcResume" : "Pause##OrcPause"))
            _sysOpWriter.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = cache.IsPaused ? ClusterOpType.ResumeTime : ClusterOpType.PauseTime,
                PayloadJson   = string.Empty,
            });

        ImGui.SameLine();
        if (!cache.IsPaused) ImGui.BeginDisabled();
        if (ImGui.Button("Step##OrcStep"))
            _sysOpWriter.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.StepTime,
                PayloadJson   = string.Empty,
            });
        if (!cache.IsPaused) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderFloat("Speed##OrcSpeed", ref timeScale, 0.1f, 10.0f, "%.1fx"))
            _sysOpWriter.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.SetTimeScale,
                PayloadJson   = timeScale.ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture),
            });

        if (disableAll) ImGui.EndDisabled();
    }

    private static void RenderTxHistory(ClusterUiCache cache)
    {
        if (!ImGui.CollapsingHeader("2PC History")) return;

        var history    = cache.TxHistory;
        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        if (ImGui.BeginTable("TxHistory", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0, rowHeight * 11.5f)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("TransactionId");
            ImGui.TableSetupColumn("Target State");
            ImGui.TableSetupColumn("Result");
            ImGui.TableSetupColumn("ACK Latency (ms)");
            ImGui.TableSetupColumn("Payload");
            ImGui.TableHeadersRow();

            foreach (var tx in history)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                bool open = ImGui.TreeNodeEx(tx.TransactionId.ToString(),
                    ImGuiTreeNodeFlags.SpanFullWidth);

                if (ImGui.BeginPopupContextItem($"ctx_{tx.TransactionId}"))
                {
                    string result = tx.Completed ? "Completed" : (tx.IsAborted ? "Aborted" : "In Flight");
                    string line   = $"{tx.TransactionId} | {tx.TargetDsmState} | {result} | {tx.PayloadJson}";
                    if (ImGui.MenuItem("Copy line to clipboard"))
                        ImGui.SetClipboardText(line);
                    ImGui.EndPopup();
                }

                ImGui.TableNextColumn(); ImGui.Text(tx.TargetDsmState.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(tx.Completed ? "Completed" : (tx.IsAborted ? "Aborted" : "In Flight"));

                string latency = tx.NodeAckLatencyMs.Count == 0
                    ? "—"
                    : string.Join(", ", tx.NodeAckLatencyMs.Select(kv => $"{kv.Key}:{kv.Value:F0}ms"));
                ImGui.TableNextColumn(); ImGui.Text(latency);

                ImGui.TableNextColumn();
                string payloadSnippet = tx.PayloadJson.Length > 25
                    ? tx.PayloadJson[..25] + "..."
                    : tx.PayloadJson;
                ImGui.TextUnformatted(payloadSnippet);
                if (!string.IsNullOrWhiteSpace(tx.PayloadJson) && ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(OrchestratorSubsystem.FormatPrettyJson(tx.PayloadJson));
                    ImGui.EndTooltip();
                }

                if (open)
                {
                    foreach (var nr in tx.NodeResponses)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TreeNodeEx($"↳ Node {nr.Key}",
                            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen |
                            ImGuiTreeNodeFlags.SpanFullWidth);
                        ImGui.TableNextColumn(); ImGui.Text("—");
                        ImGui.TableNextColumn(); ImGui.Text("—");
                        ImGui.TableNextColumn();
                        string nodeLatency = tx.NodeAckLatencyMs.TryGetValue(nr.Key, out float ms)
                            ? $"{ms:F0}ms" : "—";
                        ImGui.Text(nodeLatency);
                        ImGui.TableNextColumn();
                        string nodeSnippet = nr.Value.Length > 25 ? nr.Value[..25] + "..." : nr.Value;
                        ImGui.Text(nodeSnippet);
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

    private static void RenderStatusBanner(ClusterUiCache cache)
    {
        var activeTx    = cache.ActiveTransaction;
        var hasInFlight = cache.HasInFlightTransaction;
        var bootstrapped = cache.IsBootstrapped;
        var currentState = cache.CurrentState;

        if (ImGui.BeginChild("##OrcStatusBanner", new Vector2(-1, 54), ImGuiChildFlags.Borders))
        {
            string drillShort = activeTx != null
                ? activeTx.TransactionId.ToString()[..8]
                : "--------";

            string txStatus = hasInFlight
                ? $"TX {drillShort}... in flight"
                : "idle";

            if (hasInFlight && activeTx != null &&
                activeTx.SourceDsmState != activeTx.TargetDsmState)
            {
                ImGui.Text($"State: {activeTx.SourceDsmState} → {activeTx.TargetDsmState}");
            }
            else
            {
                ImGui.Text($"State: {currentState}");
            }
            ImGui.SameLine();
            ImGui.Text("|");
            ImGui.SameLine();
            ImGui.Text(bootstrapped ? txStatus : "NOT BOOTSTRAPPED");
        }
        ImGui.EndChild();
    }

    private void RenderClusterControl(ClusterUiCache cache, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Cluster Control", ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (ImGui.BeginChild("##OrcClusterControl", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            var reachable = cache.ReachableTargets;
            if (reachable.Count == 0)
            {
                ImGui.TextDisabled("No reachable transitions from current state.");
            }
            else
            {
                foreach (var target in reachable)
                {
                    if (ImGui.Button(target.ToString()))
                        _sysOpWriter.Write(new ClusterOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = ClusterOpType.TransitionState,
                            PayloadJson   = $"{{\"TargetState\":{(int)target}}}",
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
                _sysOpWriter.Write(new ClusterOpRequest
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

    private void RenderScenarioSection(ClusterUiCache cache, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Scenario")) return;

        if (ImGui.BeginChild("##OrcScenario", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Clamp index if list shrank
            if (_selectedLoadScenarioIdx >= cache.AvailableScenarios.Length)
                _selectedLoadScenarioIdx = -1;

            // Save Scenario
            ImGui.InputText("Save Scenario ID##OrcSaveId", ref _saveScenarioId, 128);
            ImGui.SameLine();
            if (ImGui.Button("Save Scenario##OrcBtn") && !string.IsNullOrWhiteSpace(_saveScenarioId))
                _sysOpWriter.Write(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.SaveScenario,
                    PayloadJson   = $"{{\"ScenarioId\":\"{_saveScenarioId}\"}}",
                });

            ImGui.Spacing();

            // Load Scenario
            ImGui.Combo("Select Scenario##OrcLoadId", ref _selectedLoadScenarioIdx,
                cache.AvailableScenarios, cache.AvailableScenarios.Length);
            ImGui.SameLine();
            if (ImGui.Button("Load into Edit##OrcLoadEdit") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = cache.AvailableScenarios[_selectedLoadScenarioIdx];
                _sysOpWriter.Write(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingEdit}," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Load into Live##OrcLoadLive") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = cache.AvailableScenarios[_selectedLoadScenarioIdx];
                _sysOpWriter.Write(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingLive}," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderReplaySection(ClusterUiCache cache, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Replay")) return;

        if (ImGui.BeginChild("##OrcReplay", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            if (_selectedExerciseIdx >= cache.AvailableExercises.Length) _selectedExerciseIdx = -1;

            // Load Replay
            ImGui.Combo("Select Exercise##OrcReplayId", ref _selectedExerciseIdx,
                cache.AvailableExercises, cache.AvailableExercises.Length);
            ImGui.SameLine();
            if (ImGui.Button("Load Replay##OrcReplayBtn") && _selectedExerciseIdx >= 0)
            {
                string exerciseId = cache.AvailableExercises[_selectedExerciseIdx];

                // Read replay duration from meta.json if available
                string metaPath = Path.Combine(@"C:\FDP_Temp", exerciseId, "recording.meta.json");
                if (File.Exists(metaPath))
                    _replayDuration = GetReplayDuration(File.ReadAllText(metaPath));

                _sysOpWriter.Write(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.OperatingReplay}," +
                                    $"\"ExerciseId\":\"{exerciseId}\"}}",
                });
            }

            // Seek slider — only when RunningReplay
            if (cache.CurrentState == ClusterState.OperatingReplay)
            {
                float currentExerciseTime = (float)cache.MasterSimTime;
                if (!_seekPending)
                    _seekSliderValue = currentExerciseTime;

                ImGui.Spacing();
                ImGui.Text("Seek (s):");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(300f);
                if (ImGui.SliderFloat("##OrcSeek", ref _seekSliderValue, 0f, _replayDuration))
                {
                    _seekPending       = true;
                    _seekDebounceTimer = 0.5f;
                }
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderEpisodesSection(ClusterUiCache cache, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Episodes")) return;

        if (ImGui.BeginChild("##OrcEpisodes", new Vector2(-1, 180), ImGuiChildFlags.Borders))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Active episodes list (from cache)
            var activeEpisodes = cache.ActiveEpisodes;
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
                        _sysOpWriter.Write(new ClusterOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = ClusterOpType.ManageEpisode,
                            PayloadJson   = $"{{\"Mode\":\"Stop\",\"EpisodeId\":\"{episodeId}\"}}",
                        });
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Inject Episode:");

            if (_selectedEpisodeIdx >= cache.AvailableScenarios.Length) _selectedEpisodeIdx = -1;

            ImGui.Combo("Episode Package##OrcInjectScen", ref _selectedEpisodeIdx,
                cache.AvailableScenarios, cache.AvailableScenarios.Length);
            if (ImGui.Button("Inject Episode##OrcInjectBtn") && _selectedEpisodeIdx >= 0)
            {
                string scenId     = cache.AvailableScenarios[_selectedEpisodeIdx];
                string newEpisodeId = Guid.NewGuid().ToString();
                _sysOpWriter.Write(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.ManageEpisode,
                    PayloadJson   = $"{{\"Mode\":\"Start\"," +
                                    $"\"EpisodeId\":\"{newEpisodeId}\"," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderArchiveSection(ClusterUiCache cache, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Archive Management##OrcArchive")) return;

        // — Unarchived Local Exercises —
        if (_selectedUnarchivedIdx >= cache.UnarchivedLocalExercises.Length) _selectedUnarchivedIdx = -1;

        ImGui.Text("Unarchived Local:");
        ImGui.Combo("##UnarchivedCombo", ref _selectedUnarchivedIdx,
                    cache.UnarchivedLocalExercises, cache.UnarchivedLocalExercises.Length);

        if (disableAll || _selectedUnarchivedIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.BeginDisabled();
        if (ImGui.Button("Export to NAS ▶##OrcExport")
            && _selectedUnarchivedIdx >= 0
            && _activeArchiveOpId == Guid.Empty)
        {
            var exerciselName = cache.UnarchivedLocalExercises[_selectedUnarchivedIdx];
            var requestId = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            _sysOpWriter.Write(new ClusterOpRequest
            {
                RequestId     = requestId,
                OperationType = ClusterOpType.ExportArchive,
                PayloadJson   = $"{{\"ExerciseId\":\"{exerciselName}\"}}",
            });
        }
        if (disableAll || _selectedUnarchivedIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.EndDisabled();

        ImGui.Separator();

        // — Archived NAS Exercises —
        if (_selectedArchiveIdx >= cache.ArchivedExercises.Length) _selectedArchiveIdx = -1;

        ImGui.Text("Archived on NAS:");
        ImGui.Combo("##ArchivedCombo", ref _selectedArchiveIdx,
                    cache.ArchivedExercises, cache.ArchivedExercises.Length);

        if (disableAll || _selectedArchiveIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.BeginDisabled();
        if (ImGui.Button("Import from NAS ◄##OrcImport")
            && _selectedArchiveIdx >= 0
            && _activeArchiveOpId == Guid.Empty)
        {
            var exerciseName = cache.ArchivedExercises[_selectedArchiveIdx];
            var requestId = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            _sysOpWriter.Write(new ClusterOpRequest
            {
                RequestId     = requestId,
                OperationType = ClusterOpType.ImportArchive,
                PayloadJson   = $"{{\"ExerciseId\":\"{exerciseName}\"}}",
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
                _sysOpWriter.Write(new ClusterOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = ClusterOpType.CancelOperation,
                    PayloadJson   = _activeArchiveOpId.ToString(),
                });
                _activeArchiveOpId = Guid.Empty;
            }
        }
    }
}
