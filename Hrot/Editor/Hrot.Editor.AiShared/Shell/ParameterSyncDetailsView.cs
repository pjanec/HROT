using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — the table's decision, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
/// ⭐ <see cref="SubVariableCount"/> is a projection of <see cref="ParameterSyncModel.SubVariables"/>,
/// not the list itself — the queue's own gotcha against reflecting/dumping a model that can carry
/// non-JSON-shaped members (here, entries that reference field-type CLR objects).
/// </summary>
public sealed record ParameterSyncDetailsViewPanelViewModel(
    string PanelId,
    string PanelKind,
    Guid?  NodeVisualId,
    string? Refusal,
    int    SubVariableCount) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐⭐ <b><c>S4</c> — PARAMETER SYNCHRONIZATION, as a Details view.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue · §7.6 ④. ⭐ The last of
/// <c>BP-399</c>'s five rows.
///
/// <para>⛔⛔ <b>Why it was LAST, and why it is safe NOW.</b> 📌 <c>R-99</c>: <i>"promoting an inert
/// panel is worse than leaving it buried."</i> ⚠ The bindings this table authors are consumed by a
/// generated <c>*.Orchestrators.g.cs</c>, and until <c>2026-08-22</c> that chain was broken in two
/// places — the sub-tree IDENTITY lived only in a UI draw *(<c>Q49</c> gap ①)* and the master blackboard
/// declared no slice for the body to write through *(<c>Q50</c> gap ②)*. ⭐ <b>Both are closed</b>
/// *(<c>BP-440</c>–<c>BP-447</c>)*, so the panel now authors data that reaches the runtime.</para>
///
/// <para>⚠ <b>ONE limit, stated rather than implied</b> *(<c>BP-446</c>)*: a callee whose blackboard is
/// GENERATED (Category-2) is not resolvable while the master is generated, so its asset is <b>skipped</b>
/// with a <c>BTREE0002</c> diagnostic. ⇒ ⭐ the bindings still persist and the table still works; what
/// does not yet happen is the emission. ⛔ That is a generator gap recorded in <c>Q50</c>, not a reason
/// to keep this panel buried — the designer's authoring is real either way.</para>
///
/// <para>⭐⭐ <b>EXTRACTED, not wrapped</b> — 📌 §7.4's <c>..&gt;</c>. The table, the combo, the two
/// checkboxes and every <c>SetSyncBinding</c> call are <c>InspectorWindow</c>'s, moved; that copy is
/// DELETED in the same commit.</para>
///
/// <para>⭐ <b>What changed in the move, and it is the same three as every other view:</b>
/// ① selection and asset come from the <b>CONTEXT</b>, not <c>EditorSelectionStore</c> *(§2)*;
/// ② the sub-asset resolver is a per-PERSPECTIVE service on <see cref="ParameterSyncSource"/>, because a
/// view instance is per-WINDOW *(<c>R-120</c>)*;
/// ③ ⛔ the <i>"select an asset to begin"</i> line did NOT come across — 📌 <c>R-117</c>: an empty panel
/// is the SHELL's answer, and a view that claims the panel to apologise is that defect one floor down.</para>
/// </summary>
public sealed class ParameterSyncDetailsView : IDetailsViewInstance
{
    private readonly ParameterSyncSource _source;

    public ParameterSyncDetailsView(ParameterSyncSource source)
        => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// ⭐⭐⭐ <b>What the panel has to say, as a MODEL</b> — 📌 <c>R-21</c>/<c>R-62</c>: the draw is
    /// unrailed by construction, so every branch a designer can hit is decided here where a rail can
    /// assert it. ⚠ <see langword="null"/> ⇒ nothing to show and the view does not claim the panel.
    /// </summary>
    public ParameterSyncModel? Model(DetailsContext context) => _source.ModelFor(context);

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. ⛔⛔ CORRECTED ORDER vs. the original body — the old code
    /// opened with the ImGui-context guard, so a headless call never reached <see cref="Model"/> at all.
    /// 📄 Same deviation as the design's own AS-BUILT ①.</summary>
    private (ParameterSyncModel? Model, ParameterSyncDetailsViewPanelViewModel Vm) BuildAndPublish(
        DetailsContext context, string idScope)
    {
        var model   = Model(context);
        var panelId = $"{idScope}/{ParameterSyncDetailsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        var vm = new ParameterSyncDetailsViewPanelViewModel(
            panelId, ParameterSyncDetailsViewDescriptor.ViewId,
            model?.NodeVisualId, model?.Refusal, model?.SubVariables?.Count ?? 0);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return (model, vm);
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ParameterSyncDetailsViewPanelViewModel SimulateDraw(DetailsContext context, string idScope)
        => BuildAndPublish(context, idScope).Vm;

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        var (model, _) = BuildAndPublish(context, idScope);

        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;
        if (model is not { } m) return;

        ImGuiNET.ImGui.TextUnformatted("PARAMETER SYNCHRONIZATION");

