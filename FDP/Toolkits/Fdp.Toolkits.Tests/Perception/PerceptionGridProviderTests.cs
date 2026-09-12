using Fdp.Toolkit.Perception.Modules;
using Xunit;

namespace Fdp.Toolkits.Tests.Perception;

/// <summary>
/// <b><c>B3</c> — the perception grid is a RESOURCE, owned once, borrowed by the capabilities that need it.</b>
///
/// <para>Before B3, <c>CognitiveSpatialModule</c> and <c>AutonomousPerceptionModule</c> each allocated their
/// own <c>SpatialHashGrid</c> with <c>Allocator.Persistent</c> in their constructors. That fused a capability
/// with a resource, and it is the blocker for role-based composition: a node's capability set is the union of
/// its roles, so selecting both through two roles allocated the grid <b>twice</b> — persistent native memory,
/// not a wasted tick.</para>
///
/// <para>⚠ The cross-capability sharing test lives in <c>Hrot.SimHost.Tests</c>
/// (<c>PerceptionGridSharingTests</c>), because <c>CognitiveSpatialModule</c> is in <c>Hrot.SimHost</c> and
/// this assembly cannot see it. These are the provider's own rails.</para>
/// </summary>
public sealed class PerceptionGridProviderTests
{
    [Fact]
    public void TheProviderAllocatesAGridAndFreesItExactlyOnce()
    {
        var provider = new PerceptionGridProvider();
        Assert.True(provider.Grid.GridHead.IsCreated);

        provider.Dispose();
        provider.Dispose();   // idempotent — a double free would corrupt the allocator

        // ⚠ Deliberately NOT asserting IsCreated is false afterwards. SpatialHashGrid is a STRUCT and
        // Grid is a property, so every read hands back a COPY holding the same native pointers; freeing
        // the provider's copy cannot clear this copy's flag. That is the same reason PhysicsToolkitModule
        // retains its own copy to dispose. "Was it freed?" is not observable from here — what IS testable
        // is that Dispose is idempotent, which is what the second call above checks.
    }

    /// <summary>
    /// A capability handed a provider must NOT free it: the provider outlives the capability and other
    /// capabilities still read the memory. This is the half that would corrupt rather than merely leak.
    /// </summary>
    [Fact]
    public void ACapabilityThatBorrowsAProviderDoesNotFreeIt()
    {
        var provider = new PerceptionGridProvider();

        var borrower = new AutonomousPerceptionModule(gridProvider: provider);
        borrower.Dispose();

        // The provider can still be disposed exactly once, by its owner, without the allocator objecting.
        // (Struct-copy semantics — see above — mean the stronger "the memory is still live" cannot be
        // asserted from a copy; the structural guarantee is that _ownedGridProvider is null when borrowing,
        // so the borrower's Dispose has nothing to free.)
        provider.Dispose();
    }

    /// <summary>
    /// The <c>null</c> default keeps pre-B3 hosts and tests working: the capability allocates and owns a
    /// private grid, and frees it. Kept deliberately — but a composition root selecting by role must pass
    /// the shared provider instead.
    /// </summary>
    [Fact]
    public void ACapabilityWithNoProviderStillOwnsAndFreesItsOwnGrid()
    {
        var module = new AutonomousPerceptionModule();
        module.Dispose();
        module.Dispose();   // idempotent
    }
}
