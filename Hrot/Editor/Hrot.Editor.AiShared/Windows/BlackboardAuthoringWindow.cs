using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

// ---- View-model types (internal; rendered by DrawClientArea) ----------------

/// <summary>
/// Display data for one blackboard variable row in the authoring window.
/// </summary>
public sealed record VariableViewModel(
    string Name,
    string TypeName,
    int    ByteSize,
    Type   FieldType,
    string? Comment,
    IReadOnlyList<(string AssetName, Guid AssetId, Guid ElementId)> AliasedBy,
    bool   IsUnused,
    bool   IsAutoManaged = false,
    bool   IsReadOnly = false);

/// <summary>
/// Display data for one unbound sub-tree DTO requirement row (BB SS5.6).
/// </summary>
public sealed record UnboundRequirementViewModel(
    string DtoTypeName,
    string RequiredByPath,
    Guid   RequiringAssetId,
    Guid   RequiringElementId,
    Type   DtoType,
    string RequiringAssetName);

/// <summary>
/// All data needed to render the Blackboard Variables panel for the current frame.
/// Built by <see cref="BlackboardAuthoringWindow.BuildViewModel"/> which is
/// side-effect-free and unit-testable without ImGui.
/// </summary>
public sealed record BlackboardWindowViewModel(
    bool HasActiveAsset,
    bool IsBlackboardEditorManaged,
    int  TotalInlineBytes,
    int  TotalHeavyBytes,
    int  InlineBudget,
    int  HeavyBudget,
    bool RequiresHeavyComponent,
    PackWarning Warning,
    IReadOnlyList<VariableViewModel> Variables,
    IReadOnlyList<string> KnownTypeNames,
    IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements,
    IReadOnlyList<VariableViewModel> HardcodedDtoFields = null!);

// ---- Window -----------------------------------------------------------------

