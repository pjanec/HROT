using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.IG.Tests;

public class IgRoleComponentRegistryTests
{
    [Fact]
    public void RegisterAll_DoesNotThrow()
    {
        using var world = new EntityRepository();
        var ex = Record.Exception(() => IgRoleComponentRegistry.RegisterAll(world));
        Assert.Null(ex);
    }

    [Fact]
    public void RegisterAll_RegistersCoreIgComponents()
    {
        using var world = new EntityRepository();
        IgRoleComponentRegistry.RegisterAll(world);

        Assert.Null(Record.Exception(() => world.GetComponentTable<ResolvedStyle>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<CullingState>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<VisualEffectState>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<PhysicsCollider>()));
    }
}
