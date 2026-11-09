using System;
using NodeEditor.Core.Widgets;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Panels.ReplayBrowser.Drawers;
using Fdp.Presentation.Utils.ReplayBrowser;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser;

using ImGuiApi = ImGuiNET.ImGui;
using Fdp.Core.Serialization;

/// <summary>⭐ One match row, projected by hand (entity flattened to index/generation).</summary>
public sealed record ReplaySearchResultRowViewModel(int FrameIndex, long WallClockTicks, int EntityIndex, int EntityGeneration, string ContextMessage);

/// <summary>⭐ R-6 / V7 (docs/UX/DESIGN_Review_Perspective.md §4 V7) — one search hit as the Event Timeline draws it: the frame it is at, the entity handle (of the throwaway reader, as the results grid shows it) and what matched.</summary>
public readonly record struct SearchHit(int Frame, Entity Entity, string Context, long NetworkId = 0);

/// <summary>⭐ G-7 S5 (I6) — what makes the search FEDERATED: the merged view's manager (null outside it ⇒ the single-file search) and the <see cref="FederatedSearch"/> that runs per participant file, owned components only, unioned by network id.</summary>
public sealed record FederatedSearchSource(FederatedSearch Search, Func<FederatedReplayManager?> Manager);

/// <summary>⭐ One lifecycle-match row, projected by hand.</summary>
public sealed record ReplaySearchLifecycleRowViewModel(int EntityIndex, int EntityGeneration, int StartFrame, int EndFrame, string MatchContext);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ReplaySearchPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠ <b>Deliberately does not walk the predicate <c>Document.Root</c> field-by-field</b> — the
/// same StructEdit.Core generic edit-node tree <see cref="ComponentEditWindow"/> treats the same way
/// (a third-party editor's own model, not this sweep's to reimplement). This VM captures the
/// addressable/assertable state: which mode, the results, and the status/validation lines.</para>
/// </summary>
public sealed record ReplaySearchPanelViewModel(
    string PanelId,
    string PanelKind,
    string Mode,
    bool IsMergedViewActive,
    bool IsSearching,
    string StatusLine,
    string ValidationError,
    IReadOnlyList<ReplaySearchResultRowViewModel> Results,
    IReadOnlyList<ReplaySearchLifecycleRowViewModel> LifecycleResults) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Search panel for the replay browser. Hosts a StructEdit session editing a
/// <see cref="SearchPredicateDto"/> and fires seek/select intents on result clicks.
/// The panel stores the services in the constructor but opens the StructEdit session
/// lazily on the first <see cref="DrawContent"/> call.
/// </summary>
public sealed class ReplaySearchPanel
{
    // ⭐ CE-4219 ③ — the mode table is the ONE shared PredicateModes (beside the DTOs); this panel offers the 7 that make sense on a recording (SearchModes) — not BlueprintVariable / TraceBufferScan,
    //   which are conditions of a RUNNING node (a blueprint slot read, a live trace ring) the breakpoint builder offers.
    private static readonly SearchPredicateMode[] SearchModes =
    {
        SearchPredicateMode.Component, SearchPredicateMode.Event, SearchPredicateMode.Lifecycle,
        SearchPredicateMode.Spatial, SearchPredicateMode.Structural, SearchPredicateMode.Compound, SearchPredicateMode.BehaviorParam,
    };

    private readonly IComponentEditService _editService;
    private readonly IRecordingSearchService _searchService;
    private readonly Action<int> _onSeekRequested;
    private readonly Action<Entity> _onEntitySelected;
    private readonly Action<int, Entity> _onMatchSelected;
    private readonly BehaviorRegistry _behaviorRegistry;
    private readonly Func<Entity?>? _getSelectedEntity;
    private readonly Func<int?>? _getSelectedNetworkId;

    private SearchPredicateMode _mode = SearchPredicateMode.Component;
    /// <summary>⭐ U-16 / C5 (DESIGN_Condition_Editor §6) — the friendly view: the ONE condition editor in the Search context (the facts a recording can answer). The Expert view is today's mode radio + struct editor.</summary>
    private readonly Fdp.Presentation.Editing.ConditionEditor _conditions = new(Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionContexts.Search);
    private bool _expert;
    private SearchPredicateDto? _friendly;
    private IEditSession? _predicateSession;
    private ComponentEditDrawer? _componentEditDrawer;