        if (m.Refusal is { } refusal)
        {
            ImGuiNET.ImGui.TextDisabled(refusal);
            return;
        }

        DrawTable(m);
    }

    /// <summary>⭐ The table, moved verbatim from <c>InspectorWindow.DrawSyncBindingsTable</c>.</summary>
    private static void DrawTable(ParameterSyncModel model)
    {
        var syncAsset = model.SyncAsset!;
        var subVars   = model.SubVariables!;
        var nodeId    = model.NodeVisualId;

        var bindingMap = new Dictionary<string, SubtreeSyncBinding>(StringComparer.Ordinal);
        foreach (var b in syncAsset.GetSyncBindings(nodeId)) bindingMap[b.FieldName] = b;

        if (!ImGuiNET.ImGui.BeginTable("##sync_params", 4,
                ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
            return;

        ImGuiNET.ImGui.TableSetupColumn("Field",    ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
        ImGuiNET.ImGui.TableSetupColumn("Bound to", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
        ImGuiNET.ImGui.TableSetupColumn("In",       ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 24f);
        ImGuiNET.ImGui.TableSetupColumn("Out",      ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 24f);
        ImGuiNET.ImGui.TableHeadersRow();

        for (int i = 0; i < subVars.Count; i++)
        {
            var field = subVars[i];
            bindingMap.TryGetValue(field.Name, out var binding);
            bool    syncIn    = binding?.SyncIn  ?? false;
            bool    syncOut   = binding?.SyncOut ?? false;
            string? masterVar = binding?.MasterVariableName;

            ImGuiNET.ImGui.TableNextRow();
            ImGuiNET.ImGui.PushID(i);

            ImGuiNET.ImGui.TableNextColumn();
            string fieldTypeName = BlackboardTypeHelper.GetDisplayName(field.FieldType);
            ImGuiNET.ImGui.TextUnformatted($"{field.Name} : {fieldTypeName}");

            ImGuiNET.ImGui.TableNextColumn();
            if (ImGuiNET.ImGui.BeginCombo($"##bound_{i}", masterVar ?? "(none)"))
            {
                bool noneSelected = masterVar is null;
                if (ImGuiNET.ImGui.Selectable("(none)", noneSelected) && !noneSelected)
                    syncAsset.SetSyncBinding(nodeId, new SubtreeSyncBinding(field.Name, null, syncIn, syncOut));

                foreach (var cand in syncAsset.GetVariablesOfType(fieldTypeName))
                {
                    bool selected = cand.Name == masterVar;
                    if (ImGuiNET.ImGui.Selectable(cand.Name, selected) && !selected)
                        syncAsset.SetSyncBinding(nodeId,
                            new SubtreeSyncBinding(field.Name, cand.Name, syncIn, syncOut));
                }
                ImGuiNET.ImGui.EndCombo();
            }

            ImGuiNET.ImGui.TableNextColumn();
            if (ImGuiNET.ImGui.Checkbox($"##in_{i}", ref syncIn))
                syncAsset.SetSyncBinding(nodeId, new SubtreeSyncBinding(field.Name, masterVar, syncIn, syncOut));

            ImGuiNET.ImGui.TableNextColumn();
            if (ImGuiNET.ImGui.Checkbox($"##out_{i}", ref syncOut))
                syncAsset.SetSyncBinding(nodeId, new SubtreeSyncBinding(field.Name, masterVar, syncIn, syncOut));

            ImGuiNET.ImGui.PopID();
        }

        ImGuiNET.ImGui.EndTable();
    }

    /// <summary>⭐ No session, no cache, no subscription — the view reads the asset live each frame, as
    /// the retired arm did.</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐⭐ <b>What the panel decided, before any pixel</b> — the seam that makes <c>S4</c> railable at all.
/// ⛔ The retired arm interleaved four refusals with the draw, so none of them could be asserted; here a
/// refusal is a <b>value</b>.
/// </summary>
/// <param name="NodeVisualId">The selected subtree node.</param>
/// <param name="Refusal">
/// ⭐ Non-<see langword="null"/> when the table cannot be drawn, carrying <b>which</b> reason —
/// 📌 the <c>B98b</c> discipline: <i>say WHICH refusal it is</i>, never one sentence for four causes.
/// </param>
/// <param name="SyncAsset">The master asset; <see langword="null"/> when <paramref name="Refusal"/> is set.</param>
/// <param name="SubVariables">The callee's blackboard variables; <see langword="null"/> when refused.</param>
public sealed record ParameterSyncModel(
    Guid                                    NodeVisualId,
    string?                                 Refusal,
    IBTreeSyncableAsset?                    SyncAsset,
    IReadOnlyList<BlackboardVariableEntry>? SubVariables);

/// <summary>
/// ⭐⭐ <b><c>S4</c> — the per-PERSPECTIVE service the view needs.</b> 📌 <c>R-120</c>: a view instance is
/// per-WINDOW *(docked · float · pin)*, but the sub-asset resolver is per-perspective and the composition
/// root re-wires it when the document changes.
///
/// <para>⭐⭐⭐ <b>The PREDICATE and the DRAW ask the same question through <see cref="ModelFor"/></b> —
/// 📌 the lesson <c>NodePropertiesSource</c> records: two answers to <i>"is there anything to show?"</i>
/// produce a view that claims the panel and renders nothing, which is <c>R-117</c> one floor down.</para>
/// </summary>
public sealed class ParameterSyncSource
{
    private Func<Guid, IBlackboardManagedAsset?>? _subAssetResolver;

    /// <summary>
    /// ⭐⭐ Wire the resolver. ⛔ <b>The composition root HAS the catalog and must pass it</b> — 📌 the
    /// <c>2026-08-16</c> rule, and 🔴 <c>BP-340</c>/<c>92d</c> is this exact seam's own history: the
    /// registrar omitted it while holding the catalog two lines up, and the panel rendered
    /// <i>"Sub-asset resolver not configured."</i> for every designer.
    /// </summary>
    public void SetSubAssetResolver(Func<Guid, IBlackboardManagedAsset?> resolver)
        => _subAssetResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <summary>⭐ True once the root has wired the resolver — the rail surface *(<c>R-67</c>)*.</summary>
    public bool HasSubAssetResolver => _subAssetResolver is not null;

    /// <summary>⭐⭐ Runs the wired resolver. ⛔ Non-null is not the property that matters — a resolver
    /// answering <c>null</c> for every id would satisfy <see cref="HasSubAssetResolver"/> and still leave
    /// the panel empty, so the composition-root rail asks THIS.</summary>
    public IBlackboardManagedAsset? ResolveSubAsset(Guid assetId) => _subAssetResolver?.Invoke(assetId);

    /// <summary>⭐ Does this context have a parameter-sync panel to show? The predicate's half.</summary>
    public bool CanShow(DetailsContext context) => ModelFor(context) is not null;

    /// <summary>
    /// ⭐⭐⭐ <b>The whole decision, in one function.</b> ⛔ <see langword="null"/> ⇒ the view does not
    /// claim the panel at all *(not a subtree node, or not a syncable asset)*. ⚠ A non-null model with a
    /// <c>Refusal</c> is different: the node IS a subtree node, so the panel is the right place to say
    /// <b>why</b> it cannot be edited.
    /// </summary>
    public ParameterSyncModel? ModelFor(DetailsContext context)
    {
        if (context.Selection is not { Count: 1 } one) return null;
        if (one[0] is not BTreeNodeSelection nodeSel)  return null;
        if (context.Asset is not IBTreeSyncableAsset syncAsset) return null;

        var nodeInfo = syncAsset.GetSubtreeNodeInfo(nodeSel.VisualId);
        if (nodeInfo is null) return null;   // ⛔ a plain node — not this view's business.

        if (!nodeInfo.IsResolved)
            return new ParameterSyncModel(nodeSel.VisualId,
                "Subtree not resolved -- sync unavailable.", null, null);

        if (_subAssetResolver is null)
            return new ParameterSyncModel(nodeSel.VisualId,
                "Sub-asset resolver not configured.", null, null);

        var subAsset = _subAssetResolver(nodeInfo.SubtreeAssetId);
        if (subAsset is null)
            return new ParameterSyncModel(nodeSel.VisualId,
                "Sub-asset not found.", null, null);

        if (subAsset.BlackboardVariables.Count == 0)
            return new ParameterSyncModel(nodeSel.VisualId,
                "Sub-tree has no blackboard variables.", null, null);

        return new ParameterSyncModel(nodeSel.VisualId, null, syncAsset, subAsset.BlackboardVariables);
    }
}

/// <summary>⭐ <c>S4</c> — the descriptor. 📄 §7.3's catalogue.</summary>
public static class ParameterSyncDetailsViewDescriptor
{
    /// <summary>⭐ §7.6 ④'s id.</summary>
    public const string ViewId = "details.parametersync";

    /// <summary>
    /// ⭐⭐ <b>Rank 15 — between Variables (10) and Node properties (20), deliberately.</b>
    /// ⛔ NOT above node properties: selecting a subtree node, the designer most often means <i>"what is
    /// this node"</i>, and 📌 <c>R-98</c> makes the toolbar pick sticky per context key — so a designer
    /// wiring parameters picks it once and keeps it. ⭐ Above Variables because a node IS selected, which
    /// is a narrower statement than the asset's variable list.
    /// </summary>
    public const int Rank = 15;

    /// <summary>⭐ A FRESH view per window *(<c>R-120</c>)*; the SOURCE is shared per perspective.</summary>
    public static DetailsViewDescriptor For(ParameterSyncSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Parameter Sync",
            Rank:      Rank,
            // ⭐ ONE question, asked once — see ParameterSyncSource.ModelFor.
            AppliesTo: source.CanShow,
            Create:    () => new ParameterSyncDetailsView(source));
    }
}
