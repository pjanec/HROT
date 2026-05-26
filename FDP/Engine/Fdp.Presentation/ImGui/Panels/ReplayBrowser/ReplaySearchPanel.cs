using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Panels.ReplayBrowser.Drawers;
using Fdp.Presentation.Utils.ReplayBrowser;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Search panel for the replay browser. Hosts a StructEdit session editing a
/// <see cref="SearchPredicateDto"/> and fires seek/select intents on result clicks.
/// The panel stores the services in the constructor but opens the StructEdit session
/// lazily on the first <see cref="DrawContent"/> call.
/// </summary>
public sealed class ReplaySearchPanel
{
    private enum SearchMode { Component, Event, Lifecycle, Spatial, Structural, Compound, BehaviorParam }

    private readonly IComponentEditService _editService;
    private readonly IRecordingSearchService _searchService;
    private readonly Action<int> _onSeekRequested;
    private readonly Action<Entity> _onEntitySelected;
    private readonly Action<int, Entity> _onMatchSelected;
    private readonly BehaviorRegistry _behaviorRegistry;
    private readonly Func<Entity?>? _getSelectedEntity;
    private readonly Func<long?>? _getSelectedNetworkId;

    private SearchMode _mode = SearchMode.Component;
    private IEditSession? _predicateSession;
    private ComponentEditDrawer? _componentEditDrawer;

    /// <summary>Set by the subsystem after loading a recording file.</summary>
    public string? CurrentFilePath { get; set; }

    /// <summary>
    /// When true the search panel is in Merged View mode.
    /// DrawContent renders a disabled overlay and returns early.
    /// </summary>
    public bool IsMergedViewActive { get; set; }

    /// <summary>Optional spatial picker context; injected by the subsystem when needed.</summary>
    public ISpatialPickerContext? SpatialPickerCtx { get; set; }

    /// <summary>Exposes currently configured spatial bounds for map overlay rendering.</summary>
    public BoundingBox2D? ActiveSpatialBounds
    {
        get
        {
            if (_mode == SearchMode.Spatial && _predicateSession != null)
            {
                foreach (var child in _predicateSession.Document.Root.Children)
                {
                    if (child.Name == "Bounds" && child.Binding?.GetBoxed() is BoundingBox2D box)
                    {
                        if (box.Min != box.Max) return box;
                    }
                }
            }
            return null;
        }
    }

    // Per-mode DTO instances (survive mode switches so user-entered values are preserved).
    private readonly PropertyMatchDto _componentDto = new();
    private readonly TransientEventPredicateDto _eventDto = new();
    private readonly LifecyclePredicateDto _lifecycleDto = new();
    private readonly SpatialBoundingPredicateDto _spatialDto = new();
    private readonly StructuralPredicateDto _structuralDto = new();
    private readonly CompoundPredicateDto _compoundDto = new();
    private readonly BehaviorParamPredicateDto _behaviorParamDto = new();
    private bool _filterTargetEntity;
    private bool _targetUseNetworkId;
    private int _targetIndex;
    private int _targetGeneration = 1;
    private string _targetNetworkIdStr = "0";

    // Search results
    private Task? _searchTask;
    private CancellationTokenSource? _searchCts;
    private readonly object _resultsLock = new();
    private IReadOnlyList<SearchResultDto> _results = Array.Empty<SearchResultDto>();
    private IReadOnlyList<LifecycleSearchResultDto> _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();
    private string _statusLine = string.Empty;
    private string _validationError = string.Empty;
    private float _validationErrorTimer = 0f;

    // Preset JSON (clipboard-style; stored in memory for load-back).
    private string _presetJson = string.Empty;

