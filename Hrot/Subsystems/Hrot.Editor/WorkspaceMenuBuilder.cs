using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;

namespace Hrot.Editor;

/// <summary>
/// A single entry in the Workspace dynamic submenu.
/// Carries an icon key, display label, active/dirty markers, and an optional
/// select action (e.g. to activate the corresponding document).
/// </summary>
public sealed class WorkspaceMenuEntry
{
    /// <summary>
    /// The <see cref="IIconProvider"/> key for this entry's kind icon
    /// (e.g. <c>"asset/blueprint"</c>).
    /// </summary>
    public string IconKey { get; }

    /// <summary>Display label shown in the menu.</summary>
    public string Label { get; }

    /// <summary>
    /// <see langword="true"/> when this entry represents the active/open item
    /// (the active document). Renderers prepend <c>"● "</c>.
    /// </summary>
    public bool IsActive { get; }

    /// <summary>
    /// <see langword="true"/> when this entry has unsaved changes.
    /// Renderers prepend <c>"* "</c>.
    /// </summary>
    public bool IsDirty { get; }

    /// <summary>
    /// Action invoked when the user selects this entry in the menu.
    /// May be <see langword="null"/> for read-only entries (e.g. scenario).
    /// </summary>
    public Action? OnSelect { get; }

    /// <summary>
    /// Creates a workspace menu entry.
    /// </summary>
    public WorkspaceMenuEntry(
        string  iconKey,
        string  label,
        bool    isActive,
        bool    isDirty,
        Action? onSelect)
    {
        IconKey  = iconKey  ?? throw new ArgumentNullException(nameof(iconKey));
        Label    = label    ?? throw new ArgumentNullException(nameof(label));
        IsActive = isActive;
        IsDirty  = isDirty;
        OnSelect = onSelect;
    }
}

/// <summary>
/// Pure-logic builder for the <b>Workspace</b> dynamic submenu (§12.2).
/// Rebuilt from live state on every <see cref="Build"/> call — no stale caching.
/// </summary>
/// <remarks>
/// <para>
/// The returned list aggregates:
/// <list type="bullet">
///   <item>Every open document from <see cref="AiDocumentManager.OpenDocuments"/>
///       with active ● / dirty * markers and the kind icon from
///       <see cref="AssetKindIcons.GetIconKey"/>.</item>
///   <item>The loaded scenario (<see cref="IEditorLogic.LoadedScenarioName"/>)
///       when present, with the scenario icon and no select action.</item>
/// </list>
/// Selecting a document entry calls <see cref="AiDocumentManager.Activate"/>.
/// </para>
/// <para>
/// This class has <b>no ImGui dependency</b> — menus built from its output
/// live in the composition root.
/// </para>
/// </remarks>
public static class WorkspaceMenuBuilder
{
    /// <summary>
    /// Builds the ordered list of workspace menu entries from the current live state.
    /// </summary>
    /// <param name="docManager">The document manager (never <see langword="null"/>).</param>
    /// <param name="editorLogic">The editor logic facade (never <see langword="null"/>).</param>
    /// <returns>A fresh list of entries reflecting the current state.</returns>
    public static IReadOnlyList<WorkspaceMenuEntry> Build(
        AiDocumentManager docManager,
        IEditorLogic      editorLogic)
    {
        if (docManager  is null) throw new ArgumentNullException(nameof(docManager));
        if (editorLogic is null) throw new ArgumentNullException(nameof(editorLogic));

        var entries = new List<WorkspaceMenuEntry>();

        // ── Open documents (active ●, dirty *) ──────────────────────────────
        var activeDoc = docManager.Active;
        foreach (var doc in docManager.OpenDocuments)
        {
            var iconKey = AssetKindIcons.GetIconKey(doc.Kind);
            var isActive = doc == activeDoc;
            // Capture the doc reference for the closure.
            var capturedDoc = doc;
            entries.Add(new WorkspaceMenuEntry(
                iconKey,
                doc.Asset.Name,
                isActive,
                doc.IsDirty,
                onSelect: () => docManager.Activate(capturedDoc)));
        }

        // ── Loaded scenario ─────────────────────────────────────────────────
        var scenarioName = editorLogic.LoadedScenarioName;
        if (!string.IsNullOrEmpty(scenarioName))
        {
            entries.Add(new WorkspaceMenuEntry(
                AssetKindIcons.ScenarioIconKey,
                scenarioName,
                isActive: false,
                isDirty:  false,
                onSelect: null));
        }

        return entries.AsReadOnly();
    }
}
