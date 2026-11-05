using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using ImGuiNET;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐ CE-4210 P2 (MA-5) → ⭐ CE-4253 — the scenario / macro's PARAMETERS editor: the ONE Variables table (<see cref="VariablesPanelControl"/>) over the declared params
/// (<see cref="HeaderParamsSchemaSource"/>), plus Undo / Redo of its steps (a rename moves the declaration and every binding in ONE step). ⭐ CE-4253 — it was the
/// <c>ParametersWindow</c> behind <i>Scenario ▸ Parameters…</i>; the Scenario menu is gone and this body is now a DRAWER the Details ▸ Scenario view calls inside its
/// collapsible <i>Parameters</i> header (<see cref="ScenarioEnvironmentView"/>).
/// </summary>
public sealed class ParametersDrawer
{

    private sealed class ParamsAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "Parameters";
        public AssetKind Kind => AssetKind.Scenario;
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }
    }

    /// <summary>Parameters have no cross-asset key (<see cref="HeaderParamsSchemaSource.GetRefactorKey"/> is null), so the table never asks the refactor service.</summary>
    private sealed class NoRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options) => new(fromKey, toKey, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview preview) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid assetId, DeleteOptions options) => new(assetId, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview preview) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default) => Task.FromResult(PreviewRename(fromKey, toKey, options));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default) => Task.FromResult(ApplyRename(preview));
    }

    internal VariablesPanelControl Control => _control;

    /// <summary>Why the last rename was refused (CE-4254).</summary>
    public string? RenameRefusal => _control.RenameRefusal;

    private readonly HeaderParamsSchemaSource _source;
    private readonly VariablesPanelControl _control;

    /// <summary>Frame-rail probe (CE-4254): where the name cells were drawn.</summary>
    public System.Collections.Generic.IReadOnlyList<(string Name, System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)> NameCells => _control.DrawnNameCells;
    private readonly VariablesPanelSection _section;

    /// <param name="editService">The StructEdit service the default cell's leaf editors open over (null ⇒ the shared facet service, built on first use).</param>
    public ParametersDrawer(HeaderParamsSchemaSource source, Func<StructEdit.Core.IComponentEditService?>? editService = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        var choices = new List<VariableTypeChoice>();
        foreach (var (id, type) in Fdp.Toolkit.Scenario.ParamTypes.All()) choices.Add(new VariableTypeChoice(id, type));
        StructEdit.Core.IComponentEditService? own = null;
        _control = new VariablesPanelControl(new NoRefactor(), new ParamsAsset(), choices,
            editService ?? (() => own ??= Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build()));
        _section = new VariablesPanelSection("Parameters", "ce4210_params", _source, 0, 0, PackWarning.None, AliasingEnabled: false);
    }

    /// <summary>For rails: how many times the body was drawn (the header closed ⇒ not at all).</summary>
    internal int DrawCount { get; private set; }

    private bool _openRequested;

    /// <summary>⭐ CE-4253 — asks the Scenario view to show its Parameters header EXPANDED the next time it draws (the bind popup's <i>New parameter…</i> calls this).</summary>
    public void RequestOpen() => _openRequested = true;

    /// <summary>⭐ True once per <see cref="RequestOpen"/> — the view expands the header for that frame.</summary>
    public bool ConsumeOpenRequest() { bool r = _openRequested; _openRequested = false; return r; }

    /// <summary>The drawer's content: the refusal line, the table; Ctrl+Z / Ctrl+Y undo / redo (CE-4254 — the buttons are gone).</summary>
    public void Draw()
    {
        DrawCount++;
        if (_source.LastRefusal is { } why)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.45f, 0.4f, 1f), why);
        }
        ImGui.Separator();
        if (_source.IsReadOnly) { ImGui.TextDisabled("No scenario is loaded."); return; }
        _control.DrawSingle(_section);
        HandleUndoChords(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows));
    }

    /// <summary>
    /// ⭐ CE-4254 — Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z over the source's ONE history (the buttons are gone; the timeline panel's chord precedent). Only while the panel's window is focused and no text field is
    /// being typed into (a field's own text-undo is ImGui's). Returns what it did, for the rails.
    /// </summary>
    internal string? HandleUndoChords(bool focused)
    {
        var io = ImGui.GetIO();
        if (!focused || !io.KeyCtrl || io.WantTextInput) return null;
        var h = _source.History;
        bool z = ImGui.IsKeyPressed(ImGuiKey.Z), y = ImGui.IsKeyPressed(ImGuiKey.Y);
        if (y || (z && io.KeyShift)) { if (h.CanRedo) { h.Redo(); return "redo"; } }
        else if (z) { if (h.CanUndo) { h.Undo(); return "undo"; } }
        return null;
    }
}