    public ReplaySearchPanel(
        IComponentEditService editService,
        IRecordingSearchService searchService,
        Action<int> onSeekRequested,
        Action<Entity> onEntitySelected,
        Action<int, Entity> onMatchSelected,
        BehaviorRegistry? behaviorRegistry = null,
        Func<Entity?>? getSelectedEntity = null,
        Func<long?>? getSelectedNetworkId = null)
    {
        _editService      = editService      ?? throw new ArgumentNullException(nameof(editService));
        _searchService    = searchService    ?? throw new ArgumentNullException(nameof(searchService));
        _onSeekRequested  = onSeekRequested  ?? throw new ArgumentNullException(nameof(onSeekRequested));
        _onEntitySelected = onEntitySelected ?? throw new ArgumentNullException(nameof(onEntitySelected));
        _onMatchSelected  = onMatchSelected  ?? throw new ArgumentNullException(nameof(onMatchSelected));
        _behaviorRegistry = behaviorRegistry ?? new BehaviorRegistry();
        _getSelectedEntity = getSelectedEntity;
        _getSelectedNetworkId = getSelectedNetworkId;
    }

    // ── Public draw entry point ───────────────────────────────────────────

    public void DrawContent()
    {
        if (IsMergedViewActive)
        {
            ImGuiApi.BeginDisabled();
            ImGuiApi.Text("Search is not available in Merged View.");
            ImGuiApi.EndDisabled();
            return;
        }
        EnsureSession();
        if (_predicateSession == null || _componentEditDrawer == null)
            return;

        if (_predicateSession.RebuildState == EditRebuildState.RebuildRequired)
            _predicateSession.RebuildDocument();

        DrawModeRadio();
        DrawPresetToolbar();

        var tableFlags = ImGuiTableFlags.Borders
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.Resizable
            | ImGuiTableFlags.SizingFixedFit;

        if (ImGuiApi.BeginTable("SearchCriteria", 2, tableFlags))
        {
            ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGuiApi.TableSetupColumn("Value",    ImGuiTableColumnFlags.WidthStretch);
            ImGuiApi.TableHeadersRow();

            foreach (var child in _predicateSession.Document.Root.Children)
            {
                _componentEditDrawer.DrawEditNode(child);
            }
            ImGuiApi.EndTable();
        }

        DrawEntityFilter();
        DrawExecuteButton();
        DrawResultsGrid();
    }

    // ── Test seams ────────────────────────────────────────────────────────

    /// <summary>Simulates clicking a seek (Frame N) button. For unit testing only.</summary>
    internal void InvokeSeekRequested(int frameIndex) => _onSeekRequested(frameIndex);

    /// <summary>Simulates clicking an entity deep-link. For unit testing only.</summary>
    internal void InvokeEntitySelected(Entity entity) => _onEntitySelected(entity);

    // ── Private helpers ───────────────────────────────────────────────────

    private void EnsureSession()
    {
        if (_predicateSession != null)
            return;
        object dto = DtoForMode(_mode);
        _predicateSession    = _editService.Open(dto, dto.GetType());
        _componentEditDrawer = BuildDrawer(_predicateSession);
    }

    private object DtoForMode(SearchMode mode) => mode switch
    {
        SearchMode.Component   => _componentDto,
        SearchMode.Event       => _eventDto,
        SearchMode.Lifecycle   => _lifecycleDto,
        SearchMode.Spatial     => _spatialDto,
        SearchMode.Structural  => _structuralDto,
        SearchMode.Compound    => _compoundDto,
        SearchMode.BehaviorParam => _behaviorParamDto,
        _                      => _componentDto
    };

    private ComponentEditDrawer BuildDrawer(IEditSession session)
    {
        var typeMode = _mode == SearchMode.Event ? TypeComboMode.Event : TypeComboMode.Component;
        var drawers = new Dictionary<Type, IImGuiFieldDrawer>
        {
            [typeof(BoundingBox2D)] = new BoundingBoxFieldDrawer(),
            [typeof(int)]           = new BehaviorHashFieldDrawer(_behaviorRegistry),
            [typeof(Type)]          = new FilteredTypeComboFieldDrawer(typeMode),
            [typeof(string)]        = new PropertyPathFieldDrawer(session, _behaviorRegistry),
            [typeof(SearchPredicateDto)] = new PredicateValueFieldDrawer(session, _behaviorRegistry),
        };
        return new ComponentEditDrawer(session, pickerCtx: null, drawers, SpatialPickerCtx);
    }

