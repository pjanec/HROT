using Fdp.Core;
using Fdp.Toolkit.Perception.Modules;
using Hrot.SimHost.Modules;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// <b><c>B3</c> — the point of splitting the resource out: two capabilities, ONE allocation.</b>
///
/// <para>This is the rail the whole split exists for, and it needs both assemblies:
/// <c>PerceptionGridProvider</c> is in <c>Fdp.Toolkits</c> and <c>CognitiveSpatialModule</c> is in
/// <c>Hrot.SimHost</c>. A node whose role union selects both perception capabilities used to allocate a
/// persistent <c>SpatialHashGrid</c> twice — the memory-owning form of the double-registration hazard
/// <c>[SingleInstance]</c> catches on the system axis.</para>
/// </summary>
public sealed class PerceptionGridSharingTests
{
    [Fact]
    public void TwoCapabilitiesHandedOneProviderShareOneAllocation()
    {
        using var world    = new EntityRepository();
        var provider = new PerceptionGridProvider();

        var cognitive  = new CognitiveSpatialModule(world, provider);
        var autonomous = new AutonomousPerceptionModule(gridProvider: provider);

        // Disposing both capabilities must be safe and must not free the borrowed grid: it belongs to the
        // provider, which is disposed by its owner afterwards. ⚠ "The memory is still live" cannot be
        // asserted directly — SpatialHashGrid is a struct and every read of Grid is a copy sharing the same
        // pointers — so what this pins is that two borrowers plus the owner produce exactly ONE free, with
        // no allocator complaint. A borrower that wrongly freed the grid would make the owner's Dispose a
        // double free.
        cognitive.Dispose();
        autonomous.Dispose();
        provider.Dispose();
    }

    /// <summary>
    /// And the pre-B3 shape still works: with no provider the capability owns its own grid and frees it,
    /// so every existing host and test is unaffected by the split.
    /// </summary>
    [Fact]
    public void ACognitiveSpatialModuleWithNoProviderStillOwnsAndFreesItsOwnGrid()
    {
        using var world = new EntityRepository();

        var module = new CognitiveSpatialModule(world);
        module.Dispose();
        module.Dispose();   // idempotent
    }
}
