$file = "D:\WORK\IOS-IG-SimHost-FDP\Hrot\Editor\Hrot.Editor.AiShared\Windows\BlackboardAuthoringWindow.cs"
$content = Get-Content -Raw $file
$start = $content.IndexOf("    protected override void DrawClientArea()")
$newDrawClient = @"
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
"@
$newContent = $content.Substring(0, $start) + $newDrawClient
Set-Content $file -Value $newContent
