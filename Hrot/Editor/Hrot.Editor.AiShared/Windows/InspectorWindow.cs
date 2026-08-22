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
/// Inspector window -- the asset header, plus the two sub-element panels that have not moved yet.
///
/// <para>⛔⛔ <b><c>S2</c> (<c>BP-399</c>, <c>2026-08-22</c>) — THE NODE ARMS ARE GONE FROM HERE.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.6 ②: the facet arm and the <c>B-3</c>
/// default-value arm were <b>EXTRACTED</b> to <c>Shell/NodePropertiesDetailsView</c> and are offered as
/// <c>details.nodeproperties</c> at Rank 20, so a selected node makes the <b>Details</b> panel show them
/// by default. ⇒ this window no longer dispatches facets and holds none of those services.</para>
///
/// <para>⚠ <b>What is STILL here, and why the class survives</b> *(📌 <c>BP-431</c>)*:
/// <list type="bullet">
///   <item>the <b>asset header</b> — name, <i>Find References</i>, <i>Rename…</i> — ⛔ ASSET-scoped, and
///   §7.6 gives it no home;</item>
///   <item>the <b>sub-element collision strip</b> — likewise a diagnostic with no home yet;</item>
///   <item><b>PARAMETER SYNCHRONIZATION</b> — <c>S4</c>, deliberately deferred until after the
///   orchestrator wiring *(<c>R-99</c>)*;</item>
///   <item>the <b>utility consideration</b> stub — <c>S3</c>.</item>
/// </list>
/// ⇒ ⛔ <b><c>S5</c> cannot delete this class until the first two have somewhere to go.</b></para>
/// </summary>
public sealed class InspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;
    private readonly Func<Guid, IBlackboardManagedAsset?>? _subAssetResolver;

    // Optional schema exporter for sub-element collision diagnostics (AIE-053).
    private readonly IActionSchemaExporter? _schemaExporter;

    // ⛔ S2: the facet dispatcher, the StructEdit edit service, its custom drawers, the
    //    ExpressionTargetField accessor, both sessions and the facet cache are GONE from this window —
    //    they are Shell/NodePropertiesSource now. See the note in DrawClientArea.

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
    public InspectorWindow(
        EditorSelectionStore store,
        IRefactorService refactorService,
        FindResultsWindow findResults,
        Func<Guid, IBlackboardManagedAsset?>? subAssetResolver = null,
        string? idOverride = null,
        string? owningPerspective = null,
        IActionSchemaExporter? schemaExporter = null)
        : base(idOverride ?? "ai_inspector", "Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
        _findResults = findResults;
        _subAssetResolver = subAssetResolver;
        _schemaExporter = schemaExporter;
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

        // ⛔⛔ S2 (BP-399 / BP-431) — THE FACET ARM AND THE DEFAULT-VALUE ARM ARE GONE FROM HERE.
        //    📄 DESIGN_Details_Panel_View_Switching.md §7.3's catalogue · §7.6 ② · §7.4's classDiagram
        //       (InspectorWindow ..> NodePropertiesDetailsView : content EXTRACTED to).
        //    ⭐ They are now Shell/NodePropertiesDetailsView, offered as `details.nodeproperties` at
        //       Rank 20 — so a selected node makes the Details panel show them BY DEFAULT, which is the
        //       user's 2026-08-22 ask.
        //    ⚠ BOTH arms moved together and that was measured, not assumed: the default-value arm read
        //      the SAME _currentFacet field this window cached, so extracting one alone would have
        //      forced a second facet cache (ruling 9). See BP-431.
        //    ⛔ The services that fed them (facet dispatcher, StructEdit edit service, custom drawers,
        //       the ExpressionTargetField accessor) moved to Shell/NodePropertiesSource, which the
        //       registrar owns and the composition root wires — this window no longer holds any of them.


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
    /// ⭐⭐⭐ Batch 92 (<c>92d</c>) — true when a sub-asset resolver has been wired.
    ///
    /// <para>⛔ Asserted on the CONSTRUCTED object, never on the registrar's source — 📌 <c>R-67</c>:
    /// <i>"a rail that builds its own composition root cannot see a composition-root defect."</i>
    /// 🔴 Without it, <c>DrawSyncBindingsTable</c>'s caller (<c>:449</c>) renders
    /// <i>"Sub-asset resolver not configured."</i> and the PARAMETER SYNCHRONIZATION panel is
    /// unusable.</para>
    /// </summary>
    internal bool HasSubAssetResolver => _subAssetResolver is not null;

    /// <summary>
    /// ⭐⭐ Runs the wired resolver. ⛔ <b>Non-null is not the property that matters</b> — a resolver
    /// that answers <c>null</c> for every id would satisfy <see cref="HasSubAssetResolver"/> and still
    /// leave the panel empty. ⇒ the rail asks this instead, so it reddens on a stubbed forward too.
    /// </summary>
    internal IBlackboardManagedAsset? ResolveSubAssetForRail(Guid assetId)
        => _subAssetResolver?.Invoke(assetId);

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
