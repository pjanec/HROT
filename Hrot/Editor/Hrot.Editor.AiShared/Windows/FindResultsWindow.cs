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
/// <para>⭐ Registered per perspective by <c>PerspectiveWorkspaceRegistrar</c> as
/// <c>ai_find_results_&lt;perspective&gt;</c>, plus ONE <see cref="WindowScope.Global"/> instance for the
/// asset browser *(<c>ai_asset_browser_find_results</c>)*. ⛔ The old <c>"Authoring"</c> default is gone —
/// §1c/<c>A6</c>.</para>
/// </summary>
public sealed class FindResultsWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Single-host: stays a local literal.</summary>
    internal const string Kind = "find-results";

    private string _queryLabel = string.Empty;
    private IReadOnlyList<AssetReferenceInfo>? _results;
    private RefactorPreview? _renamePreview;

    /// <summary>
    /// ⭐⭐⭐ <b><c>A6</c> — <paramref name="owningPerspective"/> is REQUIRED, and <c>A5</c> —
    /// <paramref name="scope"/> is a PARAMETER.</b>
    /// 📄 <c>DESIGN_Perspective_Unification.md</c> §1c *("the LATENT generator" · the two bugs in one
    /// line)* · §3 <c>A5</c>/<c>A6</c>.
    ///
    /// <para>⛔⛔ <b>What the removed default did.</b> 📐 The signature was
    /// <c>owningPerspective = null</c> → <c>?? "Authoring"</c>, so <b>any caller that omitted the
    /// perspective silently INVENTED one.</b> ⭐ No production caller omitted it — ⛔ but that was luck,
    /// not a control, and 🔴 <c>"Global"</c> is what the same shape looks like when it fires: passing a
    /// SCOPE into the PERSPECTIVE slot produced a phantom perspective WITH A TOOLBAR ICON, and left the
    /// window reachable only from that phantom — the opposite of its stated intent.
    /// ⇒ ⭐⭐ <b>with the default gone, a phantom perspective is UNCONSTRUCTIBLE</b> rather than
    /// something a reviewer has to notice. 📌 The same reasoning as <c>CLAUDE.md</c>'s silent-default
    /// rule.</para>
    ///
    /// <para>⭐⭐ <b>And the scope had to become a parameter for <c>A5</c> to be possible at all</b> —
    /// 📐 this class HARD-CODED <see cref="WindowScope.PerspectiveBound"/>, so the globally-available
    /// asset-browser instance could not be expressed by changing an argument.</para>
    /// </summary>
    /// <param name="owningPerspective">
    ///   ⛔ <b>REQUIRED.</b> The perspective that owns this instance — or
    ///   <see cref="string.Empty"/> when <paramref name="scope"/> is <see cref="WindowScope.Global"/>
    ///   *(the <c>OrchestratorWindow</c> pattern: always visible, and invisible to
    ///   <c>GetPerspectives()</c>)*.
    /// </param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_find_results_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="scope">
    ///   Visibility scope. Defaults to <see cref="WindowScope.PerspectiveBound"/> — the per-perspective
    ///   instances the registrar creates.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   ⭐⭐ The two states that produced §1c's defect, refused at construction:
    ///   a <see cref="WindowScope.PerspectiveBound"/> window with no perspective *(blank — it can never
    ///   pass its own visibility gate)*, and a <see cref="WindowScope.Global"/> window that names one
    ///   *(misleading — <c>"Global"</c> is a scope, never a place)*.
    /// </exception>
    public FindResultsWindow(
        string owningPerspective,
        string? idOverride = null,
        WindowScope scope = WindowScope.PerspectiveBound)
        : base(idOverride ?? "ai_find_results", "Find Results",
               Validated(owningPerspective, scope), scope)
    {
        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>
    /// ⭐⭐ <c>A5</c>/<c>A6</c> — the scope/perspective pair must agree. ⛔ A base-call argument, so the
    /// refusal happens before a half-built window exists.
    /// </summary>
    private static string Validated(string owningPerspective, WindowScope scope)
    {
        if (owningPerspective == null)
            throw new ArgumentNullException(nameof(owningPerspective));

        bool named = owningPerspective.Length > 0;

        if (scope == WindowScope.PerspectiveBound && !named)
            throw new ArgumentException(
                "A PerspectiveBound FindResultsWindow needs a perspective — an empty one can never pass "
                + "its own visibility gate, so the window would be permanently invisible. "
                + "Pass WindowScope.Global for a globally-available instance.",
                nameof(owningPerspective));

        if (scope == WindowScope.Global && named)
            throw new ArgumentException(
                $"A Global FindResultsWindow must have an EMPTY perspective, got '{owningPerspective}'. "
                + "\"Global\" is a scope, not a place: a Global window is already visible everywhere, and "
                + "naming a perspective here only misleads the next reader.",
                nameof(owningPerspective));

        return owningPerspective;
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
