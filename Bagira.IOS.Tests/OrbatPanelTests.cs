using Bagira.BDC.SSTD;
using Bagira.IOS.Panels;
using FDP.Toolkit.DER;
using Moq;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="OrbatPanel"/>.
///
/// Tests drive the panel through its public API
/// (<see cref="OrbatPanel.FindRootEntities"/>,
/// <see cref="OrbatPanel.FindChildren"/>,
/// <see cref="OrbatPanel.GetVisibleNodes"/>,
/// <see cref="OrbatPanel.MatchesFilter"/>,
/// <see cref="OrbatPanel.HandleEntityClick"/>)
/// without requiring an active ImGui render frame.
/// </summary>
public class OrbatPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DerRepo CreateRepo() => new DerRepo();

    /// <summary>
    /// Creates an entity in the repo with an EntityInfo descriptor.
    /// </summary>
    private static IDerEntity AddEntity(
        DerRepo repo,
        int entityId,
        string name,
        int commanderId = 0,
        long tkbType = 100)
    {
        var entity = repo.CreateEntity(entityId, tkbType);
        entity.SetDescriptor(new EntityInfo
        {
            EntityId       = entityId,
            Name           = name,
            CommanderId    = commanderId,
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY
        });
        return entity;
    }

    // ── FindRootEntities ──────────────────────────────────────────────────────

    [Fact]
    public void FindRootEntities_SingleRoot_ReturnsIt()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "Alpha", commanderId: 0);

        var roots = panel.FindRootEntities(repo).ToList();

        Assert.Single(roots);
        Assert.Equal(1, roots[0].EntityId);
    }

    [Fact]
    public void FindRootEntities_SubordinatesExcluded()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "HQ",     commanderId: 0);
        AddEntity(repo, 2, "Alpha1", commanderId: 1);
        AddEntity(repo, 3, "Alpha2", commanderId: 1);

        var roots = panel.FindRootEntities(repo).ToList();

        Assert.Single(roots);
        Assert.Equal(1, roots[0].EntityId);
    }

    [Fact]
    public void FindRootEntities_MultipleRoots_AllReturned()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "TF1", commanderId: 0);
        AddEntity(repo, 2, "TF2", commanderId: 0);

        var roots = panel.FindRootEntities(repo).ToList();

        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void FindRootEntities_EntityWithoutInfoDescriptor_IsSkipped()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        // Entity without EntityInfo — should not appear as root
        repo.CreateEntity(99, 100);
        AddEntity(repo, 1, "Root", commanderId: 0);

        var roots = panel.FindRootEntities(repo).ToList();

        Assert.Single(roots);
        Assert.Equal(1, roots[0].EntityId);
    }

    // ── FindChildren ──────────────────────────────────────────────────────────

    [Fact]
    public void FindChildren_NoChildren_ReturnsEmpty()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "HQ", commanderId: 0);

        var children = panel.FindChildren(1, repo).ToList();

        Assert.Empty(children);
    }

    [Fact]
    public void FindChildren_ReturnsDirectSubordinatesOnly()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "HQ",     commanderId: 0);
        AddEntity(repo, 2, "Alpha1", commanderId: 1);
        AddEntity(repo, 3, "Alpha2", commanderId: 1);
        AddEntity(repo, 4, "Bravo",  commanderId: 2); // child of Alpha1, not HQ

        var children = panel.FindChildren(1, repo).ToList();

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.EntityId == 2);
        Assert.Contains(children, c => c.EntityId == 3);
        Assert.DoesNotContain(children, c => c.EntityId == 4);
    }

    // ── MatchesFilter ─────────────────────────────────────────────────────────

    [Fact]
    public void MatchesFilter_EmptyFilter_AlwaysTrue()
    {
        var panel = new OrbatPanel();
        Assert.True(panel.MatchesFilter("AnyName", ""));
        Assert.True(panel.MatchesFilter("AnyName", null!));
    }

    [Fact]
    public void MatchesFilter_ExactMatch_ReturnsTrue()
    {
        var panel = new OrbatPanel();
        Assert.True(panel.MatchesFilter("Tank#1", "Tank#1"));
    }

    [Fact]
    public void MatchesFilter_PartialMatch_ReturnsTrue()
    {
        var panel = new OrbatPanel();
        Assert.True(panel.MatchesFilter("Tank#1", "tank"));
    }

    [Fact]
    public void MatchesFilter_CaseInsensitive_UpperFilter_MatchesLowerName()
    {
        var panel = new OrbatPanel();
        Assert.True(panel.MatchesFilter("alpha platoon", "ALPHA"));
    }

    [Fact]
    public void MatchesFilter_CaseInsensitive_MixedCase_Matches()
    {
        var panel = new OrbatPanel();
        Assert.True(panel.MatchesFilter("T-72 Main Battle Tank", "bAtTlE"));
    }

    [Fact]
    public void MatchesFilter_NoMatch_ReturnsFalse()
    {
        var panel = new OrbatPanel();
        Assert.False(panel.MatchesFilter("Infantry Squad", "tank"));
    }

    // ── GetVisibleNodes – basic cases ─────────────────────────────────────────

    [Fact]
    public void GetVisibleNodes_EmptyRepo_ReturnsEmpty()
    {
        var panel = new OrbatPanel();
        var nodes = panel.GetVisibleNodes(CreateRepo());
        Assert.Empty(nodes);
    }

    [Fact]
    public void GetVisibleNodes_SingleRoot_ReturnsOneNode()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "HQ", commanderId: 0);

        var nodes = panel.GetVisibleNodes(repo);

        Assert.Single(nodes);
        Assert.Equal(1,  nodes[0].EntityId);
        Assert.Equal("HQ", nodes[0].Name);
        Assert.Equal(0,  nodes[0].Depth);
    }

    [Fact]
    public void GetVisibleNodes_CollapsedRoot_ChildrenNotIncluded()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel(); // node 1 is NOT expanded
        AddEntity(repo, 1, "HQ",     commanderId: 0);
        AddEntity(repo, 2, "Alpha1", commanderId: 1);

        var nodes = panel.GetVisibleNodes(repo);

        // Only the root; children hidden because root is collapsed
        Assert.Single(nodes);
        Assert.Equal(1, nodes[0].EntityId);
    }

    [Fact]
    public void GetVisibleNodes_ExpandedRoot_ChildrenIncluded()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        panel.ToggleExpanded(1); // expand root
        AddEntity(repo, 1, "HQ",     commanderId: 0);
        AddEntity(repo, 2, "Alpha1", commanderId: 1);

        var nodes = panel.GetVisibleNodes(repo);

        Assert.Equal(2, nodes.Count);
        Assert.Equal(0, nodes[0].Depth);
        Assert.Equal(1, nodes[1].Depth);
        Assert.Equal(2, nodes[1].EntityId);
    }

    [Fact]
    public void GetVisibleNodes_NodeWithChildren_HasChildrenTrue()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "HQ",     commanderId: 0);
        AddEntity(repo, 2, "Alpha1", commanderId: 1);

        var nodes = panel.GetVisibleNodes(repo);

        Assert.True(nodes[0].HasChildren);
    }

    [Fact]
    public void GetVisibleNodes_LeafNode_HasChildrenFalse()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        AddEntity(repo, 1, "Tank#1", commanderId: 0);

        var nodes = panel.GetVisibleNodes(repo);

        Assert.False(nodes[0].HasChildren);
    }

    // ── Filter integration ────────────────────────────────────────────────────

    [Fact]
    public void GetVisibleNodes_FilterActive_OnlyMatchingNodesReturned()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel { FilterText = "tank" };
        AddEntity(repo, 1, "Tank#1",   commanderId: 0);
        AddEntity(repo, 2, "Infantry", commanderId: 0);

        var nodes = panel.GetVisibleNodes(repo);

        Assert.Single(nodes);
        Assert.Equal("Tank#1", nodes[0].Name);
    }

    [Fact]
    public void GetVisibleNodes_FilterActive_CaseInsensitive()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel { FilterText = "PLATOON" };
        AddEntity(repo, 1, "platoon 1",  commanderId: 0);
        AddEntity(repo, 2, "Tank#1",     commanderId: 0);

        var nodes = panel.GetVisibleNodes(repo);

        Assert.Single(nodes);
        Assert.Equal("platoon 1", nodes[0].Name);
    }

    // ── Cycle detection ───────────────────────────────────────────────────────

    [Fact]
    public void GetVisibleNodes_CircularCommanderIds_DoesNotThrowOrStackOverflow()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        // Create a cycle: entity 1 commands entity 2, entity 2 commands entity 1
        // Entity 1 is a root (CommanderId = 0), so traversal begins at 1
        AddEntity(repo, 1, "UnitA", commanderId: 0);
        AddEntity(repo, 2, "UnitB", commanderId: 1);
        // Re-point UnitA's commander to UnitB to close the cycle
        var unitA = repo.GetEntity(1)!;
        unitA.SetDescriptor(new EntityInfo
        {
            EntityId       = 1,
            Name           = "UnitA",
            CommanderId    = 2,           // points back → cycle
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY
        });
        // UnitA now has CommanderId=2, so it is NO LONGER a root.
        // The traversal should find no roots and return empty without throwing.
        panel.ToggleExpanded(1);
        panel.ToggleExpanded(2);

        var ex = Record.Exception(() => panel.GetVisibleNodes(repo));

        Assert.Null(ex);
    }

    [Fact]
    public void GetVisibleNodes_DirectSelfLoop_DoesNotThrow()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        // EntityId == CommanderId creates a self-referential cycle.
        // The entity is a root (CommanderId == 0 check fails, so not found as root,
        // but ToggleExpanded on it makes it "expanded").
        // Create root normally, then mutate to a self-loop via expanded subtree.
        AddEntity(repo, 1, "SelfLoop", commanderId: 0);
        panel.ToggleExpanded(1);
        // Mutate so the "child" is itself
        var entity = repo.GetEntity(1)!;
        entity.SetDescriptor(new EntityInfo
        {
            EntityId       = 1,
            Name           = "SelfLoop",
            CommanderId    = 1,  // self-loop
            ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY
        });

        var ex = Record.Exception(() => panel.GetVisibleNodes(repo));

        Assert.Null(ex);
    }

    // ── Depth cap ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetVisibleNodes_DeepChain_StopsAtMaxOrbatDepth()
    {
        var repo  = CreateRepo();
        var panel = new OrbatPanel();
        int chainLength = PanelConstants.MaxOrbatDepth + 5;

        // Build a linear chain: 1 → 2 → 3 → … → chainLength
        AddEntity(repo, 1, "Root", commanderId: 0);
        for (int i = 2; i <= chainLength; i++)
        {
            AddEntity(repo, i, $"Node{i}", commanderId: i - 1);
            panel.ToggleExpanded(i - 1); // expand each parent
        }

        var ex    = Record.Exception(() => panel.GetVisibleNodes(repo));
        var nodes = panel.GetVisibleNodes(repo);

        Assert.Null(ex);
        // The result must be capped at MaxOrbatDepth levels (depth 0 … MaxOrbatDepth-1)
        Assert.True(nodes.All(n => n.Depth < PanelConstants.MaxOrbatDepth),
            "No node should have depth >= MaxOrbatDepth");
    }

    // ── HandleEntityClick ─────────────────────────────────────────────────────

    [Fact]
    public void HandleEntityClick_CallsLogicSelectEntity()
    {
        var panel = new OrbatPanel();
        var logic = new Mock<IIosLogic>();

        panel.HandleEntityClick(42, logic.Object);

        logic.Verify(l => l.SelectEntity(42), Times.Once);
    }

    [Fact]
    public void HandleEntityClick_NullLogic_Throws()
    {
        var panel = new OrbatPanel();
        Assert.Throws<ArgumentNullException>(() => panel.HandleEntityClick(1, null!));
    }
}
