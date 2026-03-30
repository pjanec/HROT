using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using ImGuiNET;

namespace Bagira.Runner.Services;

/// <summary>
/// ImGui scenario and story control panel for the Orchestrator subsystem (CGF1-S0106).
/// Renders six child windows: Status Banner, Drill Control, Checkpoint, Scenario,
/// Replay, and Stories.  All interactive controls are disabled while the cluster
/// is not bootstrapped or has an in-flight distributed transaction.
/// </summary>
public sealed class OrchestratorScenarioPanel
{
    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly DrillMaster _drillMaster;
    private readonly DdsWriter<SysOpRequest> _sysOpWriter;  // S0502

    // ── Scenario section state ────────────────────────────────────────────
    private string _saveScenarioId  = string.Empty;
    private string _loadScenarioId  = string.Empty;

    // ── Replay section state ──────────────────────────────────────────────
    private string _replayDrillId   = string.Empty;
    private float  _seekSliderValue = 0f;

    // ── Stories section state ─────────────────────────────────────────────
    private string _injectScenarioId = string.Empty;
    private string _injectStoryId    = string.Empty;

    // ── Child window sizes (0,0 = auto-fit) ──────────────────────────────
    private static readonly Vector2 AutoSize = Vector2.Zero;

    /// <param name="drillMaster">The DrillMaster instance hosted by the Orchestrator subsystem.</param>
    /// <param name="sysOpWriter">DDS writer used to publish SysOpRequest commands to the network (S0502).</param>
    public OrchestratorScenarioPanel(DrillMaster drillMaster, DdsWriter<SysOpRequest> sysOpWriter)
    {
        _drillMaster = drillMaster   ?? throw new ArgumentNullException(nameof(drillMaster));
        _sysOpWriter = sysOpWriter   ?? throw new ArgumentNullException(nameof(sysOpWriter));
    }

    /// <summary>
    /// Renders all six control sections.  Must be called from within an active ImGui frame.
    /// </summary>
    public void Render()
    {
        var bootstrapped   = _drillMaster.BootstrapComplete;
        var hasInFlight    = _drillMaster.HasInFlightTransaction;
        var currentState   = _drillMaster.CurrentSystemState;
        var activeTx       = _drillMaster.ActiveTransaction;
        var disableAll     = !bootstrapped || hasInFlight;

        ImGui.Separator();

        // ── 1. Status Banner (always enabled) ─────────────────────────────
        RenderStatusBanner(currentState, activeTx, bootstrapped, hasInFlight);

        // ── 2. Drill Control ───────────────────────────────────────────────
        RenderDrillControl(currentState, disableAll);

        // ── 3. Checkpoint ─────────────────────────────────────────────────
        RenderCheckpointSection(currentState, disableAll);

        // ── 4. Scenario ────────────────────────────────────────────────────
        RenderScenarioSection(currentState, disableAll);

        // ── 5. Replay ──────────────────────────────────────────────────────
        RenderReplaySection(currentState, disableAll);

        // ── 6. Stories ─────────────────────────────────────────────────────
        RenderStoriesSection(disableAll);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private rendering helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void RenderStatusBanner(DSMState currentState, DistributedTransaction? activeTx,
        bool bootstrapped, bool hasInFlight)
    {
        if (ImGui.BeginChild("##OrcStatusBanner", new Vector2(-1, 54), ImGuiChildFlags.Borders))
        {
            string drillShort = activeTx != null
                ? activeTx.TransactionId.ToString()[..8]
                : "--------";

            string txStatus = hasInFlight
                ? $"TX {drillShort}... in flight"
                : "idle";

            // S0501: show Source→Target when a transition is in flight
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

    private void RenderDrillControl(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Drill Control", ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (ImGui.BeginChild("##OrcDrillControl", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            var reachable = _drillMaster.GetReachableTargets();
            if (reachable.Count == 0)
            {
                ImGui.TextDisabled("No reachable transitions from current state.");
            }
            else
            {
                foreach (var target in reachable)
                {
                    if (ImGui.Button(target.ToString()))
                        _sysOpWriter.Write(new SysOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = SysOpType.TransitionState,
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

    private void RenderCheckpointSection(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Checkpoint")) return;

        if (ImGui.BeginChild("##OrcCheckpoint", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            bool checkpointDisabled = disableAll || currentState != DSMState.RunningLive;
            if (checkpointDisabled) ImGui.BeginDisabled();

            if (ImGui.Button("Take Checkpoint"))
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TakeCheckpoint,
                    PayloadJson   = string.Empty,
                });

            if (checkpointDisabled) ImGui.EndDisabled();

            if (currentState != DSMState.RunningLive)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(requires RunningLive)");
            }
        }
        ImGui.EndChild();
    }

    private void RenderScenarioSection(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Scenario")) return;

        if (ImGui.BeginChild("##OrcScenario", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Save Scenario
            ImGui.InputText("Save Scenario ID##OrcSaveId", ref _saveScenarioId, 128);
            ImGui.SameLine();
            if (ImGui.Button("Save Scenario##OrcBtn") && !string.IsNullOrWhiteSpace(_saveScenarioId))
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.SaveScenario,
                    PayloadJson   = $"{{\"ScenarioId\":\"{_saveScenarioId}\"}}",
                });

            ImGui.Spacing();

            // Load Scenario
            ImGui.InputText("Load Scenario ID##OrcLoadId", ref _loadScenarioId, 128);
            ImGui.SameLine();
            if (ImGui.Button("Load into Edit##OrcLoadEdit") && !string.IsNullOrWhiteSpace(_loadScenarioId))
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingEdit}," +
                                    $"\"ScenarioId\":\"{_loadScenarioId}\"}}",
                });
            ImGui.SameLine();
            if (ImGui.Button("Load into Live##OrcLoadLive") && !string.IsNullOrWhiteSpace(_loadScenarioId))
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}," +
                                    $"\"ScenarioId\":\"{_loadScenarioId}\"}}",
                });

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderReplaySection(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Replay")) return;

        if (ImGui.BeginChild("##OrcReplay", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Load Replay
            ImGui.InputText("Drill ID##OrcReplayId", ref _replayDrillId, 64);
            ImGui.SameLine();
            if (ImGui.Button("Load Replay##OrcReplayBtn") && !string.IsNullOrWhiteSpace(_replayDrillId))
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.RunningReplay}," +
                                    $"\"DrillId\":\"{_replayDrillId}\"}}",
                });

