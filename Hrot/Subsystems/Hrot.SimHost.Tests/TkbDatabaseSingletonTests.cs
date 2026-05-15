using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies TKB-015: <see cref="ITkbDatabase"/> can be registered and retrieved as an
/// ECS world singleton via <see cref="EntityRepository.SetSingletonManaged{T}"/>.
/// </summary>
public class TkbDatabaseSingletonTests
{
    [Fact]
    public void SetSingletonManaged_TkbDatabase_CanBeRetrievedByInterface()
    {
        using var world = new EntityRepository();
        var tkb = new TkbDatabase();
        world.SetSingletonManaged<ITkbDatabase>(tkb);

        var retrieved = world.GetSingletonManaged<ITkbDatabase>();
        Assert.Same(tkb, retrieved);
    }

    [Fact]
    public void SetSingletonManaged_TkbDatabase_SameInstanceAfterRegisterAll()
    {
        using var world = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(world);
        var tkb = new TkbDatabase();
        world.SetSingletonManaged<ITkbDatabase>(tkb);

        var retrieved = world.GetSingletonManaged<ITkbDatabase>();
        Assert.Same(tkb, retrieved);
    }
}
