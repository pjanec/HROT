using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // ── Asset combo state (S0504) ─────────────────────────────────────────
    private string[] _availableScenarios     = Array.Empty<string>();
    private string[] _availableStories       = Array.Empty<string>();
    private string[] _availableDrills        = Array.Empty<string>();
    private int      _selectedLoadScenarioIdx = -1;
    private int      _selectedDrillIdx        = -1;
    private int      _selectedStoryIdx        = -1;

    // ── Replay section state ──────────────────────────────────────────────
    private float  _seekSliderValue = 0f;

    // ── Seek debounce (S0503) ─────────────────────────────────────────────
    private float _seekDebounceTimer = 0f;
    private bool  _seekPending       = false;
    private float _replayDuration    = 3600f;
    // ── Archive Management state (S0505) ─────────────────────────────
    private string[] _archivedDrills        = Array.Empty<string>();
    private string[] _unarchivedLocalDrills  = Array.Empty<string>();
    private int      _selectedArchiveIdx     = -1;
    private int      _selectedUnarchivedIdx  = -1;
    private Guid     _activeArchiveOpId      = Guid.Empty;
    // ── Child window sizes (0,0 = auto-fit) ──────────────────────────────
    private static readonly Vector2 AutoSize = Vector2.Zero;

    /// <param name="drillMaster">The DrillMaster instance hosted by the Orchestrator subsystem.</param>
    /// <param name="sysOpWriter">DDS writer used to publish SysOpRequest commands to the network (S0502).</param>
    public OrchestratorScenarioPanel(DrillMaster drillMaster, DdsWriter<SysOpRequest> sysOpWriter)
    {
        _drillMaster = drillMaster   ?? throw new ArgumentNullException(nameof(drillMaster));
        _sysOpWriter = sysOpWriter   ?? throw new ArgumentNullException(nameof(sysOpWriter));
        RefreshLocalAssets();
    }

    /// <summary>
    /// Advances the seek debounce timer.  Call once per frame from
    /// <see cref="OrchestratorSubsystem.Update"/>.
    /// </summary>
    public void Update(float dt)
    {
        if (!_seekPending) return;
        _seekDebounceTimer -= dt;
        if (_seekDebounceTimer > 0f) return;

        _seekPending = false;
        long wallTicks = (long)(_seekSliderValue * 10_000_000L);
        _sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ReplaySeek,
            PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
        });
    }

    /// <summary>
    /// Reads the replay duration in seconds from the drill's meta.json.
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
    /// Scans <c>C:\FDP_Temp</c> for asset folders.
    /// Subdirectories containing <c>*.fdp</c> files are drills;
    /// subdirectories containing <c>*.json</c> files are scenario/story packages.
    /// </summary>
    internal void RefreshLocalAssets(string? root = null)
    {
        root ??= @"C:\FDP_Temp";
        var scenarios = new List<string>();
        var drills    = new List<string>();

        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir)!;
                if (Directory.GetFiles(dir, "*.fdp").Length > 0)
                    drills.Add(name);
                else if (Directory.GetFiles(dir, "*.json").Length > 0)
                    scenarios.Add(name);
            }
        }

        _availableScenarios = scenarios.ToArray();
        _availableStories   = scenarios.ToArray();   // stories share scenario packages
        _availableDrills    = drills.ToArray();

        if (_selectedLoadScenarioIdx >= _availableScenarios.Length) _selectedLoadScenarioIdx = -1;
        if (_selectedStoryIdx        >= _availableStories.Length)   _selectedStoryIdx        = -1;
        if (_selectedDrillIdx        >= _availableDrills.Length)    _selectedDrillIdx        = -1;

        // Archive management lists (S0505)
        var gateway = _drillMaster.StorageGateway;
        var nasRoot = @"C:\FDP_Temp\nas";
        _archivedDrills = gateway?.ScanNasDrills(nasRoot).ToArray()
                          ?? Array.Empty<string>();
        var archivedSet = new HashSet<string>(_archivedDrills);
        _unarchivedLocalDrills = _availableDrills
                                 .Where(d => !archivedSet.Contains(d))
                                 .ToArray();

        if (_selectedArchiveIdx    >= _archivedDrills.Length)        _selectedArchiveIdx    = -1;
        if (_selectedUnarchivedIdx >= _unarchivedLocalDrills.Length) _selectedUnarchivedIdx = -1;
    }

    /// <summary>
    /// Renders all six control sections.  Must be called from within an active ImGui frame.
    /// </summary>
    public void Render(bool isPaused = false, float drillTime = 0f)
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
        RenderReplaySection(currentState, disableAll, isPaused, drillTime);

        // ── 6. Stories ─────────────────────────────────────────────────────
        RenderStoriesSection(disableAll);
        // ── 7. Archive Management ─────────────────────────────────────
        RenderArchiveSection(currentState, disableAll);    }

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
            ImGui.Combo("Select Scenario##OrcLoadId", ref _selectedLoadScenarioIdx,
                _availableScenarios, _availableScenarios.Length);
            ImGui.SameLine();
            if (ImGui.Button("⟳##RefScen")) RefreshLocalAssets();
            ImGui.SameLine();
            if (ImGui.Button("Load into Edit##OrcLoadEdit") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = _availableScenarios[_selectedLoadScenarioIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingEdit}," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Load into Live##OrcLoadLive") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = _availableScenarios[_selectedLoadScenarioIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void RenderReplaySection(DSMState currentState, bool disableAll,
        bool isPaused = false, float currentDrillTime = 0f)
    {
        if (!ImGui.CollapsingHeader("Replay")) return;

        if (ImGui.BeginChild("##OrcReplay", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();

            // Load Replay
            ImGui.Combo("Select Drill##OrcReplayId", ref _selectedDrillIdx,
                _availableDrills, _availableDrills.Length);
            ImGui.SameLine();
            if (ImGui.Button("⟳##RefDrill")) RefreshLocalAssets();
            ImGui.SameLine();
            if (ImGui.Button("Load Replay##OrcReplayBtn") && _selectedDrillIdx >= 0)
            {
                string drillId = _availableDrills[_selectedDrillIdx];

                // P3 debt: wire _replayDuration from the drill's meta.json (CGF1-BATCH-27 #2).
                string metaPath = Path.Combine(@"C:\FDP_Temp", drillId, "recording.meta.json");
                if (File.Exists(metaPath))
                    _replayDuration = GetReplayDuration(File.ReadAllText(metaPath));

                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.RunningReplay}," +
                                    $"\"DrillId\":\"{drillId}\"}}",
                });
            }

            // Seek slider — only when RunningReplay
            if (currentState == DSMState.RunningReplay)
            {
                // Passive tracking: keep slider in sync unless a seek is pending.
                if (!_seekPending)
                    _seekSliderValue = currentDrillTime;

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

    private void RenderArchiveSection(DSMState state, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Archive Management##OrcArchive")) return;

        bool archiveDisable = disableAll;

        // — Unarchived Local Drills —
        ImGui.Text("Unarchived Local:");
        ImGui.Combo("##UnarchivedCombo", ref _selectedUnarchivedIdx,
                    _unarchivedLocalDrills, _unarchivedLocalDrills.Length);
        ImGui.SameLine();
        if (ImGui.Button("⟳##RefreshUnarchived")) RefreshLocalAssets();

        if (archiveDisable || _selectedUnarchivedIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.BeginDisabled();
        if (ImGui.Button("Export to NAS ▶##OrcExport")
            && _selectedUnarchivedIdx >= 0
            && _activeArchiveOpId == Guid.Empty)
        {
            var drillName  = _unarchivedLocalDrills[_selectedUnarchivedIdx];
            var requestId  = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = requestId,
                OperationType = SysOpType.ExportArchive,
                PayloadJson   = $"{{\"DrillId\":\"{drillName}\"}}",
            });
        }
        if (archiveDisable || _selectedUnarchivedIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.EndDisabled();

        ImGui.Separator();

        // — Archived NAS Drills —
        ImGui.Text("Archived on NAS:");
        ImGui.Combo("##ArchivedCombo", ref _selectedArchiveIdx,
                    _archivedDrills, _archivedDrills.Length);
        ImGui.SameLine();
        if (ImGui.Button("⟳##RefreshArchived")) RefreshLocalAssets();

        if (archiveDisable || _selectedArchiveIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.BeginDisabled();
        if (ImGui.Button("Import from NAS ◄##OrcImport")
            && _selectedArchiveIdx >= 0
            && _activeArchiveOpId == Guid.Empty)
        {
            var drillName  = _archivedDrills[_selectedArchiveIdx];
            var requestId  = Guid.NewGuid();
            _activeArchiveOpId = requestId;
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = requestId,
                OperationType = SysOpType.ImportArchive,
                PayloadJson   = $"{{\"DrillId\":\"{drillName}\"}}",
            });
        }
        if (archiveDisable || _selectedArchiveIdx < 0 || _activeArchiveOpId != Guid.Empty)
            ImGui.EndDisabled();

        // — Progress / Cancel (visible while op in-flight) —
        if (_activeArchiveOpId != Guid.Empty)
        {
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0f, 1f));
            ImGui.Text("Archive operation in progress...");
            ImGui.PopStyleColor();
            ImGui.ProgressBar(-1f * (float)ImGui.GetTime(), new System.Numerics.Vector2(-1, 0), "");
            // Cancel is always active regardless of disableAll
            if (ImGui.Button("CANCEL OPERATION##OrcCancelArchive"))
            {
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.CancelOperation,
                    PayloadJson   = _activeArchiveOpId.ToString(),
                });
                _activeArchiveOpId = Guid.Empty;  // optimistic clear
            }
        }
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
            ImGui.Combo("Story Package##OrcInjectScen", ref _selectedStoryIdx,
                _availableStories, _availableStories.Length);
            ImGui.SameLine();
            if (ImGui.Button("⟳##RefStory")) RefreshLocalAssets();
            if (ImGui.Button("Inject Story##OrcInjectBtn") && _selectedStoryIdx >= 0)
            {
                string scenId     = _availableStories[_selectedStoryIdx];
                string newStoryId = Guid.NewGuid().ToString();
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.ManageStory,
                    PayloadJson   = $"{{\"Mode\":\"Start\"," +
                                    $"\"StoryId\":\"{newStoryId}\"," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }

            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }
}
