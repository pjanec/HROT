using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Windows;

// ── U-obs-5 view-model — one record per nested shape, enums projected to strings ────────────────────

/// <summary>⭐ One find-references hit, projected for the dump.</summary>
public sealed record FindResultsRowVM(
    Guid HostAssetId, string HostKind, Guid HostElementId, string HostDisplayPath,
    string TargetKey, string TargetKind, string SourceFilePath);

/// <summary>⭐ One line changed by a rename preview.</summary>
public sealed record RenameLineEditVM(int LineNumber, string OriginalText, string ReplacementText);

/// <summary>⭐ One file's edits within a rename preview.</summary>
public sealed record RenameFileEditVM(string FilePath, IReadOnlyList<RenameLineEditVM> LineEdits);

/// <summary>⭐ One issue surfaced by a rename preview.</summary>
public sealed record RenameIssueVM(string Severity, string Description);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="FindResultsWindow"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. <see cref="Mode"/> is the same
/// three-way branch <see cref="FindResultsWindow.DrawClientArea"/> already had, named as a value.
/// </summary>
public sealed record FindResultsPanelViewModel(
    string PanelId,
    string PanelKind,
    string Mode,   // "empty" | "results" | "rename-preview"
    string QueryLabel,
    IReadOnlyList<FindResultsRowVM> Results,
    string? RenameFromKey,
    string? RenameToKey,
    IReadOnlyList<RenameFileEditVM> RenameEdits,
    IReadOnlyList<RenameIssueVM> RenameIssues) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Shared window for find-references results and refactor preview.
/// Registered as "ai_find_results" in the Authoring perspective.
/// </summary>
public sealed class FindResultsWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Single-host: stays a local literal.</summary>
    internal const string Kind = "find-results";

    private string _queryLabel = string.Empty;
    private IReadOnlyList<AssetReferenceInfo>? _results;
    private RefactorPreview? _renamePreview;

    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_find_results_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    public FindResultsWindow(
        string? idOverride = null,
        string? owningPerspective = null)
        : base(idOverride ?? "ai_find_results", "Find Results",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>
    /// Show plain find-references results (no rename preview).
    /// </summary>
    public void ShowReferences(string query, IReadOnlyList<AssetReferenceInfo> results)
    {
        _queryLabel = query;
        _results = results;
        _renamePreview = null;
    }

    /// <summary>
    /// Show a rename preview with line edits visible.
    /// </summary>
    public void ShowRenamePreview(RefactorPreview preview)
    {
        _queryLabel = $"Rename: {preview.FromKey} -> {preview.ToKey}";
        _results = null;
        _renamePreview = preview;
    }

    /// <summary>
    /// ⭐⭐⭐ BUILD · CAPTURE. ⛔⛔ No ImGui — a pure projection of the last <see cref="ShowReferences"/> /
    /// <see cref="ShowRenamePreview"/> call, published before any render call.
    /// </summary>
    private FindResultsPanelViewModel BuildAndPublish()
    {
        string mode = _renamePreview != null ? "rename-preview" : _results != null ? "results" : "empty";

        IReadOnlyList<FindResultsRowVM> results = _results == null
            ? Array.Empty<FindResultsRowVM>()
            : _results.Select(r => new FindResultsRowVM(
                r.HostAssetId, r.HostKind.ToString(), r.HostElementId, r.HostDisplayPath,
                r.TargetKey, r.TargetKind.ToString(), r.SourceFilePath)).ToList();

        IReadOnlyList<RenameFileEditVM> edits  = Array.Empty<RenameFileEditVM>();
        IReadOnlyList<RenameIssueVM>    issues = Array.Empty<RenameIssueVM>();
        if (_renamePreview != null)
        {
            edits = _renamePreview.Edits.Select(fe => new RenameFileEditVM(
                fe.FilePath,
                fe.LineEdits.Select(le => new RenameLineEditVM(le.LineNumber, le.OriginalText, le.ReplacementText)).ToList()))
                .ToList();
            issues = _renamePreview.Issues.Select(i => new RenameIssueVM(i.Severity.ToString(), i.Description)).ToList();
        }

        var vm = new FindResultsPanelViewModel(
            Id, Kind, mode, _queryLabel, results,
            _renamePreview?.FromKey, _renamePreview?.ToKey, edits, issues);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal FindResultsPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        var vm = BuildAndPublish();

        if (vm.Mode == "rename-preview")
            DrawRenamePreview(vm);
        else if (vm.Mode == "results")
            DrawFindResults(vm);
        else
            ImGuiNET.ImGui.TextDisabled("No results. Use right-click on a reference to find usages.");
    }

    private static void DrawFindResults(FindResultsPanelViewModel vm)
    {
        ImGuiNET.ImGui.Text($"FIND RESULTS -- \"{vm.QueryLabel}\"  ({vm.Results.Count} references)");
        ImGuiNET.ImGui.Separator();

        var groups = vm.Results.GroupBy(r => r.SourceFilePath);
        foreach (var group in groups)
        {
            var header = $"{group.Key}  ({group.Count()} refs)";
            if (ImGuiNET.ImGui.TreeNodeEx(header, ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var r in group)
                {
                    ImGuiNET.ImGui.BulletText($"{r.HostKind}:{r.HostDisplayPath}  \"{r.TargetKey}\"");
                }
                ImGuiNET.ImGui.TreePop();
            }
        }
    }

    private static void DrawRenamePreview(FindResultsPanelViewModel vm)
    {
        ImGuiNET.ImGui.Text($"RENAME PREVIEW -- \"{vm.RenameFromKey}\" -> \"{vm.RenameToKey}\"");
        ImGuiNET.ImGui.Separator();

        foreach (var fileEdit in vm.RenameEdits)
        {
            var header = $"{fileEdit.FilePath}  ({fileEdit.LineEdits.Count} edits)";
            if (ImGuiNET.ImGui.TreeNodeEx(header, ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var lineEdit in fileEdit.LineEdits)
                {
                    ImGuiNET.ImGui.TextColored(
                        new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f),
                        $"  - L{lineEdit.LineNumber}: {lineEdit.OriginalText.Trim()}");
                    ImGuiNET.ImGui.TextColored(
                        new System.Numerics.Vector4(0.4f, 1f, 0.4f, 1f),
                        $"  + L{lineEdit.LineNumber}: {lineEdit.ReplacementText.Trim()}");
                }
                ImGuiNET.ImGui.TreePop();
            }
        }

        if (vm.RenameIssues.Count > 0)
        {
            ImGuiNET.ImGui.Separator();
            foreach (var issue in vm.RenameIssues)
            {
                ImGuiNET.ImGui.TextColored(
                    new System.Numerics.Vector4(1f, 0.8f, 0f, 1f),
                    $"[{issue.Severity}] {issue.Description}");
            }
        }
    }
}
