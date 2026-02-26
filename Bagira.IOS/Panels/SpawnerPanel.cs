using Bagira.BDC.SSTD;
using ImGuiNET;

namespace Bagira.IOS.Panels;

/// <summary>
/// A lightweight, immutable entry in the <see cref="SpawnerPanel"/> catalog
/// list.  Constructed from a TKB template at application-shell startup and
/// passed into the panel constructor.
/// </summary>
/// <param name="TkbId">TKB type identifier (matches <c>TkbTemplate.TkbType</c>).</param>
/// <param name="Name">Display name shown in the spawner list.</param>
public sealed record TkbCatalogEntry(long TkbId, string Name);

/// <summary>
/// IOS UI panel that lets the operator browse the TKB entity type catalog,
/// search/filter entries, choose a force affiliation, and activate the map
/// placement tool.
///
/// <para><b>No LINQ in Draw</b>: the filtered entry list
/// (<see cref="FilteredEntries"/>) is rebuilt whenever
/// <see cref="SearchFilter"/> changes and cached as a plain
/// <see cref="List{T}"/>; <see cref="Draw"/> iterates it with a plain
/// <c>for</c>-loop (CODE-STANDARDS §4).</para>
///
/// <para><b>Case-insensitive filter</b>: matching uses
/// <see cref="StringComparison.OrdinalIgnoreCase"/> so "t-72", "T-72", and
/// "T-72" all produce the same result.</para>
///
/// <para><b>Testing</b>: <see cref="FilteredEntries"/>,
/// <see cref="HandleTypeSelected"/>, <see cref="HandleAffiliationChange"/>, and
/// <see cref="HandleActivatePlacementTool"/> are all callable without ImGui.</para>
/// </summary>
public sealed class SpawnerPanel
{
    // ── Catalog (set at construction, never mutated afterwards) ───────────────

    private readonly List<TkbCatalogEntry> _catalog;

    // ── Filtered view (pre-computed, updated when SearchFilter changes) ───────

    private readonly List<TkbCatalogEntry> _filteredEntries;

    // ── State ─────────────────────────────────────────────────────────────────

    private string          _searchFilter = string.Empty;
    private long            _selectedType = 0;
    private eForceIdentifier _affiliation = eForceIdentifier.FORCE_FRIENDLY;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a panel pre-loaded with the given catalog entries.
    /// </summary>
    public SpawnerPanel(IEnumerable<TkbCatalogEntry> catalog)
    {
        _catalog         = catalog?.ToList() ?? new List<TkbCatalogEntry>();
        _filteredEntries = new List<TkbCatalogEntry>(_catalog.Count);
        RebuildFilter();
    }

    /// <summary>Creates a panel with an empty catalog (useful in unit tests).</summary>
    public SpawnerPanel() : this(Array.Empty<TkbCatalogEntry>()) { }

    // ── Public state accessors ────────────────────────────────────────────────

    /// <summary>
    /// Substring filter applied to catalog names.
    /// Setting this property immediately rebuilds <see cref="FilteredEntries"/>.
    /// Comparison is case-insensitive.
    /// </summary>
    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            _searchFilter = value ?? string.Empty;
            RebuildFilter();
        }
    }

    /// <summary>TKB ID of the entity type currently highlighted in the list.</summary>
    public long SelectedType => _selectedType;

    /// <summary>Force affiliation selected by the radio buttons.</summary>
    public eForceIdentifier SelectedAffiliation => _affiliation;

    /// <summary>
    /// The current filtered entry list. Pre-computed — no LINQ allocations
    /// when iterated inside <see cref="Draw"/>.
    /// </summary>
    public IReadOnlyList<TkbCatalogEntry> FilteredEntries => _filteredEntries;

    // ── Button / control handlers (public for testability) ────────────────────

    /// <summary>
    /// Handles the operator selecting a catalog entry.
    /// Exposed as a separate method so tests can assert the selection
    /// side-effect without an active ImGui frame.
    /// </summary>
    public void HandleTypeSelected(long tkbId)
    {
        _selectedType = tkbId;
    }

    /// <summary>
    /// Handles the operator changing the force-affiliation radio button.
    /// </summary>
    public void HandleAffiliationChange(eForceIdentifier affiliation)
    {
        _affiliation = affiliation;
    }

    /// <summary>
    /// Handles the "ACTIVATE PLACEMENT TOOL" button press.
    /// Calls <see cref="IIosLogic.StartPlacementMode"/> with the currently
    /// selected type and affiliation.
    /// </summary>
    public void HandleActivatePlacementTool(IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        logic.StartPlacementMode(_selectedType, _affiliation);
    }

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the spawner panel via ImGui.
    /// Called once per frame from the application shell (Phase P9).
    ///
    /// <para>Iterates <see cref="_filteredEntries"/> with a plain <c>for</c>-loop
    /// — no LINQ allocations on the hot draw path (CODE-STANDARDS §4).</para>
    /// </summary>
    public void Draw(IIosLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ImGui.Begin("Entity Spawner");

        string filterBuf = _searchFilter;
        if (ImGui.InputText("Search", ref filterBuf, PanelConstants.FilterTextMaxLength))
            SearchFilter = filterBuf;

        // Plain for-loop — _filteredEntries was pre-built, no LINQ here.
        for (int i = 0; i < _filteredEntries.Count; i++)
        {
            var entry = _filteredEntries[i];
            if (ImGui.Selectable($"{entry.Name} (Type:{entry.TkbId})"))
                HandleTypeSelected(entry.TkbId);
        }

        ImGui.Separator();

        int aff = (int)_affiliation;
        if (ImGui.RadioButton("Friend",  ref aff, (int)eForceIdentifier.FORCE_FRIENDLY))
            HandleAffiliationChange((eForceIdentifier)aff);
        ImGui.SameLine();
        if (ImGui.RadioButton("Hostile", ref aff, (int)eForceIdentifier.FORCE_OPPOSING))
            HandleAffiliationChange((eForceIdentifier)aff);

        if (ImGui.Button("ACTIVATE PLACEMENT TOOL"))
            HandleActivatePlacementTool(logic);

        ImGui.End();
    }

    // ── Private filter rebuild ────────────────────────────────────────────────

    private void RebuildFilter()
    {
        _filteredEntries.Clear();
        foreach (var entry in _catalog)
        {
            if (MatchesFilter(entry.Name, _searchFilter))
                _filteredEntries.Add(entry);
        }
    }

    private static bool MatchesFilter(string name, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
