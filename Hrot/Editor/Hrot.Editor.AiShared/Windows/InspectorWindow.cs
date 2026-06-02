using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Inspector window -- shows properties for the currently-selected sub-element.
/// StructEdit-driven dispatch by asset type; subsystems supply facet structs.
/// This is a shell; per-subsystem inspector panels are added in later phases.
/// </summary>
public sealed class InspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;
    private readonly Func<Guid, IBlackboardManagedAsset?>? _subAssetResolver;

    // Optional schema exporter for sub-element collision diagnostics (AIE-053).
    private readonly IActionSchemaExporter? _schemaExporter;

    // Optional facet dispatcher injected from composition root (keeps AiShared dep-clean).
    private IFacetDispatcher? _facetDispatcher;

    // Cached facet state (one boxed struct per frame that has an active sub-selection).
    private object? _currentFacet;
    private IAssetSubSelection? _currentFacetSelection;

    private string? _pendingRenameKey;
    private readonly byte[] _renameBuf = new byte[512];
    private bool _openRenameModal;

    /// <param name="store">Editor selection store for this perspective.</param>
    /// <param name="refactorService">Refactoring service.</param>
    /// <param name="findResults">Find-results window.</param>
    /// <param name="subAssetResolver">Optional sub-asset resolver for blackboard data.</param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override. When supplied, the window uses this id
    ///   rather than the default <c>"ai_inspector"</c>, enabling per-perspective
    ///   instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    /// <param name="facetDispatcher">
    ///   Optional per-perspective facet dispatcher (injected from composition root).
    ///   When supplied, sub-selections are routed through it for StructEdit rendering.
    /// </param>
    public InspectorWindow(
        EditorSelectionStore store,
        IRefactorService refactorService,
        FindResultsWindow findResults,
        Func<Guid, IBlackboardManagedAsset?>? subAssetResolver = null,
        string? idOverride = null,
        string? owningPerspective = null,
        IFacetDispatcher? facetDispatcher = null,
        IActionSchemaExporter? schemaExporter = null)
        : base(idOverride ?? "ai_inspector", "Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
        _findResults = findResults;
        _subAssetResolver = subAssetResolver;
        _facetDispatcher = facetDispatcher;
        _schemaExporter = schemaExporter;
    }

    /// <summary>
    /// Wires (or replaces) the facet dispatcher at runtime.
    /// Called from the composition root after construction when the dispatcher
    /// is built with the asset-specific mapper.
    /// </summary>
    public void SetFacetDispatcher(IFacetDispatcher? dispatcher)
    {
        _facetDispatcher = dispatcher;
        // Invalidate cached facet when dispatcher changes.
        _currentFacet          = null;
        _currentFacetSelection = null;
    }

    /// <summary>
    /// Returns the boxed facet for the current sub-selection (if any).
    /// Used by tests to verify dispatch without triggering ImGui.
    /// </summary>
    internal object? GetCurrentFacet()
    {
        var sub = _store.ActiveSubSelection;
        if (_facetDispatcher is null || sub is null) return null;
        if (!ReferenceEquals(sub, _currentFacetSelection))
        {
            _currentFacet          = _facetDispatcher.GetFacet(sub);
            _currentFacetSelection = sub;
        }
        return _currentFacet;
    }

    /// <summary>
    /// Commits the current facet (applies edited values back to the asset).
    /// Safe to call from tests — does not require ImGui.
    /// </summary>
    internal void CommitCurrentFacet(object editedFacet)
    {
        var sub = _store.ActiveSubSelection;
        if (_facetDispatcher is null || sub is null) return;
        _facetDispatcher.ApplyFacet(sub, editedFacet);
        // Invalidate cache so next GetCurrentFacet re-reads from asset.
        _currentFacet          = null;
        _currentFacetSelection = null;
    }

    protected override void DrawClientArea()
    {
        // Render the diagnostic strip for SubElementCollisions (AIE-053).
        DrawCollisionDiagnosticStrip();

        if (_store.ActiveAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        var asset = _store.ActiveAsset;

        ImGuiNET.ImGui.Selectable(asset.Name);
        if (ImGuiNET.ImGui.BeginPopupContextItem("##insp_ctx"))
        {
            if (ImGuiNET.ImGui.MenuItem("Find References"))
            {
                var refs = _refactorService.FindReferences(asset.Name);
                _findResults.ShowReferences(asset.Name, refs);
            }
            if (ImGuiNET.ImGui.MenuItem("Rename..."))
            {
                _pendingRenameKey = asset.Name;
                _openRenameModal = true;
                Array.Clear(_renameBuf, 0, _renameBuf.Length);
            }
            if (ImGuiNET.ImGui.MenuItem("Go to Definition"))
            {
                // placeholder -- navigation wired in a later phase
            }
            ImGuiNET.ImGui.EndPopup();
        }

        if (_openRenameModal)
        {
            ImGuiNET.ImGui.OpenPopup("Rename##insp");
            _openRenameModal = false;
        }

        if (_pendingRenameKey != null)
        {
            var renameOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal("Rename##insp", ref renameOpen,
                ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiNET.ImGui.Text($"Rename: {_pendingRenameKey}");
                ImGuiNET.ImGui.Text("New name:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.InputText("##rname_insp", _renameBuf, (uint)_renameBuf.Length);
                if (ImGuiNET.ImGui.Button("OK"))
                {
                    var newKey = System.Text.Encoding.UTF8.GetString(_renameBuf).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(newKey))
                    {
                        var preview = _refactorService.PreviewRename(
                            _pendingRenameKey, newKey, new RefactorOptions());
                        _findResults.ShowRenamePreview(preview);
                    }
                    _pendingRenameKey = null;
                    Array.Clear(_renameBuf, 0, _renameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                {
                    _pendingRenameKey = null;
                    Array.Clear(_renameBuf, 0, _renameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.EndPopup();
            }
            if (!renameOpen)
            {
                _pendingRenameKey = null;
                Array.Clear(_renameBuf, 0, _renameBuf.Length);
            }
        }

        // ---- Facet dispatch (AIE-023) -----------------------------------------------
        // Render StructEdit facet for the active sub-selection when a dispatcher is wired.
        // All ImGui calls are inside this block so headless tests can call GetCurrentFacet()
        // and CommitCurrentFacet() without an ImGui context.
        if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero
            && _facetDispatcher is not null
            && _store.ActiveSubSelection is { } activeSub)
        {
            var facet = GetCurrentFacet();
            if (facet is not null)
            {
                ImGuiNET.ImGui.Separator();
                ImGuiNET.ImGui.Text($"[{facet.GetType().Name}]");
                // Full StructEdit rendering would go here (wired in a later pass).
                // For now show a Commit button that applies the facet back.
                if (ImGuiNET.ImGui.Button("Apply##facet"))
                    CommitCurrentFacet(facet);
            }
        }

        // ---- Subtree parameter synchronization panel (1e-01, 1e-02) ----
        if (_store.ActiveSubSelection is BTreeNodeSelection nodeSel
            && _store.ActiveAsset is IBTreeSyncableAsset syncAsset)
        {
            var nodeInfo = syncAsset.GetSubtreeNodeInfo(nodeSel.VisualId);
            if (nodeInfo is not null)
            {
                ImGuiNET.ImGui.Separator();
                ImGuiNET.ImGui.Text("PARAMETER SYNCHRONIZATION");
                if (!nodeInfo.IsResolved)
                {
                    ImGuiNET.ImGui.TextDisabled("Subtree not resolved -- sync unavailable.");
                }
                else if (_subAssetResolver is null)
                {
                    ImGuiNET.ImGui.TextDisabled("Sub-asset resolver not configured.");
                }
                else
                {
                    var subAsset = _subAssetResolver(nodeInfo.SubtreeAssetId);
                    if (subAsset is null)
                    {
                        ImGuiNET.ImGui.TextDisabled("Sub-asset not found.");
                    }
                    else
                    {
                        DrawSyncBindingsTable(nodeSel.VisualId, syncAsset, subAsset);
                    }
                }
            }
        }

        // ---- Utility consideration inspector panel ----------------------------
        if (_store.ActiveSubSelection is UtilityConsiderationSelection utilSel)
        {
            ImGuiNET.ImGui.Separator();
            ImGuiNET.ImGui.Text("UTILITY CONSIDERATION");
            ImGuiNET.ImGui.TextDisabled(
                $"Option {utilSel.OptionIndex}, Consideration {utilSel.ConsiderationIndex}");
            // Curve inspector panel wired in a later phase (P5-02).
        }
    }

    /// <summary>
    /// Returns the current list of sub-element collisions without requiring an ImGui context.
    /// Returns null if no schema exporter was injected.
    /// Used by tests to verify collision detection headlessly.
    /// </summary>
    internal IReadOnlyList<ActionCollision>? GetCollisions() =>
        _schemaExporter is null ? null : SubElementCollisionDetector.GetCollisions(_schemaExporter);

    private void DrawCollisionDiagnosticStrip()
    {
        if (_schemaExporter is null) return;

        var collisions = SubElementCollisionDetector.GetCollisions(_schemaExporter);
        if (collisions.Count == 0) return;

        ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.ChildBg, new System.Numerics.Vector4(0.2f, 0.05f, 0.05f, 1f));
        ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Border,  new System.Numerics.Vector4(1f,   0.2f,  0.2f,  1f));

        if (ImGuiNET.ImGui.BeginChild("SubElementCollisions",
                new System.Numerics.Vector2(0, 30 + (collisions.Count * 20)),
                ImGuiNET.ImGuiChildFlags.Borders))
        {
            ImGuiNET.ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f),
                "⚠ SUB-ELEMENT COLLISIONS DETECTED");

            foreach (var collision in collisions)
            {
                ImGuiNET.ImGui.TextWrapped(
                    $"Short name '{collision.ShortName}' has multiple FQN claimants: " +
                    string.Join(", ", collision.ClaimingFqns));
            }
        }
        ImGuiNET.ImGui.EndChild();

        ImGuiNET.ImGui.PopStyleColor(2);
        ImGuiNET.ImGui.Spacing();
    }

    private void DrawSyncBindingsTable(
        Guid nodeVisualId,
        IBTreeSyncableAsset syncAsset,
        IBlackboardManagedAsset subAsset)
    {
        var subVars = subAsset.BlackboardVariables;
        if (subVars.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("Sub-tree has no blackboard variables.");
            return;
        }

        // Record sub-tree identity metadata for the orchestrator emitter.
        string shortDtoTypeName = ShortTypeName(subAsset.BlackboardTypeName);
        string? dtoTypeNs = NsOf(subAsset.BlackboardTypeName);
        syncAsset.RecordSubtreeNodeMeta(
            nodeVisualId,
            SanitizeIdentifier(subAsset.Name),
            shortDtoTypeName,
            dtoTypeNs);

        var existingBindings = syncAsset.GetSyncBindings(nodeVisualId);
        var bindingMap = new Dictionary<string, SubtreeSyncBinding>(existingBindings.Count);
        foreach (var b in existingBindings)
            bindingMap[b.FieldName] = b;

        if (ImGuiNET.ImGui.BeginTable("##sync_params", 4,
            ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
        {
            ImGuiNET.ImGui.TableSetupColumn("Field",      ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
            ImGuiNET.ImGui.TableSetupColumn("Bound to",   ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
            ImGuiNET.ImGui.TableSetupColumn("In",         ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 24f);
            ImGuiNET.ImGui.TableSetupColumn("Out",        ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 24f);
            ImGuiNET.ImGui.TableHeadersRow();

            for (int i = 0; i < subVars.Count; i++)
            {
                var field = subVars[i];
                bindingMap.TryGetValue(field.Name, out var binding);
                bool syncIn  = binding?.SyncIn  ?? false;
                bool syncOut = binding?.SyncOut ?? false;
                string? masterVar = binding?.MasterVariableName;

                ImGuiNET.ImGui.TableNextRow();
                ImGuiNET.ImGui.PushID(i);

                // Field name column
                ImGuiNET.ImGui.TableNextColumn();
                string fieldTypeName = BlackboardTypeHelper.GetDisplayName(field.FieldType);
                ImGuiNET.ImGui.TextUnformatted($"{field.Name} : {fieldTypeName}");

                // Bound-to dropdown column (1e-02)
                ImGuiNET.ImGui.TableNextColumn();
                var candidates = syncAsset.GetVariablesOfType(fieldTypeName);
                string currentLabel = masterVar ?? "(none)";
                if (ImGuiNET.ImGui.BeginCombo($"##bound_{i}", currentLabel))
                {
                    // "(none)" option
                    bool noneSelected = masterVar is null;
                    if (ImGuiNET.ImGui.Selectable("(none)", noneSelected) && !noneSelected)
                    {
                        var updated = new SubtreeSyncBinding(field.Name, null, syncIn, syncOut);
                        syncAsset.SetSyncBinding(nodeVisualId, updated);
                    }
                    foreach (var cand in candidates)
                    {
                        bool selected = cand.Name == masterVar;
                        if (ImGuiNET.ImGui.Selectable(cand.Name, selected) && !selected)
                        {
                            var updated = new SubtreeSyncBinding(field.Name, cand.Name, syncIn, syncOut);
                            syncAsset.SetSyncBinding(nodeVisualId, updated);
                        }
                    }
                    ImGuiNET.ImGui.EndCombo();
                }

                // SyncIn checkbox
                ImGuiNET.ImGui.TableNextColumn();
                if (ImGuiNET.ImGui.Checkbox($"##in_{i}", ref syncIn))
                {
                    var updated = new SubtreeSyncBinding(field.Name, masterVar, syncIn, syncOut);
                    syncAsset.SetSyncBinding(nodeVisualId, updated);
                }

                // SyncOut checkbox
                ImGuiNET.ImGui.TableNextColumn();
                if (ImGuiNET.ImGui.Checkbox($"##out_{i}", ref syncOut))
                {
                    var updated = new SubtreeSyncBinding(field.Name, masterVar, syncIn, syncOut);
                    syncAsset.SetSyncBinding(nodeVisualId, updated);
                }

                ImGuiNET.ImGui.PopID();
            }

            ImGuiNET.ImGui.EndTable();
        }
    }

    // ---- Identifier/type helpers ----

    private static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn[(last + 1)..] : fqn;
    }

    private static string? NsOf(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last > 0 ? fqn[..last] : null;
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return "Asset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
