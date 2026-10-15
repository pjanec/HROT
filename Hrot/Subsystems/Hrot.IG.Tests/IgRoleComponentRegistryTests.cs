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

    /// <summary>
    /// ⭐ CE-4008 — IG runs the shared EntityInfo ingress translator, which publishes the command-hierarchy events.
    /// 📐 Measured live: unregistered, a late Player entering a replay died on CmdAssignSubordinate (Strict Mode).
    /// </summary>
    [Fact]
    public void RegisterAll_RegistersTheHierarchySchemaTheSharedIngressAndUnitHierarchySystemUse()
    {
        using var world = new EntityRepository();
        IgRoleComponentRegistry.RegisterAll(world);

        Assert.True(world.Bus.IsRegistered<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>());
        Assert.True(world.Bus.IsRegistered<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>());
        // ⭐ …and the components UnitHierarchySystem (which IG runs) adds — measured live, the second crash.
        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Core.CommandHierarchy.UnitSubordinate>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Core.CommandHierarchy.UnitRoster>()));
    }
}
