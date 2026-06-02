using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shared window for find-references results and refactor preview.
/// Registered as "ai_find_results" in the Authoring perspective.
/// </summary>
public sealed class FindResultsWindow : ManagedWindow
{
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

    protected override void DrawClientArea()
    {
        if (_renamePreview != null)
        {
            DrawRenamePreview(_renamePreview);
        }
        else if (_results != null)
        {
            DrawFindResults(_queryLabel, _results);
        }
        else
        {
            ImGuiNET.ImGui.TextDisabled("No results. Use right-click on a reference to find usages.");
        }
    }

    private static void DrawFindResults(string query, IReadOnlyList<AssetReferenceInfo> results)
    {
        ImGuiNET.ImGui.Text($"FIND RESULTS -- \"{query}\"  ({results.Count} references)");
        ImGuiNET.ImGui.Separator();

        var groups = results.GroupBy(r => r.SourceFilePath);
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

    private static void DrawRenamePreview(RefactorPreview preview)
    {
        ImGuiNET.ImGui.Text($"RENAME PREVIEW -- \"{preview.FromKey}\" -> \"{preview.ToKey}\"");
        ImGuiNET.ImGui.Separator();

        foreach (var fileEdit in preview.Edits)
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

        if (preview.Issues.Count > 0)
        {
            ImGuiNET.ImGui.Separator();
            foreach (var issue in preview.Issues)
            {
                ImGuiNET.ImGui.TextColored(
                    new System.Numerics.Vector4(1f, 0.8f, 0f, 1f),
                    $"[{issue.Severity}] {issue.Description}");
            }
        }
    }
}