    private void DrawModeRadio()
    {
        SearchMode[] modes =
        {
            SearchMode.Component, SearchMode.Event, SearchMode.Lifecycle,
            SearchMode.Spatial, SearchMode.Structural, SearchMode.Compound, SearchMode.BehaviorParam
        };
        string[] labels = { "Component", "Event", "Lifecycle", "Spatial", "Structural", "Compound", "Behavior Param" };
        string[] tooltips =
        {
            "Search for exact field value matches on a specific component (or any change).",
            "Scan the transient event bus for occurrences or specific payload matches.",
            "Find the exact birth and death frames where a specific entity was alive.",
            "Detect when entities enter or exit a designated 2D bounding box.",
            "Find frames where a specific component type was added to or removed from an entity.",
            "Combine multiple search criteria using AND/OR logical trees.",
            "Evaluate fields mapped over unmanaged AI blackboard memory blocks."
        };

        for (int i = 0; i < modes.Length; i++)
        {
            if (i > 0) ImGuiApi.SameLine();
            if (ImGuiApi.RadioButton(labels[i], _mode == modes[i]))
            {
                if (_mode != modes[i])
                {
                    _mode = modes[i];
                    _predicateSession?.Dispose();
                    _predicateSession    = null;
                    _componentEditDrawer = null;
                    EnsureSession();
                }
            }

            if (ImGuiApi.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
            {
                ImGuiApi.SetTooltip(tooltips[i]);
            }
        }
    }

    private void DrawPresetToolbar()
    {
        if (ImGuiApi.Button("Save Preset"))
        {
            if (_predicateSession != null)
            {
                object dto = _predicateSession.Commit();
                _presetJson = JsonSerializer.Serialize(dto,
                    new JsonSerializerOptions { WriteIndented = false });
            }
        }

        ImGuiApi.SameLine();

        if (ImGuiApi.Button("Load Preset") && !string.IsNullOrEmpty(_presetJson))
        {
            var dto = JsonSerializer.Deserialize<SearchPredicateDto>(_presetJson);
            if (dto != null)
            {
                _predicateSession?.Dispose();
                _predicateSession    = _editService.Open(dto, dto.GetType());
                _componentEditDrawer = BuildDrawer(_predicateSession);
                _predicateSession.MarkStructuralChange();
                _predicateSession.RebuildDocument();
            }
        }
    }

    private void DrawEntityFilter()
    {
        ImGuiApi.Separator();
        ImGuiApi.Checkbox("Filter by Target Entity", ref _filterTargetEntity);

        if (_filterTargetEntity)
        {
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Fill from Selected") && _getSelectedEntity != null)
            {
                var e = _getSelectedEntity();
                if (e != null && !e.Value.IsNull)
                {
                    _targetIndex = e.Value.Index;
                    _targetGeneration = e.Value.Generation;

                    if (_getSelectedNetworkId != null)
                    {
                        var netId = _getSelectedNetworkId();
                        if (netId.HasValue)
                        {
                            _targetUseNetworkId = true;
                            _targetNetworkIdStr = netId.Value.ToString();
                        }
                        else
                        {
                            _targetUseNetworkId = false;
                        }
                    }
                    else
                    {
                        _targetUseNetworkId = false;
                    }
                }
            }

            if (ImGuiApi.RadioButton("ECS Handle", !_targetUseNetworkId)) _targetUseNetworkId = false;
            ImGuiApi.SameLine();
            if (ImGuiApi.RadioButton("Network ID", _targetUseNetworkId)) _targetUseNetworkId = true;

            if (_targetUseNetworkId)
            {
                ImGuiApi.InputText("Target Network ID", ref _targetNetworkIdStr, 32);
            }
            else
            {
                ImGuiApi.SetNextItemWidth(100f);
                ImGuiApi.InputInt("Index", ref _targetIndex);
                ImGuiApi.SameLine();
                ImGuiApi.SetNextItemWidth(100f);
                ImGuiApi.InputInt("Generation", ref _targetGeneration);
            }
        }
        ImGuiApi.Separator();
    }

    private bool ValidateDto(object dto, out string errorMessage)
    {
        switch (dto)
        {
            case PropertyMatchDto pm when pm.ComponentType == null:
                errorMessage = "Select a valid Component Type.";
                return false;
            case StructuralPredicateDto st when st.ComponentType == null:
                errorMessage = "Select a valid Component Type.";
                return false;
            case TransientEventPredicateDto te when te.EventType == null:
                errorMessage = "Select a valid Event Type.";
                return false;
            case BehaviorParamPredicateDto bp when bp.BehaviorId == 0:
                errorMessage = "Select a valid Behavior.";
                return false;
            case SpatialBoundingPredicateDto sp when sp.PositionComponentType == null:
                errorMessage = "Select a valid Position Component Type.";
                return false;
            case LifecyclePredicateDto lp when lp.IdentifierType == EntityIdentifierType.NameSubstring && lp.NameComponentType == null:
                errorMessage = "Select a valid Name Component Type.";
                return false;
            case CompoundPredicateDto cp:
                if (cp.Conditions.Count == 0)
                {
                    errorMessage = "Add at least one condition to the Compound search.";
                    return false;
                }
                foreach (var cond in cp.Conditions)
                {
                    if (!ValidateDto(cond, out errorMessage))
                        return false;
                }
                break;
        }

        errorMessage = string.Empty;
        return true;
    }

    private void DrawExecuteButton()
    {
        if (_validationErrorTimer > 0f)
        {
            _validationErrorTimer -= ImGuiApi.GetIO().DeltaTime;
            if (_validationErrorTimer <= 0f)
                _validationError = string.Empty;
        }

        bool isSearching = _searchTask != null && !_searchTask.IsCompleted;

        if (isSearching)
        {
            if (ImGuiApi.Button("Cancel Search"))
            {
                _searchCts?.Cancel();
                _statusLine = "Cancelling...";
            }
        }
        else
        {
            if (ImGuiApi.Button("Execute Search"))
            {
                string path = CurrentFilePath ?? string.Empty;
                if (string.IsNullOrEmpty(path))
                {
                    _statusLine = "No recording loaded.";
                    return;
                }

                if (_predicateSession == null)
                {
                    _statusLine = "No search criteria.";
                    return;
                }

                object dto = _predicateSession.Commit();

                if (!ValidateDto(dto, out string errorMsg))
                {
                    _validationError = errorMsg;
                    _validationErrorTimer = 3.0f;
                    return;
                }

                _validationError = string.Empty;
                _validationErrorTimer = 0f;
                _statusLine       = "Searching...";
                _results          = Array.Empty<SearchResultDto>();
                _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();

                _searchCts?.Dispose();
                _searchCts = new CancellationTokenSource();
                var token = _searchCts.Token;
                TargetEntityFilter? entityFilter = null;
                if (_filterTargetEntity)
                {
                    entityFilter = new TargetEntityFilter
                    {
                        UseNetworkId = _targetUseNetworkId,
                        Index = _targetIndex,
                        Generation = _targetGeneration,
                        NetworkId = long.TryParse(_targetNetworkIdStr, out long nid) ? nid : 0
                    };
                }

                if (_mode == SearchMode.Lifecycle)
                {
                    var pred = (LifecyclePredicateDto)dto;
                    _searchTask = Task.Run(() =>
                    {
                        try
                        {
                            var r = _searchService.ExecuteLifecycleSearch(path, pred, entityFilter, token);
                            lock (_resultsLock)
                            {
                                _lifecycleResults = r;
                                _statusLine = token.IsCancellationRequested
                                    ? $"Search cancelled. Yielded {r.Count} partial result(s)."
                                    : $"Found {r.Count} lifecycle event(s).";
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (_resultsLock)
                            {
                                _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();
                                _statusLine = $"Search failed: {ex.GetType().Name}: {ex.Message}";
                            }
                        }
                    });
                }
                else
                {
                    var pred = (SearchPredicateDto)dto;
                    _searchTask = Task.Run(() =>
                    {
                        try
                        {
                            var r = _searchService.ExecuteSearch(path, pred, entityFilter, token);
                            lock (_resultsLock)
                            {
                                _results = r;
                                _statusLine = token.IsCancellationRequested
                                    ? $"Search cancelled. Yielded {r.Count} partial result(s)."
                                    : $"Found {r.Count} result(s).";
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (_resultsLock)
                            {
                                _results = Array.Empty<SearchResultDto>();
                                _statusLine = $"Search failed: {ex.GetType().Name}: {ex.Message}";
                            }
                        }
                    });
                }
            }
        }

        if (!string.IsNullOrEmpty(_validationError))
        {
            ImGuiApi.SameLine();
            ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), _validationError);
        }
        else
        {
            string statusSnapshot;
            lock (_resultsLock) { statusSnapshot = _statusLine; }
            if (!string.IsNullOrEmpty(statusSnapshot))
            {
                ImGuiApi.SameLine();
                ImGuiApi.TextDisabled(statusSnapshot);
            }
        }
    }

    private void DrawResultsGrid()
    {
        IReadOnlyList<SearchResultDto> resultsSnapshot;
        IReadOnlyList<LifecycleSearchResultDto> lifecycleResultsSnapshot;
        lock (_resultsLock)
        {
            resultsSnapshot          = _results;
            lifecycleResultsSnapshot = _lifecycleResults;
        }

        var tableFlags = ImGuiTableFlags.Borders
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollY;

        if (_mode == SearchMode.Lifecycle)
        {
            if (!ImGuiApi.BeginTable("SearchResults", 4, tableFlags))
                return;

            ImGuiApi.TableSetupColumn("Frame");
            ImGuiApi.TableSetupColumn("Entity");
            ImGuiApi.TableSetupColumn("End Frame");
            ImGuiApi.TableSetupColumn("Context");
            ImGuiApi.TableHeadersRow();

            var results = lifecycleResultsSnapshot;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                ImGuiApi.PushID(i);
                ImGuiApi.TableNextRow();
                ImGuiApi.TableSetColumnIndex(0);
                if (ImGuiApi.SmallButton($"Frame {r.StartFrame}##seek{i}"))
                    _onMatchSelected(r.StartFrame, r.Entity);
                ImGuiApi.TableSetColumnIndex(1);
                if (ImGuiEntityLink.Draw(r.Entity.ToString()))
                    _onMatchSelected(r.StartFrame, r.Entity);
                ImGuiApi.TableSetColumnIndex(2);
                ImGuiApi.Text(r.EndFrame.ToString());
                ImGuiApi.TableSetColumnIndex(3);
                ImGuiApi.TextUnformatted(r.MatchContext);
                ImGuiApi.PopID();
            }
            ImGuiApi.EndTable();
        }
        else
        {
            if (!ImGuiApi.BeginTable("SearchResults", 3, tableFlags))
                return;

            ImGuiApi.TableSetupColumn("Frame");
            ImGuiApi.TableSetupColumn("Entity");
            ImGuiApi.TableSetupColumn("Event Type / Context");
            ImGuiApi.TableHeadersRow();

            var results = resultsSnapshot;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                ImGuiApi.PushID(i);
                ImGuiApi.TableNextRow();
                ImGuiApi.TableSetColumnIndex(0);
                if (ImGuiApi.SmallButton($"Frame {r.FrameIndex}##seek{i}"))
                    _onMatchSelected(r.FrameIndex, r.Entity);
                ImGuiApi.TableSetColumnIndex(1);
                if (ImGuiEntityLink.Draw(r.Entity.ToString()))
                    _onMatchSelected(r.FrameIndex, r.Entity);
                ImGuiApi.TableSetColumnIndex(2);
                ImGuiApi.TextUnformatted(r.ContextMessage);
                ImGuiApi.PopID();
            }
            ImGuiApi.EndTable();
        }
    }
}
