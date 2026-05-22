using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Search;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.UI.Panels;

/// <summary>
/// The "My Blueprint" outline panel.
/// Render once per frame inside any ImGui window region.
/// Reads <see cref="IMyBlueprintModel"/> data; writes through editor
/// commands and triggers navigation via callbacks supplied at construction.
/// </summary>
public sealed class MyBlueprintPanel
{
    private readonly IMyBlueprintModel _model;
    private readonly IEditorHostServices _host;
    private readonly IEditorCommands _commands;
    private readonly System.Action<GraphId> _navigateToGraph;
    private readonly System.Action<string, string> _navigateToItem;

    // Selection state.
    private MyBlueprintItem? _selectedItem;
    private string? _selectedSectionId;

    // Search state.
    private string _searchText = "";
    private readonly Dictionary<string, bool> _sectionExpanded = [];
    private readonly Dictionary<string, bool> _savedExpanded = [];   // restored on Esc
    private string? _preSearchExpansion;  // marker for "search active"

    /// <inheritdoc cref="IMyBlueprintModel.Changed"/>
    public event System.Action<MyBlueprintItem?>? SelectionChanged;

    /// <summary>The currently selected item.</summary>
    public MyBlueprintItem? SelectedItem => _selectedItem;

    /// <summary>The section id of the currently selected item.</summary>
    public string? SelectedSectionId => _selectedSectionId;

    /// <summary>
    /// Construct the panel.
    /// </summary>
    /// <param name="model">Data provider.</param>
    /// <param name="host">Host services (icons, theme).</param>
    /// <param name="commands">Command dispatcher.</param>
    /// <param name="navigateToGraph">Called on double-click of a graph entry.</param>
    /// <param name="navigateToItem">Called on double-click of any item (sectionId, itemId).</param>
    public MyBlueprintPanel(
        IMyBlueprintModel model,
        IEditorHostServices host,
        IEditorCommands commands,
        System.Action<GraphId> navigateToGraph,
        System.Action<string, string> navigateToItem)
    {
        _model           = model;
        _host            = host;
        _commands        = commands;
        _navigateToGraph = navigateToGraph;
        _navigateToItem  = navigateToItem;

        _model.Changed += () => { /* model changed — re-renders automatically next frame */ };
    }

    /// <summary>Draw the panel inside the current ImGui window/region.</summary>
    public void Draw()
    {
        DrawHeader();
        DrawSearchBox();
        ImGui.Separator();
        DrawSections();
    }

    // ── header ────────────────────────────────────────────────────────────────

    private void DrawHeader()
    {
        ImGui.TextColored(_host.Theme.TextDefault, "My Blueprint");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 48f);

        if (ImGui.SmallButton("\u2295\u25bc"))  // ⊕▼
            ImGui.OpenPopup("##mybp_add");

