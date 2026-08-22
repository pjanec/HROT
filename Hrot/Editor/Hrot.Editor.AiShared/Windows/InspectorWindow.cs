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
    // ⛔ S5-prep: the refactor service and the Find Results window left with the asset header —
    //    their only consumers were "Find References" and "Rename…", now on the Asset Browser row menu.
    private readonly Func<Guid, IBlackboardManagedAsset?>? _subAssetResolver;

    // Optional schema exporter for sub-element collision diagnostics (AIE-053).
    private readonly IActionSchemaExporter? _schemaExporter;

    // ⛔ S2: the facet dispatcher, the StructEdit edit service, its custom drawers, the
    //    ExpressionTargetField accessor, both sessions and the facet cache are GONE from this window —
    //    they are Shell/NodePropertiesSource now. See the note in DrawClientArea.

    /// <param name="store">Editor selection store for this perspective.</param>
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
        Func<Guid, IBlackboardManagedAsset?>? subAssetResolver = null,
        string? idOverride = null,
        string? owningPerspective = null,
        IActionSchemaExporter? schemaExporter = null)
        : base(idOverride ?? "ai_inspector", "Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _subAssetResolver = subAssetResolver;
        _schemaExporter = schemaExporter;
    }

    protected override void DrawClientArea()
    {
        // ⛔⛔ AIE-053's collision strip is GONE from here (2026-08-22).
        //    🔒 User ruling: "it need to be routed to where the collision can be seen or fixed."
        //    ⭐ It is now a row in the DiagnosticsWindow's issue table — the shared window
        //      docs/designs/blueprint-integ-1/DESIGN.md §5.7 names — via SubElementCollisionDiagnostics.
        //    ⚠⚠ And the strip that stood here was DEAD: it called GetBindingAmbiguities, which returns
        //      Array.Empty UNCONDITIONALLY, so it could never draw on any input. The Diagnostics rows
        //      use GetCollisions — the real data — at Info severity, which is what the detector's own
        //      doc says the difference is.

        if (_store.ActiveAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        // ⛔⛔ THE ASSET HEADER IS GONE FROM HERE (2026-08-22).
        //    🔒 User ruling: "go to definition and rename and find references, these all sound like
        //       context menu items … asset related context menu items then, still nothing for a
        //       details panel view." · "picker should not have that menu."
        //    📄 AI_Editor_Shared_Infrastructure.md §16.1 agrees in its own words: Find References is
        //       "Used by THE RIGHT-CLICK MENU, the Find Results window, and indirectly by the rename
        //       preview" — operations 1 and 4 of five.
        //    ⭐ "Find References" and "Rename…" now live on the ASSET BROWSER's row context menu, which
        //      is where a designer points at an asset (AssetBrowserPanel.DrawRowContextMenu, opt-in via
        //      AssetBrowserPanelOptions.RowCommands so the PICKER does not get them).
        //    ⛔ "Go to Definition" did NOT move: measured, its body here was EMPTY ("placeholder --
        //      navigation wired in a later phase") while CommandCatalog.GoToDefinition on the graph is
        //      the real one (BP-76). A dead duplicate of a built feature is deleted, not relocated.

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

        // ⛔⛔ S3 (BP-399, 2026-08-22) — THE UTILITY CONSIDERATION ARM IS GONE FROM HERE.
        //    📄 DESIGN_Details_Panel_View_Switching.md §7.6 ③: "port it honestly as a stub, do not
        //       pretend it is a feature." It is now Shell/UtilityConsiderationDetailsView, offered as
        //       `details.utility`.
        //    ⚠ It was UNREACHABLE here and still is there — nothing raises UtilityConsiderationSelection
        //      (measured: two C# sites in the repo, both of them declarations). ⭐ PORTED not DELETED
        //      because the design record claims it: docs/designs/utility-ai/ specifies the editor, and
        //      .dev/_DONE/utility-ai/batches/BATCH-14-INSTRUCTIONS.md §1d added this very arm on purpose.
    }

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