            // Seek slider — only when RunningReplay
            if (currentState == DSMState.RunningReplay)
            {
                ImGui.Spacing();
                ImGui.Text("Seek (s):");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(300f);
                if (ImGui.SliderFloat("##OrcSeek", ref _seekSliderValue, 0f, 3600f))
                {
                    long wallTicks = (long)(_seekSliderValue * 10_000_000L); // seconds → 100-ns ticks
                    _sysOpWriter.Write(new SysOpRequest
                    {
                        RequestId     = Guid.NewGuid(),
                        OperationType = SysOpType.ReplaySeek,
                        PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
                    });
                }
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderStoriesSection(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Stories")) return;

        if (ImGui.BeginChild("##OrcStories", new Vector2(-1, 180), ImGuiChildFlags.Borders))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Active stories list
            var activeStories = _drillMaster.ActiveStories;
            if (activeStories.Count == 0)
            {
                ImGui.TextDisabled("No active stories.");
            }
            else
            {
                foreach (var storyId in activeStories)
                {
                    string shortId = storyId.ToString()[..8] + "...";
                    ImGui.Text(shortId);
                    ImGui.SameLine();
                    if (ImGui.Button($"Unload##OrcUnload{storyId}"))
                        _sysOpWriter.Write(new SysOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = SysOpType.ManageStory,
                            PayloadJson   = $"{{\"Mode\":\"Stop\",\"StoryId\":\"{storyId}\"}}",
                        });
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Inject Story:");
            ImGui.InputText("Scenario ID##OrcInjectScen", ref _injectScenarioId, 128);
            ImGui.InputText("Story ID##OrcInjectStory",   ref _injectStoryId,    64);
            if (ImGui.Button("Inject Story##OrcInjectBtn") &&
                !string.IsNullOrWhiteSpace(_injectScenarioId) &&
                !string.IsNullOrWhiteSpace(_injectStoryId))
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.ManageStory,
                    PayloadJson   = $"{{\"Mode\":\"Start\"," +
                                    $"\"StoryId\":\"{_injectStoryId}\"," +
                                    $"\"ScenarioId\":\"{_injectScenarioId}\"}}",
                });

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }
}
