using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common;
using FDP.Toolkit.DER;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Hrot.ExCon.Panels;

/// <summary>
/// Represents a single entry in the flattened ORBAT node list produced by
/// <see cref="OrbatPanel.GetVisibleNodes"/>.
/// </summary>
public sealed class OrbatNode
{
    /// <summary>The DER entity ID.</summary>
    public int EntityId { get; init; }

    /// <summary>Display name from <see cref="EntityInfo.Name"/>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Nesting depth (0 = root unit).</summary>
    public int Depth { get; init; }

    /// <summary>True if the entity has at least one direct subordinate.</summary>
    public bool HasChildren { get; init; }
}

/// <summary>
/// ExCon UI panel that renders the ORBAT (Order of Battle) hierarchy as a
/// collapsible tree based on <see cref="EntityInfo.CommanderId"/>.
///
/// <para><b>Cycle guard</b>: each call to <see cref="GetVisibleNodes"/> carries
/// its own <see cref="HashSet{T}"/> of visited entity IDs.  If a circular
/// <c>CommanderId</c> chain is detected (unit A → unit B → unit A) the traversal
/// skips the repeated node, so the method always terminates.</para>
///
/// <para><b>Depth cap</b>: recursion is additionally hard-stopped at
/// <see cref="PanelConstants.MaxOrbatDepth"/> levels as a second safety net
/// against malformed data.</para>
///
/// <para><b>Filter</b>: when <see cref="FilterText"/> is non-empty, only nodes
/// whose name contains the filter string (case-insensitive) are included in the
/// result; expansion state is ignored and the full subtree is scanned so that
/// deeply nested matches are surfaced.</para>
///
/// <para><b>Testing</b>: all business logic is in public/internal methods that
/// operate on plain <see cref="IDerRepo"/> inputs; no ImGui calls are required
/// to exercise the logic.</para>
/// </summary>
public sealed class OrbatPanel
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly HashSet<int> _expandedNodes = new();
    private string _filterText = string.Empty;
    
    private readonly List<TkbCatalogEntry> _catalog;
    private long _selectedType = 0;

    // Inject the catalog. Default to empty to avoid breaking parameterless tests.
    public OrbatPanel(IEnumerable<TkbCatalogEntry>? catalog = null)
    {
        _catalog = catalog?.ToList() ?? new List<TkbCatalogEntry>();
        if (_catalog.Count > 0)
        {
            _selectedType = _catalog[0].TkbId;
        }
    }

    // ── Public state accessors ────────────────────────────────────────────────

    /// <summary>
    /// Case-insensitive substring filter applied to entity names.
    /// An empty string matches every node.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set => _filterText = value ?? string.Empty;
    }

    /// <summary>Returns true if the given entity node is currently expanded.</summary>
    public bool IsExpanded(int entityId) => _expandedNodes.Contains(entityId);

    // ── Hierarchy queries (also called directly by unit tests) ────────────────

    /// <summary>
    /// Enumerates entities whose <see cref="EntityInfo.CommanderId"/> is 0
    /// (top-level / root units with no parent).
    /// </summary>
    public IEnumerable<IDerEntity> FindRootEntities(IDerRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        foreach (var entity in repo.GetAllEntities())
        {
            if (!entity.HasDescriptor<Hrot.NED.Descriptors.EntityInfo>()) continue;
            var info = entity.GetDescriptor<Hrot.NED.Descriptors.EntityInfo>();
            if (info.CommanderId == 0)
                yield return entity;
        }
    }

    /// <summary>
    /// Enumerates the direct subordinates of <paramref name="parentId"/>
    /// (entities whose <see cref="EntityInfo.CommanderId"/> matches).
    /// </summary>
    public IEnumerable<IDerEntity> FindChildren(int parentId, IDerRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        foreach (var entity in repo.GetAllEntities())
        {
            if (!entity.HasDescriptor<Hrot.NED.Descriptors.EntityInfo>()) continue;
            var info = entity.GetDescriptor<Hrot.NED.Descriptors.EntityInfo>();
            if (info.CommanderId == parentId)
                yield return entity;
        }
    }

    /// <summary>
    /// Returns true if <paramref name="name"/> satisfies the current filter
    /// string. An empty filter always returns true.  Comparison is
    /// case-insensitive (CODE-STANDARDS – no OrdinalIgnoreCase magic literal).
    /// </summary>
    public bool MatchesFilter(string name, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    // ── Expansion toggle ──────────────────────────────────────────────────────

    /// <summary>Toggles the expanded/collapsed state for the given entity node.</summary>
    public void ToggleExpanded(int entityId)
    {
        if (!_expandedNodes.Remove(entityId))
            _expandedNodes.Add(entityId);
    }

    // ── Entity click forwarding ───────────────────────────────────────────────

    /// <summary>
    /// Handles the operator clicking on an ORBAT node.
    /// Exposed as a separate method so tests can assert the selection
    /// side-effect without running an ImGui render frame.
    /// </summary>
    public void HandleEntityClick(int entityId, IExConLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        logic.SelectEntity(entityId);
    }

    /// <summary>
    /// Returns <c>true</c> when the entity identified by <paramref name="entityId"/>
    /// is a simulated (non-map-graphic) entity.
    ///
    /// <para>Returns <c>false</c> when the entity is not found in <paramref name="repo"/>
    /// or when <see cref="IDerEntity.TkbType"/> ≥ <c>8000</c> (map graphic range).</para>
    /// </summary>
    public static bool IsSimulatedEntity(int entityId, IDerRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        var entity = repo.GetEntity(entityId);
        if (entity == null) return false;
        return entity.TkbType < 8000;
    }

    /// <summary>
    /// Activates the IG placement tool for a new unit.
    ///
    /// <para><b>Architecture note</b>: this method intentionally performs <em>no</em>
    /// local DER repository mutations.  The authoritative lifecycle is:<br/>
    /// ExCon → <c>CMD_PLACE_ENTITY</c> → IG activates placement tool → operator
    /// clicks map → ExCon sends <c>CreateEntityRequest</c> → SimHost allocates
    /// a network ID and publishes <c>EntityMaster</c> + descriptors →
    /// <see cref="FDP.Toolkit.DER.MasterIngressHandler{T}"/> populates the DER repo
    /// → <c>CreateEntityAck</c> arrives → ExCon logs the new ID and auto-selects
    /// the entity.</para>
    /// </summary>
    public void HandleNewUnitClick(IExConLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        
        if (_selectedType == 0) return; // Guard against uninitialized selection

        var patch = new EntityPropertyPatch
        {
            Affiliation = eForceIdentifier.FORCE_FRIENDLY
        };
        var propsJson = JsonConvert.SerializeObject(patch, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() }
        });
        // Dynamically request the selected type instead of hardcoding Unit_InfantrySquad
        logic.StartPlacementMode(_selectedType, propsJson);
    }

    // ── Visible node list (testable, used by Draw in Phase P9) ────────────────

    /// <summary>
    /// Produces a depth-first flattened list of visible ORBAT nodes suitable
    /// for both rendering and unit testing.
    ///
    /// <list type="bullet">
    ///   <item>Cycle detection: a per-call visited set prevents each entity ID
    ///   appearing more than once.</item>
    ///   <item>Depth cap: recursion stops at
    ///   <see cref="PanelConstants.MaxOrbatDepth"/>.</item>
    ///   <item>Filtering: when <see cref="FilterText"/> is set only name-matching
    ///   nodes are included; the full subtree is searched regardless of expansion
    ///   state.</item>
    ///   <item>Collapse: when no filter is active, children of collapsed nodes
    ///   are not emitted.</item>
    /// </list>
    ///
    /// <para><b>O(n) complexity:</b> a single pass over all entities builds a
    /// <c>CommanderId → children</c> dictionary before the tree walk begins, so
    /// each entity is visited exactly once regardless of hierarchy depth.</para>
    /// </summary>
    public List<OrbatNode> GetVisibleNodes(IDerRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        var result  = new List<OrbatNode>();
        var visited = new HashSet<int>();

        // Build the children lookup in a single O(n) pass so that CollectNodes
        // never has to scan the full entity list again (eliminates O(n²) cost).
        var childrenLookup = BuildChildrenLookup(repo);

        foreach (var root in FindRootEntities(repo))
            CollectNodes(root, childrenLookup, result, visited, depth: 0);

        return result;
    }

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the ORBAT tree panel via ImGui.
    /// Called once per frame from the application shell (Phase P9).
    /// </summary>
    public void Draw(IExConLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ExConPanelColors.Push();
        ImGui.Begin("ORBAT Tree");
        ExConPanelColors.Pop();

        string filterBuf = _filterText;
        if (ImGui.InputText("Filter", ref filterBuf, PanelConstants.FilterTextMaxLength))
            FilterText = filterBuf;

        var nodes = GetVisibleNodes(logic.Repo);
        foreach (var node in nodes)
        {
            float indent = node.Depth * ImGui.GetStyle().IndentSpacing;
            if (indent > 0) ImGui.Indent(indent);

            bool isPendingDelete = logic.IsEntityPendingDelete(node.EntityId);
            if (isPendingDelete) ImGui.BeginDisabled();

            var flags = node.HasChildren
                ? ImGuiTreeNodeFlags.OpenOnArrow
                : ImGuiTreeNodeFlags.Leaf;
            string label = $"{node.Name} ({node.EntityId})";
            bool open = ImGui.TreeNodeEx(label, flags);
            if (ImGui.IsItemClicked()) HandleEntityClick(node.EntityId, logic);

            // ── Context menu ─────────────────────────────────────────────────
            // Use a per-node unique popup ID to avoid ImGui ID collisions.
            string popupId = $"##ctx_{node.EntityId}";
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup(popupId);

            if (ImGui.BeginPopup(popupId))
            {
                if (ImGui.MenuItem("Select entity"))
                    logic.SendSetSelection(node.EntityId);

                if (ImGui.MenuItem("Center on entity"))
                    logic.CenterOnEntity(node.EntityId);

                if (ImGui.MenuItem("Delete"))
                    logic.DeleteEntity(node.EntityId);

                if (IsSimulatedEntity(node.EntityId, logic.Repo))
                {
                    if (ImGui.MenuItem("Edit Route"))
                        logic.StartPersonalRouteAuthoring(node.EntityId);

                    if (ImGui.MenuItem("Abort Mission"))
                        _ = logic.MissionEditorService.SendControlCommandAsync(
                                node.EntityId,
                                eMissionCommandType.CMD_ABORT_ALL,
                                Guid.Empty);
                }

                ImGui.EndPopup();
            }
            // ── End context menu ─────────────────────────────────────────────

            if (open)
            {
                if (!_expandedNodes.Contains(node.EntityId)) ToggleExpanded(node.EntityId);
                ImGui.TreePop();
            }
            else if (_expandedNodes.Contains(node.EntityId))
                ToggleExpanded(node.EntityId);

            if (isPendingDelete) ImGui.EndDisabled();

            if (indent > 0) ImGui.Unindent(indent);
        }

        ImGui.Separator();

        // Render the Combo Box for Unit selection
        if (_catalog.Count > 0)
        {
            string previewText = _catalog.FirstOrDefault(c => c.TkbId == _selectedType)?.Name ?? "(none)";
            if (ImGui.BeginCombo("Unit Type", previewText))
            {
                foreach (var entry in _catalog)
                {
                    bool isSelected = entry.TkbId == _selectedType;
                    if (ImGui.Selectable($"{entry.Name} (Type:{entry.TkbId})", isSelected))
                        _selectedType = entry.TkbId;
                    
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        if (ImGui.Button("New Unit...")) HandleNewUnitClick(logic);
        ImGui.End();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <c>CommanderId → List&lt;IDerEntity&gt;</c> lookup from a
    /// single O(n) scan of the repository.  Used by
    /// <see cref="GetVisibleNodes"/> to avoid O(n²) repeated calls to
    /// <see cref="FindChildren"/> during tree traversal.
    /// </summary>
    private static Dictionary<int, List<IDerEntity>> BuildChildrenLookup(IDerRepo repo)
    {
        var lookup = new Dictionary<int, List<IDerEntity>>();

        foreach (var entity in repo.GetAllEntities())
        {
            if (!entity.HasDescriptor<Hrot.NED.Descriptors.EntityInfo>()) continue;
            var info = entity.GetDescriptor<Hrot.NED.Descriptors.EntityInfo>();

            // Only entities with a non-zero CommanderId are children.
            if (info.CommanderId == 0) continue;

            if (!lookup.TryGetValue(info.CommanderId, out var siblings))
            {
                siblings = new List<IDerEntity>();
                lookup[info.CommanderId] = siblings;
            }

            siblings.Add(entity);
        }

        return lookup;
    }

    private void CollectNodes(
        IDerEntity entity,
        Dictionary<int, List<IDerEntity>> childrenLookup,
        List<OrbatNode> result,
        HashSet<int>    visited,
        int depth)
    {
        // Safety net 1: depth cap
        if (depth >= PanelConstants.MaxOrbatDepth)
            return;

        // Safety net 2: cycle detection — each entity appears at most once
        if (!visited.Add(entity.EntityId))
            return;

        if (!entity.HasDescriptor<Hrot.NED.Descriptors.EntityInfo>())
            return;

        var info     = entity.GetDescriptor<Hrot.NED.Descriptors.EntityInfo>();

        // Use the pre-built lookup (O(1) lookup) instead of scanning all entities.
        childrenLookup.TryGetValue(entity.EntityId, out var children);
        bool hasChildren = children is { Count: > 0 };

        bool filtering = !string.IsNullOrEmpty(_filterText);

        // Emit this node when: no filter, or its name matches the filter.
        if (!filtering || MatchesFilter(info.Name ?? string.Empty, _filterText))
        {
            result.Add(new OrbatNode
            {
                EntityId    = entity.EntityId,
                Name        = info.Name ?? string.Empty,
                Depth       = depth,
                HasChildren = hasChildren
            });
        }

        // Recurse into children:
        //  - always recurse when a filter is active (scan the full subtree for matches)
        //  - recurse only into expanded nodes when no filter is active
        bool shouldRecurse = filtering || _expandedNodes.Contains(entity.EntityId);
        if (shouldRecurse && children is not null)
        {
            foreach (var child in children)
                CollectNodes(child, childrenLookup, result, visited, depth + 1);
        }
    }
}
