using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.Player.Tests;

public class PlayerRoleComponentRegistryTests
{
    [Fact]
    public void RegisterAll_DoesNotThrow()
    {
        using var world = new EntityRepository();
        var ex = Record.Exception(() => PlayerRoleComponentRegistry.RegisterAll(world));
        Assert.Null(ex);
    }

    [Fact]
    public void RegisterAll_RegistersCorePlayerComponents()
    {
        using var world = new EntityRepository();
        PlayerRoleComponentRegistry.RegisterAll(world);

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
        PlayerRoleComponentRegistry.RegisterAll(world);

        Assert.True(world.Bus.IsRegistered<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>());
        Assert.True(world.Bus.IsRegistered<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>());
        // ⭐ …and the components UnitHierarchySystem (which IG runs) adds — measured live, the second crash.
        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Core.CommandHierarchy.UnitSubordinate>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Core.CommandHierarchy.UnitRoster>()));
    }

    /// <summary>⭐ CE-1139 — the IG carries the body stance (replicated in), so it can draw a crouched or prone soldier.</summary>
    [Fact]
    public void CE1139_ThePlayer_RegistersTheStanceComponents()
    {
        using var world = new EntityRepository();
        PlayerRoleComponentRegistry.RegisterAll(world);
        Assert.True(world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>());
        Assert.True(world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceStatus>());
    }
}
