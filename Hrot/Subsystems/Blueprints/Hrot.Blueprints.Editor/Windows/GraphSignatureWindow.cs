using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// Editor window that lets an author edit a Function graph's signature:
/// its <see cref="Graph.Inputs"/> and <see cref="Graph.Outputs"/>
/// (<see cref="List{ParameterDecl}"/>: Name + <see cref="BlueprintTypeRef.TypeId"/>).
///
/// <para>
/// The window owns two <see cref="GraphSignatureEditModel"/> instances per selected
/// graph (one for Inputs, one for Outputs).  Mutations are automatically delegated
/// to the edit models; each model invokes its <c>onChanged</c> delegate which marks
/// the asset dirty via the <see cref="DirtyTracker"/> passed at construction time.
/// </para>
///
/// <para>
/// The active graph is chosen via a combo box over
/// <c>asset.Graphs.Where(g =&gt; g.Kind == GraphKind.Function)</c>
/// (BATCH-03D2: <see cref="EditorSelectionStore"/> exposes only
/// <see cref="EditorSelectionStore.SelectedAsset"/> — no active graph — so the
/// window carries its own <c>_selectedGraphId</c> view-state).
/// </para>
///
/// <para>
/// All ImGui calls are inside <see cref="DrawClientArea"/>; mutation logic lives
/// in the headless <see cref="GraphSignatureEditModel"/> so tests can drive the
/// model without a display context.  The headless seam is
/// <see cref="ResolveEditModels"/>.
/// </para>
/// </summary>
public sealed class GraphSignatureWindow : ManagedWindow
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker         _dirtyTracker;

    // ── view-state (graph picker) ─────────────────────────────────────────────
    private Guid _selectedGraphId;

    // ── cached asset ─────────────────────────────────────────────────────────
    private BlueprintAsset? _asset;

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="selectionStore">
    ///   Legacy <see cref="EditorSelectionStore"/> (Blueprints.Editor) driven by
    ///   the composition root's <c>ActiveChanged</c> handler.
    /// </param>
    /// <param name="dirtyTracker">
    ///   Shared dirty tracker; mutations fire
    ///   <c>dirtyTracker.MarkDirty(asset.AssetId)</c>.
    /// </param>
    /// <param name="idOverride">
    ///   Stable ImGui window id; defaults to <c>"ai_graph_signature_blueprint"</c>.
    /// </param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    public GraphSignatureWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker         dirtyTracker,
        string?              idOverride        = null,
        string?              owningPerspective = null)
        : base(idOverride        ?? "ai_graph_signature_blueprint",
               "Graph Signature",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retarget to a new active Blueprint asset (e.g. when the document changes).
    /// Resets the graph picker so the next frame re-selects the first Function graph.
    /// </summary>
    public void Retarget(BlueprintAsset? asset)
    {
        if (_asset == asset) return;
        _asset           = asset;
        _selectedGraphId = Guid.Empty;
    }

    /// <summary>
    /// Headless seam — mirrors <c>BlueprintDetailsWindow.ResolveSession()</c>.
    /// Returns the pair of <see cref="GraphSignatureEditModel"/> instances (Inputs,
    /// Outputs) for the currently-selected Function graph, or <c>null</c> when no
    /// asset / Function graph is selected.
    /// </summary>
    /// <remarks>
    /// Tests call this directly to drive mutations without touching ImGui.
    /// </remarks>
    public (GraphSignatureEditModel Inputs, GraphSignatureEditModel Outputs)? ResolveEditModels()
    {
        var asset = _asset ?? _selectionStore.SelectedAsset;
        if (asset == null) return null;

        var graph = ResolveSelectedGraph(asset);
        if (graph == null) return null;

        return BuildEditModels(graph, asset);
    }

    // ── ManagedWindow ─────────────────────────────────────────────────────────

    protected override void DrawClientArea()
    {
        var asset = _asset ?? _selectionStore.SelectedAsset;
        if (asset == null)
        {
            ImGuiNET.ImGui.TextDisabled("No blueprint selected.");
            return;
        }

        var functionGraphs = asset.Graphs
            .Where(g => g.Kind == GraphKind.Function)
            .ToList();

        if (functionGraphs.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No Function graphs in this blueprint.");
            return;
        }

        // ── Graph-picker combo ────────────────────────────────────────────────
        var selectedGraph = functionGraphs.FirstOrDefault(g => g.Id == _selectedGraphId)
                            ?? functionGraphs[0];
        _selectedGraphId = selectedGraph.Id;

        if (ImGuiNET.ImGui.BeginCombo("##graph_picker", selectedGraph.Name))
        {
            foreach (var g in functionGraphs)
            {
                bool isSelected = g.Id == _selectedGraphId;
                if (ImGuiNET.ImGui.Selectable(g.Name, isSelected))
                {
                    _selectedGraphId = g.Id;
                    selectedGraph    = g;
                }
                if (isSelected)
                    ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }

        ImGuiNET.ImGui.Separator();

        // ── Build edit models for selected graph ──────────────────────────────
        var (inputsModel, outputsModel) = BuildEditModels(selectedGraph, asset);

        // ── Inputs section ────────────────────────────────────────────────────
        ImGuiNET.ImGui.TextUnformatted("Inputs");
        DrawParameterRows("##inputs", selectedGraph.Inputs, inputsModel);

        ImGuiNET.ImGui.Spacing();

        // ── Outputs section ───────────────────────────────────────────────────
        ImGuiNET.ImGui.TextUnformatted("Outputs");
        DrawParameterRows("##outputs", selectedGraph.Outputs, outputsModel);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private Graph? ResolveSelectedGraph(BlueprintAsset asset)
    {
        var functionGraphs = asset.Graphs
            .Where(g => g.Kind == GraphKind.Function)
            .ToList();

        if (functionGraphs.Count == 0) return null;

        return functionGraphs.FirstOrDefault(g => g.Id == _selectedGraphId)
               ?? functionGraphs[0];
    }

    private (GraphSignatureEditModel Inputs, GraphSignatureEditModel Outputs)
        BuildEditModels(Graph graph, BlueprintAsset asset)
    {
        var assetId = asset.AssetId;
        var inputs  = new GraphSignatureEditModel(graph, false, () => _dirtyTracker.MarkDirty(assetId));
        var outputs = new GraphSignatureEditModel(graph, true,  () => _dirtyTracker.MarkDirty(assetId));
        return (inputs, outputs);
    }

    /// <summary>
    /// Renders an editable table of parameter rows (Name text-input + Type combo +
    /// Remove button) and a trailing "+ Add" row.  All ImGui calls are local to
    /// this method; mutations are routed through <paramref name="model"/>.
    /// </summary>
    private static void DrawParameterRows(
        string                          tableId,
        IReadOnlyList<ParameterDecl>    parameters,
        GraphSignatureEditModel         model)
    {
        const int    NameBufLen  = 256;
        const float  RemoveWidth = 24f;

        string? toRemove = null;

        if (ImGuiNET.ImGui.BeginTable(tableId, 3,
            ImGuiNET.ImGuiTableFlags.BordersInnerV | ImGuiNET.ImGuiTableFlags.SizingStretchProp))
        {
            ImGuiNET.ImGui.TableSetupColumn("Name",   ImGuiNET.ImGuiTableColumnFlags.WidthStretch,  1.5f);
            ImGuiNET.ImGui.TableSetupColumn("Type",   ImGuiNET.ImGuiTableColumnFlags.WidthStretch,  1.0f);
            ImGuiNET.ImGui.TableSetupColumn("##del",  ImGuiNET.ImGuiTableColumnFlags.WidthFixed,    RemoveWidth);
            ImGuiNET.ImGui.TableHeadersRow();

            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                ImGuiNET.ImGui.TableNextRow();

                // ── Name column ───────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(0);
                var nameBuf = System.Text.Encoding.UTF8.GetBytes(param.Name + "\0");
                Array.Resize(ref nameBuf, NameBufLen);
                ImGuiNET.ImGui.PushID($"name_{tableId}_{i}");
                if (ImGuiNET.ImGui.InputText("##n", nameBuf, (uint)nameBuf.Length))
                {
                    var newName = System.Text.Encoding.UTF8
                        .GetString(nameBuf)
                        .TrimEnd('\0');
                    if (newName != param.Name)
                        model.RenameParameter(param.Name, newName);
                }
                ImGuiNET.ImGui.PopID();

                // ── Type column ───────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(1);
                var typeNames  = BlackboardTypeHelper.DefaultKnownTypeNames;
                var typeId     = param.Type?.TypeId ?? "";
                var currentIdx = Enumerable.Range(0, typeNames.Count)
                    .FirstOrDefault(j => typeNames[j] == typeId, -1);
                if (currentIdx < 0) currentIdx = 0;

                ImGuiNET.ImGui.PushID($"type_{tableId}_{i}");
                if (ImGuiNET.ImGui.Combo("##t", ref currentIdx,
                    typeNames.ToArray(), typeNames.Count))
                {
                    model.RetypeParameter(param.Name, typeNames[currentIdx]);
                }
                ImGuiNET.ImGui.PopID();

                // ── Remove column ─────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(2);
                ImGuiNET.ImGui.PushID($"del_{tableId}_{i}");
                if (ImGuiNET.ImGui.SmallButton("X"))
                    toRemove = param.Name;
                ImGuiNET.ImGui.PopID();
            }

            ImGuiNET.ImGui.EndTable();
        }

        // Apply pending removal after iterating (avoid modifying list mid-loop).
        if (toRemove != null)
            model.RemoveParameter(toRemove);

        // ── "+ Add" row ───────────────────────────────────────────────────────
        if (ImGuiNET.ImGui.Button($"+##{tableId}_add"))
        {
            var defaultType = BlackboardTypeHelper.DefaultKnownTypeNames[0];
            model.AddParameter($"Param{model.Parameters.Count}", defaultType);
        }
    }
}
