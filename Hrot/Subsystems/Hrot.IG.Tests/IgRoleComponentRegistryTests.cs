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

    /// <summary>⭐ CE-1139 — the IG carries the body stance (replicated in), so it can draw a crouched or prone soldier.</summary>
    [Fact]
    public void CE1139_TheIg_RegistersTheStanceComponents()
    {
        using var world = new EntityRepository();
        IgRoleComponentRegistry.RegisterAll(world);
        Assert.True(world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>());
        Assert.True(world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceStatus>());
    }
}
