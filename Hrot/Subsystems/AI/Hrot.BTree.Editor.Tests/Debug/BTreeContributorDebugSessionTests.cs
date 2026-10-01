using System;
using Fbt;
using FluentAssertions;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Debug;

/// <summary>
/// FIX2-007: BTreeAssetContributor must wire blob DebugMetadata into the attached
/// BTreeDebugSession so that RunningElementId (and stack symbolication) works at runtime.
/// Tests go through the contributor's RegisterBlob() production path; they must NOT call
/// SetDebugMetadata() directly.
/// </summary>
public sealed class BTreeContributorDebugSessionTests
{
    // ---- helpers -----------------------------------------------------------

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BTreeTraceWorkingMemory1024>();
        // ⭐⭐ O7c-② / CE-319: the cursor is a SLOT now, so a world that wants one needs
        //   BehaviorState (it carries the key's input) and the tier ladder (it holds the slot).
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
        Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
        return world;
    }

    private static BehaviorTreeBlob MakeBlob(string treeName, NodeDebugMetadata[]? metadata)
        => new()
        {
            TreeName        = treeName,
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
            DebugMetadata   = metadata,
        };

    // ---- FIX2-007: production path test ------------------------------------

    [Fact]
    public void RegisterBlob_AfterUpdate_RunningElementId_MatchesSymbolicatedVisualId()
    {
        // Arrange
        var expectedVisualId = new Guid("aaaabbbb-0000-0000-0000-000000000001");
        var session = new BTreeDebugSession();
        var contributor = new BTreeAssetContributor(session);

        var metadata = new NodeDebugMetadata[]
        {
            new() { VisualId = expectedVisualId.ToString("D") },
        };
        var blob = MakeBlob("FIX2007TestTree", metadata);

        // Act -- call the contributor's register method (NOT SetDebugMetadata directly)
        contributor.RegisterBlob(blob, "FIX2007TestTree");

        // Set up ECS: entity with RunningNodeIndex = 0 (which should map to expectedVisualId)
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState { ActiveBehaviorHash = 4242, BrainTier = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree });
        Fdp.Toolkit.Behavior.RootStateAccess.EnsureRootState(world, entity);
        Fdp.Toolkit.Behavior.RootStateAccess.SetState(world, entity, new Fbt.BehaviorTreeState { RunningNodeIndex = 0 });

        session.Update(world, entity);

        // Assert
        var snap = session.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();
        snap!.RunningElementId.Should().Be(expectedVisualId,
            because: "RegisterBlob must wire DebugMetadata into the session so " +
                     "Update() can symbolicate the running node index");
    }

    [Fact]
    public void RegisterBlob_WithoutSession_DoesNotThrow()
    {
        // Contributor constructed without a session should project the asset without crashing.
        var contributor = new BTreeAssetContributor();
        var blob = MakeBlob("NoSessionTree", null);

        var act = () => contributor.RegisterBlob(blob, "NoSessionTree");

        act.Should().NotThrow();
        contributor.Enumerate().Should().HaveCount(1);
    }

    [Fact]
    public void RegisterBlob_WithNullMetadata_SessionSymbolicationIsCleared()
    {
        // If the blob has no debug metadata, SetDebugMetadata(null, ...) is called,
        // which clears any previously set metadata on the session.
        var session = new BTreeDebugSession();
        var contributor = new BTreeAssetContributor(session);

        // First register a blob WITH metadata.
        var firstId = new Guid("aaaabbbb-0000-0000-0000-000000000001");
        contributor.RegisterBlob(
            MakeBlob("Tree1", new[] { new NodeDebugMetadata { VisualId = firstId.ToString("D") } }),
            "Tree1");
        session.TrySymbolicateIndex(0).Should().Be(firstId);

        // Then register a blob WITHOUT metadata -- the contributor calls SetDebugMetadata(null, ...).
        contributor.RegisterBlob(MakeBlob("Tree2", null), "Tree2");

        session.TrySymbolicateIndex(0).Should().BeNull(
            because: "null DebugMetadata passed to SetDebugMetadata clears symbolication");
    }

    [Fact]
    public void RegisterBlob_TwiceWithDifferentMetadata_SessionUsesLatest()
    {
        var id1 = new Guid("11111111-0000-0000-0000-000000000001");
        var id2 = new Guid("22222222-0000-0000-0000-000000000002");
        var session = new BTreeDebugSession();
        var contributor = new BTreeAssetContributor(session);

        contributor.RegisterBlob(
            MakeBlob("TreeA", new[] { new NodeDebugMetadata { VisualId = id1.ToString("D") } }),
            "TreeA");

        contributor.RegisterBlob(
            MakeBlob("TreeB", new[] { new NodeDebugMetadata { VisualId = id2.ToString("D") } }),
            "TreeB");

        // Session should reflect the latest SetDebugMetadata call.
        session.TrySymbolicateIndex(0).Should().Be(id2);
    }

    /// <summary>
    /// ⭐ <c>CE-476</c> — an entity's nodes are named from the tree its interpreter RUNS (the behaviour registry — the same
    /// lookup the inspector's tree view uses), not from whichever tree the catalogue registered last.
    /// 🔴 Measured live on a headless cluster: the catalogue registers every compiled tree through the single slot, so the
    /// C# hill-attack commander traced with <c>activeNode</c> null and every <c>nodeVisualId</c> <c>Guid.Empty</c>.
    /// 📄 docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md §4.
    /// </summary>
    [Fact]
    public void AnEntityIsNamedFromTheTreeItRuns_NotTheLastOneRegistered()
    {
        var running = new Guid("11111111-0000-0000-0000-000000000001");
        var other   = new Guid("22222222-0000-0000-0000-000000000002");

        var runningBlob = MakeBlob("TreeA", new[] { new NodeDebugMetadata { VisualId = running.ToString("D") } });
        runningBlob.Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } };
        runningBlob.MethodNames = new[] { "Run" };
        var actions = new Fbt.Runtime.ActionRegistry<byte, Fdp.Toolkit.Behavior.BTreeContext>();
        actions.Register("Run", (ref byte _, ref BehaviorTreeState _, ref Fdp.Toolkit.Behavior.BTreeContext _, int _) => NodeStatus.Running);
        var behaviors = new Fdp.Toolkit.Behavior.BehaviorRegistry();
        behaviors.Register("TreeA", new Fdp.Toolkit.Behavior.BehaviorDefinition
        {
            Name             = "TreeA",
            BrainTier        = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Fbt.Runtime.Interpreter<byte, Fdp.Toolkit.Behavior.BTreeContext>(runningBlob, actions),
        });

        var session = new BTreeDebugSession(coordinator: null, behaviors);
        // The catalogue's last registration is ANOTHER tree — the single slot now names TreeB's nodes.
        new BTreeAssetContributor(session).RegisterBlob(
            MakeBlob("TreeB", new[] { new NodeDebugMetadata { VisualId = other.ToString("D") } }), "TreeB");

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState
        {
            ActiveBehaviorHash = Fdp.Toolkit.Behavior.BehaviorHash.FromName("TreeA"),
            BrainTier          = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree,
        });
        Fdp.Toolkit.Behavior.RootStateAccess.EnsureRootState(world, entity);
        Fdp.Toolkit.Behavior.RootStateAccess.SetState(world, entity, new Fbt.BehaviorTreeState { RunningNodeIndex = 0 });

        session.Update(world, entity);

        var snap = session.GetCurrentStateSnapshot();
        snap.Should().NotBeNull();
        snap!.RunningElementId.Should().Be(running, because: "the entity runs TreeA; TreeB was merely registered last");
    }
}
