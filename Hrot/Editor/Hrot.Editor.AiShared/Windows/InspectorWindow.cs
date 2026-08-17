using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Presentation.Editing;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using StructEdit.Core;

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

    // StructEdit edit-service + per-type session cache (SE1).
    // The edit service is injected from outside (keeps AiShared dep-clean from picker assemblies).
    private IComponentEditService? _facetEditService;
    private IReadOnlyDictionary<Type, IImGuiFieldDrawer>? _facetCustomDrawers;

    // Active StructEdit session for the current facet. Keyed by BOTH the facet type AND the
    // sub-selection it was opened for, so selecting a different node of the SAME facet type
    // (e.g. Wait(1s) -> Wait(2s)) rebuilds the session against that node's values rather than
    // reusing the stale first node's session.
    private IEditSession? _facetSession;
    private Type? _facetSessionType;
    private IAssetSubSelection? _facetSessionSub;

    // B-3: StructEdit session for the bound variable's DefaultValueJson.
    // Delegate that extracts ExpressionTargetField from a boxed facet (injected from composition root).
    private Func<object?, string?>? _expressionTargetFieldAccessor;
    // Active session for the bound variable's default value.
    private IEditSession? _defaultValueSession;
    private string? _defaultValueSessionVarName;   // variable name the session was opened for

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
    /// <param name="facetEditService">
    ///   Optional StructEdit <see cref="IComponentEditService"/> used to open edit sessions for
    ///   boxed facet structs. When null, the stub "Apply" button is shown instead.
    ///   Injected from the composition root with all picker drawers pre-registered (SE1).
    /// </param>
    /// <param name="facetCustomDrawers">
    ///   Optional map of CLR type → <see cref="IImGuiFieldDrawer"/> forwarded to
    ///   <see cref="ComponentEditDrawer"/>. Carries the HSM and BTree picker drawers
    ///   (registered by the composition root; keeps AiShared dep-clean).
    /// </param>
    /// <param name="expressionTargetFieldAccessor">
    ///   Optional delegate (injected from composition root) that reads the
    ///   <c>ExpressionTargetField</c> value from a boxed facet struct (B-3).
    ///   When supplied and non-null, enables the "Static Parameters" default-value
    ///   StructEdit panel below the action facet.
    /// </param>
    public InspectorWindow(
        EditorSelectionStore store,
        IRefactorService refactorService,
        FindResultsWindow findResults,
        Func<Guid, IBlackboardManagedAsset?>? subAssetResolver = null,
        string? idOverride = null,
        string? owningPerspective = null,
        IFacetDispatcher? facetDispatcher = null,
        IActionSchemaExporter? schemaExporter = null,
        IComponentEditService? facetEditService = null,
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? facetCustomDrawers = null,
        Func<object?, string?>? expressionTargetFieldAccessor = null)
        : base(idOverride ?? "ai_inspector", "Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
        _findResults = findResults;
        _subAssetResolver = subAssetResolver;
        _facetDispatcher = facetDispatcher;
        _schemaExporter = schemaExporter;
        _facetEditService = facetEditService;
        _facetCustomDrawers = facetCustomDrawers;
        _expressionTargetFieldAccessor = expressionTargetFieldAccessor;
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
        DisposeAndClearFacetSession();
    }

    /// <summary>
    /// Wires (or replaces) the StructEdit edit service and custom field drawers at runtime (SE1).
    /// Safe to call after construction — invalidates the cached session so the next render
    /// opens a fresh one.
    /// </summary>
    public void SetFacetEditService(
        IComponentEditService? editService,
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null)
    {
        _facetEditService   = editService;
        _facetCustomDrawers = customDrawers;
        DisposeAndClearFacetSession();
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
        // Also drop the StructEdit session so it is rebuilt from the freshly committed values.
        DisposeAndClearFacetSession();
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
                    var newKey = Fdp.Presentation.Utils.ImGuiBufferText.Decode(_renameBuf);
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

        // ---- Facet dispatch + StructEdit render (AIE-023, SE1) ----------------------
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

                if (_facetEditService is not null)
                {
                    // SE1: real StructEdit render.
                    // Open (or reuse) the session for this facet type.
                    var facetType = facet.GetType();
                    // Rebuild when the facet TYPE changes OR the selected node changes (records →
                    // value equality), so per-node values are shown/edited correctly.
                    if (_facetSession is null
                        || _facetSessionType != facetType
                        || !Equals(_facetSessionSub, activeSub))
                    {
                        DisposeAndClearFacetSession();
                        _facetSession     = _facetEditService.Open(facet, facetType);
                        _facetSessionType = facetType;
                        _facetSessionSub  = activeSub;
                    }

                    // If the session requests a structural rebuild, honor it.
                    if (_facetSession.RebuildState == EditRebuildState.RebuildRequired)
                        _facetSession.RebuildDocument();

                    var drawers = _facetCustomDrawers
                        ?? new Dictionary<Type, IImGuiFieldDrawer>();

                    var drawer = new ComponentEditDrawer(_facetSession, pickerCtx: null, drawers);

                    // Two-column "Property | Value" table (matches ComponentEditWindow layout).
                    if (ImGuiNET.ImGui.BeginTable("##facet_edit", 2,
                        ImGuiNET.ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGuiNET.ImGui.TableSetupColumn("Property", ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.4f);
                        ImGuiNET.ImGui.TableSetupColumn("Value",    ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.6f);

                        drawer.DrawEditNode(_facetSession.Document.Root);

                        ImGuiNET.ImGui.EndTable();
                    }

                    // Commit on every dirty frame so edits flow back to the asset continuously.
                    if (_facetSession.IsDirty)
                    {
                        var committed = _facetSession.Commit();
                        CommitCurrentFacet(committed);
                        // CommitCurrentFacet disposes and clears _facetSession; session is null now.
                    }
                }
                else
                {
                    // Fallback stub when no edit service is wired.
                    ImGuiNET.ImGui.Text($"[{facet.GetType().Name}]");
                    if (ImGuiNET.ImGui.Button("Apply##facet"))
                        CommitCurrentFacet(facet);
                }
            }
        }

        // ---- B-3: Default-value StructEdit panel ("Static Parameters") ----
        // Shown when: an expressionTargetFieldAccessor is wired, the current facet carries a
        // non-null ExpressionTargetField, the active asset implements IBlackboardManagedAsset,
        // and an edit service is available.
        if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero
            && _expressionTargetFieldAccessor is not null
            && _facetEditService is not null
            && _currentFacet is not null
            && _store.ActiveAsset is IBlackboardManagedAsset bbAsset)
        {
            string? boundVarName = _expressionTargetFieldAccessor(_currentFacet);
            if (!string.IsNullOrEmpty(boundVarName))
            {
                // Find the variable entry in the asset's blackboard.
                BlackboardVariableEntry? varEntry = null;
                foreach (var v in bbAsset.BlackboardVariables)
                {
                    if (v.Name == boundVarName) { varEntry = v; break; }
                }

                if (varEntry is not null)
                {
                    // Rebuild the session when the bound variable changes.
                    if (_defaultValueSession is null || _defaultValueSessionVarName != boundVarName)
                    {
                        DisposeAndClearDefaultValueSession();
                        // ⭐⭐ C-dialog (Batch 68): this used to inline its OWN copy of
                        // DefaultValueAuthoring.Hydrate -- the same deserialize-or-Activator try/catch --
                        // and then call Open itself, so a variable default-value session had TWO
                        // implementations. ⛔ §9's rail is that exactly ONE call site opens one; it
                        // failed here. Routed, not rebuilt.
                        // ⭐⭐ C-dialog (Batch 68): this used to inline its OWN copy of
                        // DefaultValueAuthoring.Hydrate -- the same deserialize-or-Activator try/catch --
                        // and then call Open itself, so a variable default-value session had TWO
                        // implementations. ⛔ §9's rail is that exactly ONE call site opens one; it
                        // failed here. Routed, not rebuilt.
                        _defaultValueSession        = DefaultValueAuthoring.OpenSession(_facetEditService, varEntry);
                        _defaultValueSessionVarName = boundVarName;
                    }

                    if (_defaultValueSession.RebuildState == EditRebuildState.RebuildRequired)
                        _defaultValueSession.RebuildDocument();

                    ImGuiNET.ImGui.Separator();
                    ImGuiNET.ImGui.Text("STATIC PARAMETERS");
                    ImGuiNET.ImGui.TextDisabled($"Default values for: {boundVarName}");

                    var drawers2 = _facetCustomDrawers ?? new Dictionary<Type, IImGuiFieldDrawer>();
                    var drawer2  = new ComponentEditDrawer(_defaultValueSession, pickerCtx: null, drawers2);

                    if (ImGuiNET.ImGui.BeginTable("##defval_edit", 2,
                        ImGuiNET.ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGuiNET.ImGui.TableSetupColumn("Field", ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.4f);
                        ImGuiNET.ImGui.TableSetupColumn("Value", ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 0.6f);

                        drawer2.DrawEditNode(_defaultValueSession.Document.Root);

                        ImGuiNET.ImGui.EndTable();
                    }

                    if (_defaultValueSession.IsDirty)
                    {
                        var committed = _defaultValueSession.Commit();
                        string json;
                        try   { json = JsonSerializer.Serialize(committed, varEntry.FieldType, DefaultValueAuthoring.JsonOptions); }
                        catch { json = "{}"; }
                        bbAsset.UpdateVariableDefaultValueJson(boundVarName, json);
                        // Drop and rebuild next frame so the session reflects the persisted JSON.
                        DisposeAndClearDefaultValueSession();
                    }
                }
                else
                {
                    // Variable referenced by facet not found in asset blackboard.
                    // Clear any stale session.
                    if (_defaultValueSession is not null)
                        DisposeAndClearDefaultValueSession();
                }
            }
            else
            {
                // No bound variable — clear any leftover session.
                if (_defaultValueSession is not null)
                    DisposeAndClearDefaultValueSession();
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

    /// <summary>
    /// Exposes the currently cached StructEdit session for headless tests (SE1).
    /// Non-null only when a facet is active and an edit service is wired.
    /// </summary>
    internal IEditSession? GetFacetSession() => _facetSession;

    /// <summary>
    /// True when a StructEdit <see cref="IComponentEditService"/> has been wired (via the
    /// constructor or <see cref="SetFacetEditService"/>). Used by headless tests to verify
    /// the composition root forwarded the edit service so the Inspector renders live facet
    /// fields instead of the fallback stub (SE1 live-wiring).
    /// </summary>
    internal bool HasFacetEditService => _facetEditService is not null;

    /// <summary>
    /// True when an <c>expressionTargetFieldAccessor</c> delegate has been wired (B-3).
    /// Used by headless tests to verify the composition root forwarded the accessor so the
    /// "Static Parameters" panel is active for Action/Transition facets.
    /// </summary>
    internal bool HasExpressionTargetFieldAccessor => _expressionTargetFieldAccessor is not null;

    /// <summary>
    /// Returns the bound variable name for the current facet (if any) using the wired
    /// <c>expressionTargetFieldAccessor</c>. Returns null when no accessor is wired or the
    /// current facet does not carry an ExpressionTargetField. Safe to call from headless
    /// tests — does not require an ImGui context.
    /// </summary>
    internal string? GetCurrentExpressionTargetField()
    {
        if (_expressionTargetFieldAccessor is null) return null;
        var facet = GetCurrentFacet();
        return _expressionTargetFieldAccessor(facet);
    }

    /// <summary>
    /// Exposes the currently cached default-value StructEdit session for headless tests (B-3).
    /// Non-null only when a bound variable is active and an edit service is wired.
    /// </summary>
    internal IEditSession? GetDefaultValueSession() => _defaultValueSession;

    private void DisposeAndClearFacetSession()
    {
        _facetSession?.Dispose();
        _facetSession     = null;
        _facetSessionType = null;
        _facetSessionSub  = null;
    }

    private void DisposeAndClearDefaultValueSession()
    {
        _defaultValueSession?.Dispose();
        _defaultValueSession        = null;
        _defaultValueSessionVarName = null;
    }

    private void DrawCollisionDiagnosticStrip()
    {
        if (_schemaExporter is null) return;

        // GetBindingAmbiguities returns only genuine ambiguities for FQN-based binding.
        // Short-name collisions between distinct FQNs are harmless when binding is always
        // by full FQN — using GetBindingAmbiguities avoids false-positive error strips.
        var collisions = SubElementCollisionDetector.GetBindingAmbiguities(_schemaExporter);
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
