using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Blackboard;
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
    bool   IsUnused);

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
    IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements);

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

    // Inline rename state
    
    

    // Add-variable popup state
    
    
    
    
    

    // Remove-variable confirmation state
    
    

    // Remove-unused confirmation state
    

    // Lossy-save confirmation state (1f-07)
    private bool _openLossySaveConfirm;
    private bool _lossySavePopupOpen = true;

    public BlackboardAuthoringWindow(
        EditorSelectionStore store,
        IRefactorService refactorService)
        : base("ai_blackboard_variables", "Blackboard Variables", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
    }

    // ---- View-model building (unit-testable) --------------------------------

    /// <summary>
    /// Builds a <see cref="BlackboardWindowViewModel"/> from the currently active asset.
    /// Pure; no ImGui calls; safe to invoke from unit tests.
    /// </summary>
    public static BlackboardWindowViewModel BuildViewModel(
        IEditableAsset? activeAsset,
        IReadOnlyList<string>? knownTypeNames = null,
        AggregationResult? aggregationResult = null)
    {
        var typeNames = knownTypeNames ?? BlackboardTypeHelper.DefaultKnownTypeNames;

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
                UnboundRequirements:       Array.Empty<UnboundRequirementViewModel>());
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
                UnboundRequirements:       Array.Empty<UnboundRequirementViewModel>());
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
                UnboundRequirements:       unboundRows);
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
        var sizeMap = pack.Variables.ToDictionary(pv => pv.Name, pv => pv.ByteSize);
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
                isUnused));
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
            UnboundRequirements:       unboundRows);
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

        var vm = BuildViewModel(_store.ActiveAsset);

        if (!vm.HasActiveAsset)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        if (!vm.IsBlackboardEditorManaged)
        {
            ImGuiNET.ImGui.TextDisabled("This asset does not use an editor-managed blackboard.");
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

        _variablesControl.DrawSingle(section);

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
