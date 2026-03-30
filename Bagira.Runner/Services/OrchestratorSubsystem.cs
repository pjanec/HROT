using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Map.Common;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Framework.Runner;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using ImGuiNET;
using ModuleHost.Core;
using ModuleHost.Core.Time;

namespace Bagira.Runner.Services;

/// <summary>
/// Hosts <see cref="DrillMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem
{
    private DdsParticipant? _participant;
    private DrillMaster? _drillMaster;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private OrchestratorScenarioPanel? _scenarioPanel;
    private DdsWriter<SysOpRequest>? _sysOpWriter;  // S0502

    // ── Time coordinator (CGF1-A.1, BATCH-09) ─────────────────────────────
    // Minimal kernel + coordinator so PendingTimeMode drives SwitchToDeterministic.
    private FdpEventBus? _eventBus;
    private EntityRepository? _timeWorld;
    private ModuleHostKernel? _timeKernel;
    private DistributedTimeCoordinator? _timeCoordinator;
    private IDescriptorTranslator? _timeModeTranslator;
    private string? _lastProcessedTimeMode;

    /// <summary>Internal event bus exposed for test assertions on SwitchTimeModeEvent.</summary>
    internal FdpEventBus? TimeBusForTest => _eventBus;

    /// <summary>
    /// Internal test hook: exposes the <see cref="DrillMaster"/> hosted by this subsystem so
    /// E2E test fixtures can inject <see cref="SysOpRequest"/> values via
    /// <see cref="DrillMaster.HandleSysOpRequest"/> and read cluster state.
    /// </summary>
    internal DrillMaster? TestHook_DrillMaster => _drillMaster;

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);  // S0501: beige

    public void Initialize(SubsystemConfig config)
    {
        _config      = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
        _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
        _drillMaster = new DrillMaster(_participant, _config);
        _sysOpWriter   = new DdsWriter<SysOpRequest>(_participant);    // S0502
        _scenarioPanel = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter);

        // ── Time coordinator setup (CGF1-A.1, BATCH-09) ──────────────────────
        // A minimal ECS kernel gives the DistributedTimeCoordinator a wall-clock source
        // (MasterTimeController.GetCurrentState().TotalWallTicks) without needing a full
        // simulation world on the orchestrator process.
        _eventBus  = new FdpEventBus();
        _timeWorld = new EntityRepository();
        var accumulator = new EventAccumulator();
        _timeKernel = new ModuleHostKernel(_timeWorld, accumulator);
        var timeConfig = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
        var timeCtrl   = TimeControllerFactory.Create(_eventBus, timeConfig);
        _timeKernel.SetTimeController(timeCtrl);
        _timeKernel.Initialize();

        var coordConfig = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master,
            SyncConfig = TimeConfig.Default };
        _timeCoordinator   = new DistributedTimeCoordinator(_eventBus, _timeKernel, coordConfig,
            new HashSet<int>());
        _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);
    }

    public void Update(float deltaTime)
    {
        // Advance the orchestrator's wall clock so the coordinator has a monotonic
        // TotalWallTicks reference for future-barrier calculations.
        _timeKernel?.Update();
        _eventBus?.SwapBuffers();

        _drillMaster?.Tick();

        // CGF1-A.1: Consume PendingTimeMode and drive DistributedTimeCoordinator.
        var pendingMode = _drillMaster?.PendingTimeMode;
        if (pendingMode != _lastProcessedTimeMode)
        {
            if (pendingMode == "Deterministic" && _timeCoordinator != null && _drillMaster != null)
            {
                // Collect current roster IDs as slave targets for the barrier broadcast.
                var slaveIds = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
                _timeCoordinator.SwitchToDeterministic(slaveIds);
            }
            _lastProcessedTimeMode = pendingMode;
        }

        // Poll coordinator barrier and egress/ingress translate SwitchTimeModeEvent to DDS.
        _timeCoordinator?.Update();
        _timeModeTranslator?.ScanAndPublish(null!);
        _timeModeTranslator?.PollIngress(null!, null!);
    }

    public void DrawWorld() { }

    public void DrawUI()
    {
        if (_drillMaster == null) return;
        if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }   // S0501

        var bootstrapped = _drillMaster.BootstrapComplete;

        // ── Bootstrap banner ──────────────────────────────────────────────────
        if (!bootstrapped)
        {
            var waiting = _config.Mandatory
                .Where(name => !_drillMaster.NodeRoster.ActiveNodes.Values
                    .Any(p => p.SubsystemName == name &&
                              p.LocalDsmState == DSMState.Standby))
                .ToArray();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));
            ImGui.TextWrapped($"Waiting for mandatory nodes: {string.Join(", ", waiting)}");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        // ── Simulation controls (disabled until bootstrapped) ─────────────────
        if (!bootstrapped) ImGui.BeginDisabled();

        // S0502: wire TODO buttons to real SysOpRequest writes
        if (ImGui.Button("Initialize Live") && _sysOpWriter != null)
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.TransitionState,
                PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}}}",
            });
        ImGui.SameLine();
        if (ImGui.Button("Pause") && _sysOpWriter != null)
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.PauseTime,
                PayloadJson   = string.Empty,
            });
        ImGui.SameLine();
        if (ImGui.Button("Resume") && _sysOpWriter != null)
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.ResumeTime,
                PayloadJson   = string.Empty,
            });

        if (!bootstrapped) ImGui.EndDisabled();

        ImGui.Separator();

        // ── System health table ───────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Node Health", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ImGui.BeginTable("NodeHealth", 6,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("NodeId");
                ImGui.TableSetupColumn("Subsystem");
                ImGui.TableSetupColumn("Last HB (ms ago)");
                ImGui.TableSetupColumn("DSM State");
                ImGui.TableSetupColumn("CPU %");
                ImGui.TableSetupColumn("RAM (MB)");
                ImGui.TableHeadersRow();

                foreach (var kv in _drillMaster.NodeRoster.ActiveNodes)
                {
                    var p     = kv.Value;
                    var msAgo = (long)(nowMs - p.LastHeartbeatUtcSeconds * 1000.0);
                    var ramMb = p.RamUsedBytes / (1024.0 * 1024.0);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(p.NodeId.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(p.SubsystemName);
                    ImGui.TableNextColumn(); ImGui.Text(msAgo.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(p.LocalDsmState.ToString());
                    ImGui.TableNextColumn(); ImGui.Text($"{p.CpuUsagePercent:F1}");
                    ImGui.TableNextColumn(); ImGui.Text($"{ramMb:F1}");
                }
                ImGui.EndTable();
            }
        }

        // ── 2PC history table (S0501: 5-column overhaul) ──────────────────────
        if (ImGui.CollapsingHeader("2PC History"))
        {
            var history = _drillMaster.TransactionHistory;
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

                    // Column 1: full GUID as a TreeNode for expandability
                    ImGui.TableNextColumn();
                    bool open = ImGui.TreeNodeEx(tx.TransactionId.ToString(),
                        ImGuiTreeNodeFlags.SpanFullWidth);

                    // Context menu on row
                    if (ImGui.BeginPopupContextItem($"ctx_{tx.TransactionId}"))
                    {
                        string line = $"{tx.TransactionId} | {tx.TargetDsmState} | " +
                                      $"{(tx.IsAborted ? "Aborted" : "Completed")} | {tx.PayloadJson}";
                        if (ImGui.MenuItem("Copy line to clipboard"))
                            ImGui.SetClipboardText(line);
                        ImGui.EndPopup();
                    }

                    // Column 2: target state
                    ImGui.TableNextColumn(); ImGui.Text(tx.TargetDsmState.ToString());

                    // Column 3: result
                    ImGui.TableNextColumn(); ImGui.Text(tx.IsAborted ? "Aborted" : "Completed");

                    // Column 4: aggregate ACK latency summary
                    string latency = tx.NodeAckLatencyMs.Count == 0
                        ? "—"
                        : string.Join(", ", tx.NodeAckLatencyMs.Select(kv => $"{kv.Key}:{kv.Value:F0}ms"));
                    ImGui.TableNextColumn(); ImGui.Text(latency);

                    // Column 5: payload snippet with tooltip
                    ImGui.TableNextColumn();
                    string payloadSnippet = tx.PayloadJson.Length > 25
                        ? tx.PayloadJson[..25] + "..."
                        : tx.PayloadJson;
                    ImGui.TextUnformatted(payloadSnippet);
                    if (!string.IsNullOrWhiteSpace(tx.PayloadJson) && ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(FormatPrettyJson(tx.PayloadJson));
                        ImGui.EndTooltip();
                    }

                    // Expanded rows: one child row per NodeResponse entry
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
                            string nodeSnippet = nr.Value.Length > 25
                                ? nr.Value[..25] + "..."
                                : nr.Value;
                            ImGui.Text(nodeSnippet);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.EndTable();
            }
        }

        // ── Scenario & Story controls (CGF1-S0106) ───────────────────────────
        _scenarioPanel?.Render();
        ImGui.End();   // S0501
    }

    public void Shutdown()
    {
        _scenarioPanel = null;
        _sysOpWriter?.Dispose();     // S0502
        _sysOpWriter = null;
        _drillMaster?.Dispose();
        _drillMaster = null;
        _timeKernel?.Dispose();
        _timeKernel = null;
        _timeWorld = null;
        _eventBus = null;
        _timeCoordinator = null;
        _lastProcessedTimeMode = null;
        _participant?.Dispose();
        _participant = null;
    }

    /// <summary>
    /// Formats a JSON string with indentation for tooltip display.
    /// Returns the original string if parsing fails (CGF1-S0501).
    /// </summary>
    internal static string FormatPrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }
}
