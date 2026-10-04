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
/// <para>⚠ The borrower rails (a capability handed a provider never frees it; one with none owns its own) live in
/// <c>Hrot.SimHost.Tests</c> (<c>PerceptionGridSharingTests</c>): the only grid capability left is <c>EqsModule</c>,
/// in <c>Hrot.SimHost</c> (<c>CE-3052</c> retired the toolkit's <c>AutonomousPerceptionModule</c>). This is the
/// provider's own rail.</para>
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
}