        if (ImGui.BeginPopup("##mybp_add"))
        {
            if (ImGui.MenuItem("+ Variable"))  InvokeCreate("editor.create-variable");
            if (ImGui.MenuItem("+ Function"))  InvokeCreate("editor.create-function");
            if (ImGui.MenuItem("+ Macro"))     InvokeCreate("editor.create-macro");
            if (ImGui.MenuItem("+ Custom Event")) InvokeCreate("editor.create-custom-event");
            if (ImGui.MenuItem("+ Event Dispatcher")) InvokeCreate("editor.create-event-dispatcher");
            ImGui.EndPopup();
        }
    }

    // ── search box ────────────────────────────────────────────────────────────

    private void DrawSearchBox()
    {
        ImGui.SetNextItemWidth(-1f);

        // "/" shortcut to focus.
        if (!ImGui.IsAnyItemActive() && ImGui.IsKeyPressed(ImGuiKey.Slash))
            ImGui.SetKeyboardFocusHere();

        string buf = _searchText;
        if (ImGui.InputTextWithHint("##mybp_search", "\uf002 search", ref buf, 256))
        {
            bool wasSearching = _searchText.Length > 0;
            bool nowSearching = buf.Length > 0;

            if (!wasSearching && nowSearching)
            {
                // Save expansion state before search.
                _savedExpanded.Clear();
                foreach (var kv in _sectionExpanded)
                    _savedExpanded[kv.Key] = kv.Value;
                _preSearchExpansion = "saved";
            }

            _searchText = buf;
        }

        if (ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            // Restore expansion state.
            if (_preSearchExpansion is not null)
            {
                _sectionExpanded.Clear();
                foreach (var kv in _savedExpanded)
                    _sectionExpanded[kv.Key] = kv.Value;
                _preSearchExpansion = null;
            }
            _searchText = "";
        }
    }

    // ── sections ──────────────────────────────────────────────────────────────

    private void DrawSections()
    {
        bool searching = _searchText.Length > 0;

        foreach (var section in _model.Sections.OrderBy(s => s.SortOrder))
        {
            var items = _model.GetItems(section.Id);

            // When searching, skip sections with no matches.
            List<(MyBlueprintItem item, FuzzyMatcher.Result match)>? searchResults = null;
            if (searching)
            {
                searchResults = items
                    .Select(it => (it, FuzzyMatcher.Score(_searchText, it.DisplayName, null)))
                    .Where(p => p.Item2.HasMatch)
                    .ToList();

                if (searchResults.Count == 0) continue;

                // Auto-expand sections with matches.
                _sectionExpanded[section.Id] = true;
            }

            DrawSection(section, items, searchResults);
        }
    }

    private void DrawSection(
        MyBlueprintSectionDescriptor section,
        IReadOnlyList<MyBlueprintItem> items,
        List<(MyBlueprintItem item, FuzzyMatcher.Result match)>? searchResults)
    {
        bool expanded = _sectionExpanded.GetValueOrDefault(section.Id, true);
        string label  = $"{section.DisplayName} ({items.Count})##sec_{section.Id}";

        if (ImGui.CollapsingHeader(label, expanded
                ? ImGuiTreeNodeFlags.DefaultOpen
                : ImGuiTreeNodeFlags.None))
        {
            _sectionExpanded[section.Id] = true;
            ImGui.Indent(8f);

            if (searchResults is not null)
            {
                // Flat list of matched items.
                foreach (var (item, match) in searchResults)
                    DrawItem(item, match.MatchPositions, false);
            }
            else
            {
                DrawItemsGrouped(section.Id, items, 0);
            }

            ImGui.Unindent(8f);
        }
        else
        {
            _sectionExpanded[section.Id] = false;
        }

        // Per-section ⊕ button at end of header.
        if (section.CanCreateItems && section.CreateCommandId is not null)
        {
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 16f);
            ImGui.SmallButton("\u2295##sec_add_" + section.Id);
            if (ImGui.IsItemClicked())
                InvokeCreate(section.CreateCommandId);
        }
    }

    // ── grouped items ─────────────────────────────────────────────────────────

    private void DrawItemsGrouped(string sectionId, IReadOnlyList<MyBlueprintItem> items, int depth)
    {
        // Group by first segment of CategoryPath.
        var byCategory = new SortedDictionary<string, List<MyBlueprintItem>>(StringComparer.OrdinalIgnoreCase);
        var uncategorized = new List<MyBlueprintItem>();

        foreach (var item in items)
        {
            if (item.CategoryPath is { Length: > 0 } cat)
            {
                string key = cat.Contains('/') ? cat[..cat.IndexOf('/')] : cat;
                if (!byCategory.TryGetValue(key, out var list))
                    byCategory[key] = list = [];
                list.Add(item);
            }
            else
            {
                uncategorized.Add(item);
            }
        }

        // Draw category folders first.
        foreach (var (catName, catItems) in byCategory)
        {
            string folderId = $"{sectionId}__cat_{catName}_{depth}";
            bool folderOpen = _sectionExpanded.GetValueOrDefault(folderId, true);

            if (ImGui.TreeNodeEx($"{catName}##fold_{folderId}",
                    folderOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
            {
                _sectionExpanded[folderId] = true;
                foreach (var item in catItems)
                    DrawItem(item, null, true);
                ImGui.TreePop();
            }
            else
            {
                _sectionExpanded[folderId] = false;
            }
        }

        // Then uncategorized items.
        foreach (var item in uncategorized)
            DrawItem(item, null, false);
    }

    // ── single item row ───────────────────────────────────────────────────────

    private void DrawItem(MyBlueprintItem item, IReadOnlyList<int>? matchPositions, bool indented)
    {
        bool isSelected = _selectedItem?.ItemId == item.ItemId;

        ImGui.PushID(item.ItemId);
        bool clicked = MyBlueprintItemRenderer.Render(
            item, isSelected, _host.Icons, _host.Theme, matchPositions, out bool dblClicked,
            () =>
            {
                if (MyBlueprintDragSource.BeginSource(item.ItemId, item.SectionId, item.DisplayName))
                    MyBlueprintDragSource.EndSource();

                MyBlueprintContextMenu.Draw(item, _commands, _navigateToItem);
            });
        ImGui.PopID();

        if (clicked)
        {
            _selectedItem     = item;
            _selectedSectionId = item.SectionId;
            SelectionChanged?.Invoke(item);
        }

        if (dblClicked)
            _navigateToItem(item.SectionId, item.ItemId);

    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void InvokeCreate(string commandId)
        => _commands.Invoke(commandId);
}
