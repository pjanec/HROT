using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using FDP.Toolkit.DER;
using ImGuiNET;

namespace Bagira.IOS.Panels;

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
/// IOS UI panel that renders the ORBAT (Order of Battle) hierarchy as a
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
            if (!entity.HasDescriptor<EntityInfo>()) continue;
            var info = entity.GetDescriptor<EntityInfo>();
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
            if (!entity.HasDescriptor<EntityInfo>()) continue;
            var info = entity.GetDescriptor<EntityInfo>();
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
    public void HandleEntityClick(int entityId, IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        logic.SelectEntity(entityId);
    }

    /// <summary>
    /// Creates a placeholder unit in the DER repository and selects it.
    /// </summary>
    public void HandleNewUnitClick(IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);

        var repo = logic.Repo;
        int newId = GetNextEntityId(repo);
        var entity = repo.CreateEntity(newId, TkbEntityTypes.Unit_InfantrySquad);

        entity.SetDescriptor(new EntityInfo
        {
            EntityId        = newId,
            Name            = $"New Unit {newId}",
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
            CommanderId     = 0,
        });

        logic.SelectEntity(newId);

        // Start placement mode so the operator can click the map to send a
        // CreateEntityRequest to SimHost — without this, the entity only exists
        // in the local DER repository and never appears on the shared map.
        logic.StartPlacementMode(TkbEntityTypes.Unit_InfantrySquad, eForceIdentifier.FORCE_FRIENDLY);
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
    public void Draw(IIosLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ImGui.Begin("ORBAT Tree");

        string filterBuf = _filterText;
        if (ImGui.InputText("Filter", ref filterBuf, PanelConstants.FilterTextMaxLength))
            FilterText = filterBuf;

        var nodes = GetVisibleNodes(logic.Repo);
        foreach (var node in nodes)
        {
            var flags = node.HasChildren
                ? ImGuiTreeNodeFlags.OpenOnArrow
                : ImGuiTreeNodeFlags.Leaf;
            string label = $"{node.Name} ({node.EntityId})";
            bool open = ImGui.TreeNodeEx(label, flags);
            if (ImGui.IsItemClicked()) HandleEntityClick(node.EntityId, logic);
            if (open)
            {
                if (!_expandedNodes.Contains(node.EntityId)) ToggleExpanded(node.EntityId);
                ImGui.TreePop();
            }
            else if (_expandedNodes.Contains(node.EntityId))
                ToggleExpanded(node.EntityId);
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
            if (!entity.HasDescriptor<EntityInfo>()) continue;
            var info = entity.GetDescriptor<EntityInfo>();

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

    private static int GetNextEntityId(IDerRepo repo)
    {
        int maxId = 0;
        foreach (var entity in repo.GetAllEntities())
            if (entity.EntityId > maxId) maxId = entity.EntityId;

        return maxId + 1;
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

        if (!entity.HasDescriptor<EntityInfo>())
            return;

        var info     = entity.GetDescriptor<EntityInfo>();

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
