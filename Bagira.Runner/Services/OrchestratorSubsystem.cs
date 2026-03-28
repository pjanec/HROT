using System.Linq;
using Bagira.Map.Common;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using FDP.Framework.Runner;
using ImGuiNET;

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

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.12f, 0.18f, 0.42f, 1f);

    public void Initialize(SubsystemConfig config)
    {
        _config      = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
        _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
        _drillMaster = new DrillMaster(_participant, _config);
    }

    public void Update(float deltaTime)
    {
        _drillMaster?.Tick();
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
            if (ImGui.BeginTable("NodeHealth", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("NodeId");
                ImGui.TableSetupColumn("Subsystem");
                ImGui.TableSetupColumn("Last HB (ms ago)");
                ImGui.TableSetupColumn("DSM State");
                ImGui.TableHeadersRow();

                foreach (var kv in _drillMaster.NodeRoster.ActiveNodes)
                {
                    var p     = kv.Value;
                    var msAgo = (long)(nowMs - p.LastHeartbeatUtcSeconds * 1000.0);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(p.NodeId.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(p.SubsystemName);
                    ImGui.TableNextColumn(); ImGui.Text(msAgo.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(p.LocalDsmState.ToString());
                }
                ImGui.EndTable();
            }
        }

        // ── 2PC history table ─────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("2PC History"))
        {
            var history = _drillMaster.TransactionHistory;
            if (ImGui.BeginTable("TxHistory", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("TransactionId");
                ImGui.TableSetupColumn("Target State");
                ImGui.TableSetupColumn("Result");
                ImGui.TableHeadersRow();

                foreach (var tx in history)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(tx.TransactionId.ToString()[..8] + "...");
                    ImGui.TableNextColumn(); ImGui.Text(tx.TargetDsmState.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(tx.IsAborted ? "Aborted" : "Completed");
                }
                ImGui.EndTable();
            }
        }
    }

    public void Shutdown()
    {
        _drillMaster?.Dispose();
        _drillMaster = null;
        _participant?.Dispose();
        _participant = null;
    }
}
