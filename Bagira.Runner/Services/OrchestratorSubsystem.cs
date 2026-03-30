using System.Collections.Generic;
using System.Linq;
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

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.12f, 0.18f, 0.42f, 1f);

    public void Initialize(SubsystemConfig config)
    {
        _config      = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
        _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
        _drillMaster = new DrillMaster(_participant, _config);
        _scenarioPanel = new OrchestratorScenarioPanel(_drillMaster);

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

        var bootstrapped = _drillMaster.BootstrapComplete;

        // ── Bootstrap banner ──────────────────────────────────────────────────
        if (!bootstrapped)
        {
            var waiting = _config.Mandatory
                .Where(name => !_drillMaster.NodeRoster.ActiveNodes.Values
                    .Any(p => p.SubsystemName == name &&
                              p.LocalDsmState == Bagira.BDC.SSTD.Orchestration.DSMState.Standby))
                .ToArray();

            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f));
            ImGui.TextWrapped($"Waiting for mandatory nodes: {string.Join(", ", waiting)}");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        // ── Simulation controls (disabled until bootstrapped) ─────────────────
        if (!bootstrapped) ImGui.BeginDisabled();

        if (ImGui.Button("Initialize Live"))  { /* TODO: S0201 SysOpRequest */ }
        ImGui.SameLine();
        if (ImGui.Button("Pause"))            { /* TODO: S0201 SysOpRequest */ }
        ImGui.SameLine();
        if (ImGui.Button("Resume"))           { /* TODO: S0201 SysOpRequest */ }

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

        // ── 2PC history table ─────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("2PC History"))
        {
            var history = _drillMaster.TransactionHistory;
            if (ImGui.BeginTable("TxHistory", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("TransactionId");
                ImGui.TableSetupColumn("Target State");
                ImGui.TableSetupColumn("Result");
                ImGui.TableSetupColumn("ACK Latency (ms)");
                ImGui.TableHeadersRow();

                foreach (var tx in history)
                {
                    // Build a compact ACK-latency summary: "node:ms, ..." or "0" when not yet populated.
                    string latency = tx.NodeAckLatencyMs.Count == 0
                        ? "0"
                        : string.Join(", ", tx.NodeAckLatencyMs.Select(kv => $"{kv.Key}:{kv.Value:F0}ms"));

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(tx.TransactionId.ToString()[..8] + "...");
                    ImGui.TableNextColumn(); ImGui.Text(tx.TargetDsmState.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(tx.IsAborted ? "Aborted" : "Completed");
                    ImGui.TableNextColumn(); ImGui.Text(latency);
                }
                ImGui.EndTable();
            }
        }

        // ── Scenario & Story controls (CGF1-S0106) ───────────────────────────
        _scenarioPanel?.Render();
    }

    public void Shutdown()
    {
        _scenarioPanel = null;
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
}