/// <summary>
/// Docked panel showing and editing the blackboard variable list of the active AI asset.
/// The drawing logic delegates to a testable static view-model builder so that unit
/// tests never touch ImGui.
/// </summary>
public sealed class BlackboardAuthoringWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IRefactorService _refactorService;
    private readonly ComparisonToolbarAction? _comparisonToolbar;
    private readonly ComparisonSessionRegistry? _sessionRegistry;
    private readonly BlackboardAggregatorService? _aggregatorService;
    private IActionSchemaExporter? _actionSchemaExporter;

    // Inline rename state
    
    

    // Add-variable popup state
    
    
    
    
    

    // Remove-variable confirmation state
    
    

    // Remove-unused confirmation state
    

    // Lossy-save confirmation state (1f-07)
    private bool _openLossySaveConfirm;
    private bool _lossySavePopupOpen = true;

    /// <param name="store">Editor selection store for this perspective.</param>
    /// <param name="refactorService">Refactoring service.</param>
    /// <param name="sanitizerRegistry">Optional comparison sanitizer registry.</param>
    /// <param name="exportBuilder">Optional comparison export builder.</param>
    /// <param name="sessionRegistry">Optional comparison session registry.</param>
    /// <param name="aggregatorService">
    ///   Optional blackboard aggregator service. When supplied, its
    ///   <see cref="BlackboardAggregatorService.Aggregate"/> output is passed to
    ///   <see cref="BuildViewModel"/> so budget warnings from sub-tree DTO requirements
    ///   surface in the bin-packing display (AIE-052).
    /// </param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_blackboard_variables_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    public BlackboardAuthoringWindow(
        EditorSelectionStore store,
        IRefactorService refactorService,
        SanitizerRegistry? sanitizerRegistry = null,
        ComparisonExportBuilder? exportBuilder = null,
        ComparisonSessionRegistry? sessionRegistry = null,
        BlackboardAggregatorService? aggregatorService = null,
        string? idOverride = null,
        string? owningPerspective = null,
        IActionSchemaExporter? actionSchemaExporter = null)
        : base(idOverride ?? "ai_blackboard_variables", "Blackboard Variables",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
        _sessionRegistry = sessionRegistry;
        _aggregatorService = aggregatorService;
        _actionSchemaExporter = actionSchemaExporter;
        if (sanitizerRegistry != null && exportBuilder != null && sessionRegistry != null)
            _comparisonToolbar = new ComparisonToolbarAction(sanitizerRegistry, exportBuilder, sessionRegistry);
    }

    // ---- View-model building (unit-testable) --------------------------------

    /// <summary>
    /// Builds a <see cref="BlackboardWindowViewModel"/> from the currently active asset.
    /// Pure; no ImGui calls; safe to invoke from unit tests.
    /// </summary>
    /// <param name="activeAsset">The currently selected asset, or null when nothing is selected.</param>
    /// <param name="knownTypeNames">Optional override for the type-name completion list.</param>
    /// <param name="aggregationResult">Optional sub-tree aggregation result (AIE-052).</param>
    /// <param name="actionSchemaExporter">
    /// Optional schema exporter used to resolve hardcoded DTO fields from bound action FQNs.
    /// When null, <see cref="BlackboardWindowViewModel.HardcodedDtoFields"/> is empty.
    /// </param>
    /// <param name="boundActionFqns">
    /// FQNs of hardcoded actions bound to nodes in the active asset.
    /// Only used when <paramref name="actionSchemaExporter"/> is non-null.
    /// </param>
    public static BlackboardWindowViewModel BuildViewModel(
        IEditableAsset? activeAsset,
        IReadOnlyList<string>? knownTypeNames = null,
        AggregationResult? aggregationResult = null,
        IActionSchemaExporter? actionSchemaExporter = null,
        IReadOnlyList<string>? boundActionFqns = null)
    {
        var typeNames = knownTypeNames ?? BlackboardTypeHelper.DefaultKnownTypeNames;

        // Compute hardcoded DTO fields from bound action FQNs (S1-1).
        var hardcodedDtoFields = BuildHardcodedDtoFields(actionSchemaExporter, boundActionFqns);

        if (activeAsset is null)
        {
            return new BlackboardWindowViewModel(
                HasActiveAsset:            false,
                IsBlackboardEditorManaged: false,
                TotalInlineBytes:          0,
                TotalHeavyBytes:           0,
                InlineBudget:              BlackboardBinPacker.MaxInlineBytes,
                HeavyBudget:               BlackboardBinPacker.MaxHeavyBytes,
                RequiresHeavyComponent:    false,
                Warning:                   PackWarning.None,
                Variables:                 Array.Empty<VariableViewModel>(),
                KnownTypeNames:            typeNames,
                UnboundRequirements:       Array.Empty<UnboundRequirementViewModel>(),
                HardcodedDtoFields:        hardcodedDtoFields);
        }

        if (activeAsset is not IBlackboardManagedAsset bbAsset
            || !bbAsset.IsBlackboardEditorManaged)
        {
            return new BlackboardWindowViewModel(
                HasActiveAsset:            true,
                IsBlackboardEditorManaged: false,
                TotalInlineBytes:          0,
                TotalHeavyBytes:           0,
                InlineBudget:              BlackboardBinPacker.MaxInlineBytes,
                HeavyBudget:               BlackboardBinPacker.MaxHeavyBytes,
                RequiresHeavyComponent:    false,
                Warning:                   PackWarning.None,
                Variables:                 Array.Empty<VariableViewModel>(),
                KnownTypeNames:            typeNames,
                UnboundRequirements:       Array.Empty<UnboundRequirementViewModel>(),
                HardcodedDtoFields:        hardcodedDtoFields);
        }

        var rawVars = bbAsset.BlackboardVariables;

        // Build the set of (assetId, elementId) pairs that have been aliased.
        var aliasedKeys = new HashSet<(Guid, Guid)>();
        foreach (var v in rawVars)
        {
            foreach (var a in bbAsset.GetAliasesFor(v.Name))
                aliasedKeys.Add((a.RequiringAssetId, a.RequiringElementId));
        }

        // Project aggregation requirements into unbound view-model rows,
        // filtering out requirements that have already been aliased.
        var unboundRows = aggregationResult != null && aggregationResult.Requirements.Count > 0
            ? aggregationResult.Requirements
                .Where(r => !aliasedKeys.Contains((r.RequiringAssetId, r.RequiringElementId)))
                .Select(r => new UnboundRequirementViewModel(
                    r.DtoType.Name,
                    r.RequiredByPath,
                    r.RequiringAssetId,
                    r.RequiringElementId,
                    r.DtoType,
                    ExtractAssetName(r.RequiredByPath)))
                .ToList()
            : (IReadOnlyList<UnboundRequirementViewModel>)Array.Empty<UnboundRequirementViewModel>();

        if (rawVars.Count == 0 && unboundRows.Count == 0)
        {
            return new BlackboardWindowViewModel(
                HasActiveAsset:            true,
                IsBlackboardEditorManaged: true,
                TotalInlineBytes:          0,
                TotalHeavyBytes:           0,
                InlineBudget:              BlackboardBinPacker.MaxInlineBytes,
                HeavyBudget:               BlackboardBinPacker.MaxHeavyBytes,
                RequiresHeavyComponent:    false,
                Warning:                   PackWarning.None,
                Variables:                 Array.Empty<VariableViewModel>(),
                KnownTypeNames:            typeNames,
                UnboundRequirements:       unboundRows,
                HardcodedDtoFields:        hardcodedDtoFields);
        }

        var descriptors = rawVars
            .Select(v => new BlackboardVariableDescriptor(v.Name, v.FieldType))
            .ToList();

        // Derive aggregated descriptors from the aggregation requirements for packing.
        var aggregatedDescriptors = aggregationResult?.Requirements
            .Select(r => new BlackboardVariableDescriptor(r.DtoType.Name, r.DtoType))
            .ToList();

        var pack = BlackboardBinPacker.Pack(descriptors, aggregatedDescriptors);

        // Build per-variable view models using byte size from the packer result.
        // Note: aggregated DTO-requirement descriptors are named by DtoType.Name, so several
        // can share a name when multiple nodes bind the same DTO type (e.g. a condition and an
        // action both bound to one DemoCounterParams variable). Dedup is safe — same name means
        // the same DTO type and therefore the same byte size — and only master-variable names
        // (unique) are looked up below.
        var sizeMap = new Dictionary<string, int>();
        foreach (var pv in pack.Variables)
            sizeMap[pv.Name] = pv.ByteSize;
        var rows = new List<VariableViewModel>(rawVars.Count);
        foreach (var v in rawVars)
        {
            sizeMap.TryGetValue(v.Name, out int byteSize);
            bool isUnused = bbAsset.CountNodesReferencingVariable(v.Name) == 0;
            var aliases = bbAsset.GetAliasesFor(v.Name)
                .Select(a => (a.RequiringAssetName, a.RequiringAssetId, a.RequiringElementId))
                .ToList<(string AssetName, Guid AssetId, Guid ElementId)>();
            rows.Add(new VariableViewModel(
                v.Name,
                BlackboardTypeHelper.GetDisplayName(v.FieldType),
                byteSize,
                v.FieldType,
                v.Comment,
                aliases.Count > 0
                    ? aliases
                    : (IReadOnlyList<(string, Guid, Guid)>)Array.Empty<(string, Guid, Guid)>(),
                isUnused,
                IsAutoManaged: v.IsAutoManaged));
        }

        return new BlackboardWindowViewModel(
            HasActiveAsset:            true,
            IsBlackboardEditorManaged: true,
            TotalInlineBytes:          pack.TotalInlineBytes,
            TotalHeavyBytes:           pack.TotalHeavyBytes,
            InlineBudget:              BlackboardBinPacker.MaxInlineBytes,
            HeavyBudget:               BlackboardBinPacker.MaxHeavyBytes,
            RequiresHeavyComponent:    pack.RequiresHeavyComponent,
            Warning:                   pack.Warning,
            Variables:                 rows,
            KnownTypeNames:            typeNames,
            UnboundRequirements:       unboundRows,
            HardcodedDtoFields:        hardcodedDtoFields);
    }

    /// <summary>
    /// Builds the list of read-only <see cref="VariableViewModel"/> rows derived from
    /// hardcoded action DTO fields (S1-1).
    /// </summary>
    private static IReadOnlyList<VariableViewModel> BuildHardcodedDtoFields(
        IActionSchemaExporter? exporter,
        IReadOnlyList<string>? boundFqns)
    {
        if (exporter == null || boundFqns == null || boundFqns.Count == 0)
            return Array.Empty<VariableViewModel>();

        var result = new List<VariableViewModel>();
        // Use a set to deduplicate fields by (DtoType, FieldName) across multiple FQNs.
        var seen = new HashSet<(Type, string)>();

        foreach (var fqn in boundFqns)
        {
            var entry = exporter.Lookup(fqn);
            if (entry?.DtoFields == null)
                continue;

            foreach (var field in entry.DtoFields)
            {
                if (!seen.Add((entry.DtoType, field.Name)))
                    continue;

                result.Add(new VariableViewModel(
                    Name:      field.Name,
                    TypeName:  BlackboardTypeHelper.GetDisplayName(field.FieldType),
                    ByteSize:  0,
                    FieldType: field.FieldType,
                    Comment:   null,
                    AliasedBy: Array.Empty<(string, Guid, Guid)>(),
                    IsUnused:  false,
                    IsReadOnly: true));
            }
        }

        return result;
    }

    // Extracts the asset name from a RequiredByPath string, e.g.
    // "Shoot_BT > Action#7 (FireAtTarget)" -> "Shoot_BT".
    private static string ExtractAssetName(string requiredByPath)
    {
        int sep = requiredByPath.IndexOf(" > ", StringComparison.Ordinal);
        return sep > 0 ? requiredByPath[..sep] : requiredByPath;
    }

    // ---- ImGui rendering ---------------------------------------------------

    private VariablesPanelControl? _variablesControl;
    private IEditableAsset? _lastAssetBase;

    protected override void DrawClientArea()
    {
        // Prune stale alias bindings before building the view-model (DEBT-06).
        if (_store.ActiveAsset is IBlackboardManagedAsset bbAssetForPrune)
            bbAssetForPrune.PruneStaleAliasBindings(bbAssetForPrune.GetKnownSubAssetIds());

        // Comparison toolbar (shown when comparison services are available).
        if (_comparisonToolbar != null && _store.ActiveAsset != null)
        {
            _comparisonToolbar.Render(
                _store.ActiveAsset.AssetId,
                _store.ActiveAsset.SourceFilePath,
                _store.ActiveAsset.Kind);
            ImGuiNET.ImGui.Separator();
        }

        // State-aware banner: AssemblyFailed replaces the entire client area (1f-07).
        if (_store.ActiveAsset is IBlackboardManagedAsset assetForState
            && assetForState.IsBlackboardEditorManaged
            && assetForState.LoadState == BlackboardLoadState.AssemblyFailed)
        {
            ImGuiNET.ImGui.TextColored(
                new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f),
                $"Cannot load blackboard. {assetForState.LoadDiagnosticMessage}");
            return;
        }

        // AIE-052: aggregate sub-tree DTO requirements so bin-packing can surface budget warnings.
        var aggregationResult = (_aggregatorService != null && _store.ActiveAsset != null)
            ? _aggregatorService.Aggregate(_store.ActiveAsset)
            : (AggregationResult?)null;

        var vm = BuildViewModel(_store.ActiveAsset, aggregationResult: aggregationResult, actionSchemaExporter: _actionSchemaExporter);

        if (!vm.HasActiveAsset)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        if (!vm.IsBlackboardEditorManaged)
        {
            ImGuiNET.ImGui.TextDisabled("This asset does not use an editor-managed blackboard.");
            if (_store.ActiveAsset is IBlackboardManagedAsset bbToEnable)
            {
                ImGuiNET.ImGui.Spacing();
                ImGuiNET.ImGui.TextWrapped("Enable an editor-managed blackboard to declare typed variables for this asset.");
                if (ImGuiNET.ImGui.Button("Use editor-managed blackboard"))
                    bbToEnable.SetBlackboardEditorManaged(true);
            }
            return;
        }

        var bbAsset = (IBlackboardManagedAsset)_store.ActiveAsset!;
        var asset   = _store.ActiveAsset!;

        // State-aware banners for StructParseFailed and SpanCaptureFailed (1f-07).
        bool isReadOnly = bbAsset.LoadState == BlackboardLoadState.StructParseFailed
                       || bbAsset.LoadState == BlackboardLoadState.SpanCaptureFailed;
        if (bbAsset.LoadState == BlackboardLoadState.StructParseFailed)
        {
            ImGuiNET.ImGui.TextColored(
                new System.Numerics.Vector4(1f, 0.75f, 0f, 1f),
                $"Warning: struct parse failed -- read-only. {bbAsset.LoadDiagnosticMessage}");
        }
        else if (bbAsset.LoadState == BlackboardLoadState.SpanCaptureFailed)
        {
            ImGuiNET.ImGui.TextColored(
                new System.Numerics.Vector4(1f, 0.75f, 0f, 1f),
                $"Warning: span capture failed -- read-only. {bbAsset.LoadDiagnosticMessage}");
            if (ImGuiNET.ImGui.Button("Save anyway (lossy)"))
                _openLossySaveConfirm = true;
            if (_openLossySaveConfirm)
            {
                ImGuiNET.ImGui.OpenPopup("confirm_lossy_save");
                _openLossySaveConfirm = false;
            }
            if (ImGuiNET.ImGui.BeginPopupModal("confirm_lossy_save", ref _lossySavePopupOpen,
                ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiNET.ImGui.Text("Some field spans could not be captured. Saving will lose hand-introduced fields.");
                ImGuiNET.ImGui.Text("Proceed with lossy save?");
                if (ImGuiNET.ImGui.Button("Yes, save"))
                {
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                    ImGuiNET.ImGui.CloseCurrentPopup();
                ImGuiNET.ImGui.EndPopup();
            }
        }

        var schema = new BTreeHsmSchemaSource(bbAsset, vm, isReadOnly);
        var section = new VariablesPanelSection(
            "Variables",
            "##bb_vars",
            schema,
            vm.TotalInlineBytes,
            vm.InlineBudget,
            vm.TotalHeavyBytes,
            vm.HeavyBudget,
            vm.RequiresHeavyComponent,
            vm.Warning,
            AliasingEnabled: true
        );

        if (_variablesControl == null || _lastAssetBase != asset)
        {
            _variablesControl = new VariablesPanelControl(_refactorService, asset, vm.KnownTypeNames);
            _lastAssetBase = asset;
        }

        Func<string, FieldDecoration?>? rowDec = null;
        if (_sessionRegistry != null && _store.ActiveAsset != null)
        {
            var compSession = _sessionRegistry.GetSession(_store.ActiveAsset.AssetId);
            if (compSession != null)
                rowDec = fieldName => BlackboardComparisonDecorator.GetDecoration(fieldName, compSession);
        }

        _variablesControl.DrawSingle(section, rowDec);

        // Sub-tree allocations section (1e-05): auto-managed slots for Approach B nodes.
        var autoAllocs = (asset as IBTreeSyncableAsset)?.GetAutoAllocatedVariables();
        if (autoAllocs is { Count: > 0 })
        {
            ImGuiNET.ImGui.Separator();
            if (ImGuiNET.ImGui.CollapsingHeader("SUB-TREE ALLOCATIONS (auto-managed)",
                ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.Alpha, 0.5f);
                for (int i = 0; i < autoAllocs.Count; i++)
                {
                    var alloc = autoAllocs[i];
                    ImGuiNET.ImGui.TextUnformatted($"{alloc.Name}    (size unknown until build)");
                }
                ImGuiNET.ImGui.PopStyleVar();
            }
        }
    }
}
