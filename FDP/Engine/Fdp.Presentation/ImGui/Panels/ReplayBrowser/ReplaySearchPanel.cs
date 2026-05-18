using System;
using System.Collections.Generic;
using System.Text.Json;
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
    private readonly BehaviorRegistry _behaviorRegistry;

    private SearchMode _mode = SearchMode.Component;
    private IEditSession? _predicateSession;
    private ComponentEditDrawer? _componentEditDrawer;

    /// <summary>Set by the subsystem after loading a recording file.</summary>
    public string? CurrentFilePath { get; set; }

    /// <summary>Optional spatial picker context; injected by the subsystem when needed.</summary>
    public ISpatialPickerContext? SpatialPickerCtx { get; set; }

    // Per-mode DTO instances (survive mode switches so user-entered values are preserved).
    private readonly PropertyMatchDto _componentDto = new();
    private readonly TransientEventPredicateDto _eventDto = new();
    private readonly LifecyclePredicateDto _lifecycleDto = new();
    private readonly SpatialBoundingPredicateDto _spatialDto = new();
    private readonly StructuralPredicateDto _structuralDto = new();
    private readonly CompoundPredicateDto _compoundDto = new();
    private readonly BehaviorParamPredicateDto _behaviorParamDto = new();

    // Search results
    private Task? _searchTask;
    private readonly object _resultsLock = new();
    private IReadOnlyList<SearchResultDto> _results = Array.Empty<SearchResultDto>();
    private IReadOnlyList<LifecycleSearchResultDto> _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();
    private string _statusLine = string.Empty;

    // Preset JSON (clipboard-style; stored in memory for load-back).
    private string _presetJson = string.Empty;

    public ReplaySearchPanel(
        IComponentEditService editService,
        IRecordingSearchService searchService,
        Action<int> onSeekRequested,
        Action<Entity> onEntitySelected,
        BehaviorRegistry? behaviorRegistry = null)
    {
        _editService      = editService      ?? throw new ArgumentNullException(nameof(editService));
        _searchService    = searchService    ?? throw new ArgumentNullException(nameof(searchService));
        _onSeekRequested  = onSeekRequested  ?? throw new ArgumentNullException(nameof(onSeekRequested));
        _onEntitySelected = onEntitySelected ?? throw new ArgumentNullException(nameof(onEntitySelected));
        _behaviorRegistry = behaviorRegistry ?? new BehaviorRegistry();
    }

    // ── Public draw entry point ───────────────────────────────────────────

    public void DrawContent()
    {
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

            _componentEditDrawer.DrawEditNode(_predicateSession.Document.Root);
            ImGuiApi.EndTable();
        }

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

    private void DrawExecuteButton()
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
            _statusLine       = "Searching...";
            _results          = Array.Empty<SearchResultDto>();
            _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();

            if (_mode == SearchMode.Lifecycle)
            {
                var pred = (LifecyclePredicateDto)dto;
                _searchTask = Task.Run(() =>
                {
                    try
                    {
                        var r = _searchService.ExecuteLifecycleSearch(path, pred);
                        lock (_resultsLock)
                        {
                            _lifecycleResults = r;
                            _statusLine       = $"Found {r.Count} lifecycle event(s).";
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
                        var r = _searchService.ExecuteSearch(path, pred);
                        lock (_resultsLock)
                        {
                            _results    = r;
                            _statusLine = $"Found {r.Count} result(s).";
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

        string statusSnapshot;
        lock (_resultsLock) { statusSnapshot = _statusLine; }
        if (!string.IsNullOrEmpty(statusSnapshot))
        {
            ImGuiApi.SameLine();
            ImGuiApi.TextDisabled(statusSnapshot);
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
                    _onSeekRequested(r.StartFrame);
                ImGuiApi.TableSetColumnIndex(1);
                if (ImGuiEntityLink.Draw(r.Entity.ToString()))
                {
                    _onEntitySelected(r.Entity);
                    _onSeekRequested(r.StartFrame);
                }
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
                    _onSeekRequested(r.FrameIndex);
                ImGuiApi.TableSetColumnIndex(1);
                if (ImGuiEntityLink.Draw(r.Entity.ToString()))
                {
                    _onEntitySelected(r.Entity);
                    _onSeekRequested(r.FrameIndex);
                }
                ImGuiApi.TableSetColumnIndex(2);
                ImGuiApi.TextUnformatted(r.ContextMessage);
                ImGuiApi.PopID();
            }
            ImGuiApi.EndTable();
        }
    }
}
