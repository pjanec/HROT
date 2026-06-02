using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Debug;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiShared.Tests.Debug;

/// <summary>
/// AIE-030: Verify that DebugSessionRegistry.RegisterSessionFactory allows
/// BTreeDebugSession and HsmDebugSession to be acquired, and that
/// BTreeAssetContributor wires NodeDebugMetadata into the session for
/// node-index → VisualId symbolication.
/// </summary>
public sealed class Aie030DebugSessionRegistryIntegrationTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static DebugSessionRegistry MakeRegistry(
        out BTreeDebugSession btreeSession,
        out Hrot.Hsm.Editor.Debug.HsmDebugSession hsmSession)
    {
        var coordinator = new AiTracerCoordinator();
        btreeSession = new BTreeDebugSession(coordinator);
        hsmSession   = new Hrot.Hsm.Editor.Debug.HsmDebugSession(coordinator);

        var registry = new DebugSessionRegistry();
        var capturedBTree = btreeSession;
        var capturedHsm   = hsmSession;
        registry.RegisterSessionFactory<BTreeDebugSession>(() => capturedBTree);
        registry.RegisterSessionFactory<Hrot.Hsm.Editor.Debug.HsmDebugSession>(() => capturedHsm);
        return registry;
    }

    // ── AIE-030 test 1: BTree session ──────────────────────────────────────────

    [Fact]
    public void DebugRegistry_AcquireBTreeSession_ReturnsSession()
    {
        var registry = MakeRegistry(out var expectedSession, out _);

        bool acquired = registry.TryAcquireSession<BTreeDebugSession>(out var session);

        Assert.True(acquired);
        Assert.NotNull(session);
        // The returned session must be the same instance the factory provided.
        Assert.Same(expectedSession, session);
        Assert.Same(session, registry.ActiveSession);
    }

    // ── AIE-030 test 2: HSM session ────────────────────────────────────────────

    [Fact]
    public void DebugRegistry_AcquireHsmSession_ReturnsSession()
    {
        var registry = MakeRegistry(out _, out var expectedSession);

        bool acquired = registry.TryAcquireSession<Hrot.Hsm.Editor.Debug.HsmDebugSession>(
            out var session);

        Assert.True(acquired);
        Assert.NotNull(session);
        Assert.Same(expectedSession, session);
        Assert.Same(session, registry.ActiveSession);
    }

    // ── AIE-030 test 3: contributor wires debug metadata → symbolication works ─
    // Proven via the ECS Update path (the production call chain used at runtime).
    // TrySymbolicateIndex is internal to BTree.Editor; the round-trip through
    // Update() + GetCurrentStateSnapshot() is the public API surface.

    [Fact]
    public void Contributor_WiresDebugMetadata_IntoSession()
    {
        // Arrange: contributor with metadata blob + ECS world with RunningNodeIndex = 0.
        var expectedVisualId = new Guid("aabbccdd-0000-0000-0000-000000000030");
        var session     = new BTreeDebugSession();
        var contributor = new BTreeAssetContributor(session);

        var metadata = new NodeDebugMetadata[]
        {
            new() { VisualId = expectedVisualId.ToString("D") }, // index 0
        };

        var blob = new BehaviorTreeBlob
        {
            TreeName        = "Aie030TestTree",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
            DebugMetadata   = metadata,
        };

        // Act: RegisterBlob wires metadata into the session.
        contributor.RegisterBlob(blob, "Aie030TestTree");

        // Now drive Update() so the session symbolication is exercised end-to-end.
        var world  = new EntityRepository();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BTreeTraceWorkingMemory1024>();
        var entity = world.CreateEntity();
        var brain  = new BrainBTreeState();
        brain.State.RunningNodeIndex = 0;
        world.AddComponent(entity, brain);

        session.Update(world, entity);

        // Assert: RunningElementId must equal the VisualId for node index 0.
        var snap = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap);
        Assert.Equal(expectedVisualId, snap!.RunningElementId);
    }
}