    /// <summary>Set by the subsystem after loading a recording file.</summary>
    public string? CurrentFilePath { get; set; }

    // ⭐ R-6 / V7 — the hits as the Event Timeline's "Search hits" row reads them: a snapshot rebuilt only when a search lands (the version moves), never per frame
    private int _resultsVersion;
    private SearchHit[] _hits = Array.Empty<SearchHit>();
    private int _hitsVersion;

    /// <summary>⭐ R-6 / V7 — moves whenever the results change (a search started, landed or failed). A reader compares it and rebuilds only then.</summary>
    public int ResultsVersion => Volatile.Read(ref _resultsVersion);

    /// <summary>⭐ R-6 / V7 — the shown hits (a lifecycle search's by its start frame), the SAME rows the results grid lists. ⭐ A cached snapshot: rebuilt only when <see cref="ResultsVersion"/> moved, 0 B otherwise.</summary>
    public IReadOnlyList<SearchHit> Hits
    {
        get
        {
            lock (_resultsLock)
            {
                if (_hitsVersion == _resultsVersion) return _hits;
                var list = new SearchHit[_results.Count + _lifecycleResults.Count];
                int n = 0;
                for (int i = 0; i < _results.Count; i++) list[n++] = new SearchHit(_results[i].FrameIndex, _results[i].Entity, _results[i].ContextMessage, _results[i].NetworkId);
                for (int i = 0; i < _lifecycleResults.Count; i++) list[n++] = new SearchHit(_lifecycleResults[i].StartFrame, _lifecycleResults[i].Entity, _lifecycleResults[i].MatchContext);
                _hitsVersion = _resultsVersion;
                return _hits = list;
            }
        }
    }

    /// <summary>⭐ R-6 / V7 — a click on hit <paramref name="index"/> of <see cref="Hits"/> does what a click on its row in the grid does: the host's ONE match path (seek + select). False when out of range.</summary>
    public bool ActivateHit(int index)
    {
        var hits = Hits;
        if ((uint)index >= (uint)hits.Count) return false;
        Match(hits[index].Frame, hits[index].Entity, hits[index].NetworkId);
        return true;
    }

    /// <summary>⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §4) — when the host returns text, the panel draws only that (a host with no recording to search says so: <i>"Open an exercise recording (AAR) to search"</i>). Null / returning null ⇒ the panel draws as ever.</summary>
    public Func<string?>? UnavailableReason { get; set; }

    /// <summary>
    /// Whether the host is showing the Merged View (dumped in the view-model).
    /// ⭐ G-7 S5 (I6) — it no longer disables the panel: <see cref="Federated"/> makes the search run per participant file. ⛔ SUPERSEDED: <i>"Search is not available in Merged View."</i> (an overlay that returned early).
    /// </summary>
    public bool IsMergedViewActive { get; set; }

    /// <summary>⭐ G-7 S5 (I6) — the federated search. While its manager is non-null (the merged view) Execute runs per participant file; null ⇒ the single-file search (the file at <see cref="CurrentFilePath"/>).</summary>
    public FederatedSearchSource? Federated { get; set; }

    /// <summary>⭐ G-7 S5 (I6) — a click on a FEDERATED hit (a network id): the host seeks its frame (a frame of the primary node's recording) and selects the unit BY NETWORK ID — the hit's <c>Entity</c> is a handle in one participant's file, not in the shown world. Null ⇒ falls back to the match callback.</summary>
    public Action<int, long>? FederatedMatchSelected { get; set; }

    private void Match(int frame, Entity entity, long networkId)
    {
        if (networkId > 0 && FederatedMatchSelected != null) FederatedMatchSelected(frame, networkId);
        else _onMatchSelected(frame, entity);
    }

    /// <summary>
    /// ⭐ CE-4206 R2 — the ONE map pick context (P4) the predicate forms draw their reference leaves with: the spatial predicate's <c>AreaLink</c> area is edited on the map
    /// with handles through it (replacing the separate bounding-box pick context). Injected by the subsystem; null ⇒ the leaves draw their numeric fields only.
    /// </summary>
    public IMapPickContext? PickContext { get; set; }

    /// <summary>Exposes currently configured spatial bounds for map overlay rendering.</summary>
    public BoundingBox2D? ActiveSpatialBounds
    {
        get
        {
            if (_expert && _mode == SearchPredicateMode.Spatial && _predicateSession != null)
            {
                foreach (var child in _predicateSession.Document.Root.Children)
                {
                    if (child.Name == "Bounds" && child.Binding?.GetBoxed() is Fdp.Toolkit.Replication.AreaLink area && AreaBoxes.ToBox(area) is { } box)
                    {
                        if (box.Min != box.Max) return box;
                    }
                }
            }
            return null;
        }
    }

    // Per-mode DTO instances (survive mode switches so user-entered values are preserved) — created blank by the shared table on first use.
    private readonly Dictionary<SearchPredicateMode, SearchPredicateDto> _modeDtos = new();
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
        Func<int?>? getSelectedNetworkId = null)
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

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of mode/status/results. No ImGui. ⚠ Locks
    /// <c>_resultsLock</c> exactly as <see cref="DrawResultsGrid"/> does before reading the results
    /// lists, since a background search task can be writing them concurrently.</summary>
    public ReplaySearchPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        IReadOnlyList<SearchResultDto> results;
        IReadOnlyList<LifecycleSearchResultDto> lifecycleResults;
        lock (_resultsLock)
        {
            results = _results;
            lifecycleResults = _lifecycleResults;
        }

        bool isSearching = _searchTask != null && !_searchTask.IsCompleted;

        var resultRows = results.Select(r => new ReplaySearchResultRowViewModel(
            r.FrameIndex, r.WallClockTicks, r.Entity.Index, r.Entity.Generation, r.ContextMessage)).ToList();
        var lifecycleRows = lifecycleResults.Select(r => new ReplaySearchLifecycleRowViewModel(
            r.Entity.Index, r.Entity.Generation, r.StartFrame, r.EndFrame, r.MatchContext)).ToList();

        return new ReplaySearchPanelViewModel(
            panelId, panelKind, _mode.ToString(), IsMergedViewActive, isSearching,
            _statusLine, _validationError, resultRows, lifecycleRows);
    }

    // ── Public draw entry point ───────────────────────────────────────────

    public void DrawContent()
    {
        if (UnavailableReason?.Invoke() is { } unavailable) { ImGuiApi.TextDisabled(unavailable); return; }
        EnsureSession();
        if (_predicateSession == null || _componentEditDrawer == null)
            return;

        if (_predicateSession.RebuildState == EditRebuildState.RebuildRequired)
            _predicateSession.RebuildDocument();

        // ⭐ U-16 / C5 — the condition as sentence rows (the friendly view); "Expert view" shows today's mode radio + tree. The search panel edits a DTO of its own (no predicate node to bind), so the friendly view works through `ref _friendly` directly.
        bool expert = _expert;
        if (ImGuiApi.Checkbox("Expert view", ref expert)) SetExpert(expert);
        if (!_expert) TreeRowsDrawn = 0;
        if (!_expert)
        {
            _conditions.Draw("search-when", ref _friendly, PickContext);
            if (_conditions.ExpertRequested) { SetExpert(true); _conditions.ExpertRequested = false; }
            ImGuiApi.Spacing();
        }
        else
        {
        DrawModeRadio();
        DrawPresetToolbar();

        if (StructTable.Begin("SearchCriteria", headers: true, label: "Property"))   // ⭐ CE-3384 — the ONE struct-table chrome
        {

            TreeRowsDrawn = 0;
            foreach (var child in _predicateSession.Document.Root.Children)
            {
                _componentEditDrawer.DrawEditNode(child);
                TreeRowsDrawn++;
            }
            StructTable.End();
        }
        }

        DrawEntityFilter();
        DrawExecuteButton();
        DrawResultsGrid();
    }

    /// <summary>
    /// ⭐ U-16 — switches between the friendly view and the Expert view, carrying the ONE condition across: into Expert the friendly value opens in the struct editor (its mode radio follows the DTO's type);
    /// back to friendly the struct editor's DTO (never a Lifecycle one — that search has no sentence) becomes the friendly value.
    /// </summary>
    private void SetExpert(bool expert)
    {
        if (expert == _expert) return;
        if (expert)
        {
            if (_friendly != null && Array.IndexOf(SearchModes, PredicateModes.ModeOf(_friendly)) >= 0)
            {
                _mode = PredicateModes.ModeOf(_friendly);
                _predicateSession?.Dispose();
                _predicateSession    = _editService.Open(_friendly, _friendly.GetType());
                _componentEditDrawer = BuildDrawer(_predicateSession);
                _predicateSession.MarkStructuralChange();
                _predicateSession.RebuildDocument();
            }
        }
        else if (_predicateSession != null && _mode != SearchPredicateMode.Lifecycle && _predicateSession.Commit() is SearchPredicateDto dto)
            _friendly = dto;
        _expert = expert;
    }

    // ── Test seams ────────────────────────────────────────────────────────

    /// <summary>How many top-level struct-editor rows the Expert view drew last frame (0 while the friendly view shows) — rails.</summary>
    public int TreeRowsDrawn { get; private set; }
    /// <summary>The friendly view's sentence rows as drawn (rails).</summary>
    public IReadOnlyList<string> ConditionRows => _conditions.Sentences;
    /// <summary>The first unfilled blank of the friendly view, or null (the Execute button holds while it is set).</summary>
    public string? ConditionProblem => _expert ? null : _conditions.Problem;
    /// <summary>Whether the Expert view (the mode radio + struct editor) is showing.</summary>
    public bool ExpertView { get => _expert; set => SetExpert(value); }
    /// <summary>The friendly view's condition (what Execute searches while the Expert view is off).</summary>
    public SearchPredicateDto? FriendlyCondition { get => _friendly; set => _friendly = value; }

    /// <summary>⭐ CE-3378 — the drawer the Expert view builds for <paramref name="session"/> (the forwarding rail asks its catalogue). For unit testing only.</summary>
    internal ComponentEditDrawer DrawerFor(IEditSession session) => BuildDrawer(session);

    /// <summary>⭐ R-6 / V7 — lands a search's results as a finished search does (the version moves). For unit testing only.</summary>
    internal void SetResultsForTest(IReadOnlyList<SearchResultDto> results, IReadOnlyList<LifecycleSearchResultDto>? lifecycle = null)
    {
        lock (_resultsLock) { _results = results; _lifecycleResults = lifecycle ?? Array.Empty<LifecycleSearchResultDto>(); _resultsVersion++; }
    }

    /// <summary>⭐ G-7 S5 — opens the predicate session the way the first <see cref="DrawContent"/> does, so a rail can <see cref="StartSearch"/> without an ImGui frame. For unit testing only.</summary>
    internal void PrepareForTest() => EnsureSession();

    /// <summary>The search task, for a rail to await. For unit testing only.</summary>
    internal System.Threading.Tasks.Task? SearchTaskForTest => _searchTask;

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

    private object DtoForMode(SearchPredicateMode mode)
    {
        if (Array.IndexOf(SearchModes, mode) < 0) mode = SearchPredicateMode.Component;
        if (!_modeDtos.TryGetValue(mode, out var dto)) _modeDtos[mode] = dto = PredicateModes.CreateDefault(mode);
        return dto;
    }

    // ⭐ CE-3378 (N-1) — the search's cells come from ONE catalogue overlay per mode, built once and kept (the Type combo lists components or events by mode); its session-bound editors read
    //   ctx.Session / ctx.Get<BehaviorRegistry>(), so nothing is rebuilt per session or per frame
    private Fdp.Presentation.Editing.Types.TypeCatalogue? _componentTypes, _eventTypes;
    private Fdp.Presentation.Editing.Types.ServiceBag? _drawServices;

    private Fdp.Presentation.Editing.Types.TypeCatalogue BuildCatalogue(TypeComboMode typeMode)
    {
        var types = Fdp.Presentation.Editing.Types.TypeCatalogue.Standard.With(
            new Fdp.Presentation.Editing.Types.FieldTypeEditor(typeof(Type), null, new FilteredTypeComboFieldDrawer(typeMode)));
        // ⭐ CE-308: int has TWO pickers — the behaviour-hash combo and the occurrence SLOT combo — routed on their attributes (they were one IntPickerRouterFieldDrawer on the type)
        types.AddRule(typeof(WorkingSlotPickerAttribute), new WorkingSlotTypeEditor());
        types.AddRule(typeof(BehaviorHashPickerAttribute), new Fdp.Presentation.Editing.Types.FieldTypeEditor(typeof(int), null, new BehaviorHashFieldDrawer(_behaviorRegistry)));
        types.AddRule(typeof(PropertyPathPickerAttribute), new PropertyPathTypeEditor());
        return types;
    }

    private ComponentEditDrawer BuildDrawer(IEditSession session)
    {
        var types = _mode == SearchPredicateMode.Event ? (_eventTypes ??= BuildCatalogue(TypeComboMode.Event)) : (_componentTypes ??= BuildCatalogue(TypeComboMode.Component));
        return new ComponentEditDrawer(session, PickContext, types: types, services: _drawServices ??= new Fdp.Presentation.Editing.Types.ServiceBag().With(_behaviorRegistry),
            defaults: TypeDefaults.For(session.Document.Root.ClrType));
    }

    private void DrawModeRadio()
    {
        var modes = SearchModes;
        string[] labels = Array.ConvertAll(modes, PredicateModes.Label);
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
                _presetJson = JsonSerializer.Serialize(dto, FdpJsonOptionsRegistry.DefaultRelaxed);
            }
        }

        ImGuiApi.SameLine();

        if (ImGuiApi.Button("Load Preset") && !string.IsNullOrEmpty(_presetJson))
        {
            var dto = JsonSerializer.Deserialize<SearchPredicateDto>(_presetJson, FdpJsonOptionsRegistry.DefaultRelaxed);
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

    private static readonly string[] TargetLabels = { "Target Network ID", "Index", "Generation" };

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

            if (FormGrid.Begin("replay.search.target", TargetLabels))
            {
                if (_targetUseNetworkId)
                {
                    FormGrid.Row("Target Network ID"); ImGuiApi.InputText("##targetnetid", ref _targetNetworkIdStr, 32);
                }
                else
                {
                    FormGrid.Row("Index");      ImGuiApi.InputInt("##targetindex", ref _targetIndex);
                    FormGrid.Row("Generation"); ImGuiApi.InputInt("##targetgeneration", ref _targetGeneration);
                }
                FormGrid.End();
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
            bool blank = ConditionProblem != null;   // ⭐ U-16 — an unfilled blank: the search cannot run (DESIGN_Condition_Editor §4)
            if (blank) ImGuiApi.BeginDisabled();
            bool execute = ImGuiApi.Button("Execute Search");
            if (blank) { ImGuiApi.EndDisabled(); if (ImGuiApi.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGuiApi.SetTooltip(ConditionProblem!); }
            if (execute) StartSearch();
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

    /// <summary>
    /// ⭐ The Execute button's body (extracted so a rail drives the search without a click): validates the condition, then runs it on a worker — over the file at <see cref="CurrentFilePath"/>, or, ⭐ G-7 S5 (I6),
    /// over EVERY participant file through <see cref="Federated"/> while the merged view is shown.
    /// </summary>
    internal void StartSearch()
    {
        var federatedManager = Federated?.Manager();   // ⭐ G-7 S5 (I6) — the merged view searches EVERY participant file
        string path = CurrentFilePath ?? string.Empty;
        if (federatedManager != null) path = federatedManager.Contexts.TryGetValue(federatedManager.LocalEntitiesProviderNodeId, out var pc) ? pc.CurrentFdpPath ?? string.Empty : string.Empty;
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

        object? dtoOrNone = _expert ? _predicateSession.Commit() : _friendly;
        if (dtoOrNone == null)
        {
            _validationError = "Add at least one condition.";
            _validationErrorTimer = 3.0f;
            return;
        }
        object dto = dtoOrNone;

        if (!ValidateDto(dto, out string errorMsg))
        {
            _validationError = errorMsg;
            _validationErrorTimer = 3.0f;
            return;
        }

        _validationError = string.Empty;
        _validationErrorTimer = 0f;
        _statusLine       = "Searching...";
        lock (_resultsLock) { _results = Array.Empty<SearchResultDto>(); _lifecycleResults = Array.Empty<LifecycleSearchResultDto>(); _resultsVersion++; }

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
                NetworkId = int.TryParse(_targetNetworkIdStr, out int nid) ? nid : 0
            };
        }

        if (_expert && _mode == SearchPredicateMode.Lifecycle)
        {
            var pred = (LifecyclePredicateDto)dto;
            _searchTask = Task.Run(() =>
            {
                try
                {
                    var r = _searchService.ExecuteLifecycleSearch(path, pred, entityFilter, token);
                    lock (_resultsLock)
                    {
                        _lifecycleResults = r; _resultsVersion++;
                        _statusLine = token.IsCancellationRequested
                            ? $"Search cancelled. Yielded {r.Count} partial result(s)."
                            : $"Found {r.Count} lifecycle event(s).";
                    }
                }
                catch (Exception ex)
                {
                    lock (_resultsLock)
                    {
                        _lifecycleResults = Array.Empty<LifecycleSearchResultDto>(); _resultsVersion++;
                        _statusLine = $"Search failed: {ex.GetType().Name}: {ex.Message}";
                    }
                }
            });
        }
        else
        {
            var pred = (SearchPredicateDto)dto;
            // ⭐ G-7 S5 (I6) — what the federated search needs, captured on the UI thread: each participant's file, the offsets, the primary node
            (int Node, string Path)[]? parts = null; Dictionary<int, long>? offsets = null; int primaryNode = 0;
            if (federatedManager != null)
            {
                var list = new List<(int, string)>();
                foreach (var kv in federatedManager.Contexts) if (kv.Value.CurrentFdpPath is { } fp) list.Add((kv.Key, fp));
                parts = list.ToArray(); offsets = new Dictionary<int, long>(federatedManager.NodeOffsets); primaryNode = federatedManager.LocalEntitiesProviderNodeId;
            }
            var federated = Federated?.Search;
            _searchTask = Task.Run(() =>
            {
                try
                {
                    var r = parts != null && federated != null
                        ? federated.Execute(parts, offsets!, primaryNode, pred, entityFilter, token)
                        : _searchService.ExecuteSearch(path, pred, entityFilter, token);
                    lock (_resultsLock)
                    {
                        _results = r; _resultsVersion++;
                        _statusLine = token.IsCancellationRequested
                            ? $"Search cancelled. Yielded {r.Count} partial result(s)."
                            : $"Found {r.Count} result(s).";
                    }
                }
                catch (Exception ex)
                {
                    lock (_resultsLock)
                    {
                        _results = Array.Empty<SearchResultDto>(); _resultsVersion++;
                        _statusLine = $"Search failed: {ex.GetType().Name}: {ex.Message}";
                    }
                }
            });
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

        if (_expert && _mode == SearchPredicateMode.Lifecycle)
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
                    Match(r.FrameIndex, r.Entity, r.NetworkId);
                ImGuiApi.TableSetColumnIndex(1);
                if (ImGuiEntityLink.Draw(r.NetworkId > 0 ? $"net {r.NetworkId}" : r.Entity.ToString()))
                    Match(r.FrameIndex, r.Entity, r.NetworkId);
                ImGuiApi.TableSetColumnIndex(2);
                ImGuiApi.TextUnformatted(r.ContextMessage);
                ImGuiApi.PopID();
            }
            ImGuiApi.EndTable();
        }
    }
}
