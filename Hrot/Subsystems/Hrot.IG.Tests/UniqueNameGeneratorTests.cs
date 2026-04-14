using System;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Fdp.Core;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="UniqueNameGenerator"/>.
/// </summary>
public class UniqueNameGeneratorTests
{
    // ── GetMaxIndex ────────────────────────────────────────────────────────────

    [Fact]
    public void GetMaxIndex_EmptyWorld_ReturnsZero()
    {
        using var world = new EntityRepository();

        int result = UniqueNameGenerator.GetMaxIndex(world, "Tank-");

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetMaxIndex_SingleMatchingEntity_ReturnsItsIndex()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>();
        var e = world.CreateEntity();
        world.SetComponent(e, new EntityInfo { Name = new FixedString64("Tank-3") });

        int result = UniqueNameGenerator.GetMaxIndex(world, "Tank-");

        Assert.Equal(3, result);
    }

    [Fact]
    public void GetMaxIndex_MultipleMatchingEntities_ReturnsMaxIndex()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>();

        foreach (var name in new[] { "Tank-1", "Tank-5", "Tank-2" })
        {
            var e = world.CreateEntity();
            world.SetComponent(e, new EntityInfo { Name = new FixedString64(name) });
        }

        int result = UniqueNameGenerator.GetMaxIndex(world, "Tank-");

        Assert.Equal(5, result);
    }

    [Fact]
    public void GetMaxIndex_MixedEntities_IgnoresNonMatchingPrefix()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>();

        var e1 = world.CreateEntity();
        world.SetComponent(e1, new EntityInfo { Name = new FixedString64("Tank-3") });

        var e2 = world.CreateEntity();
        world.SetComponent(e2, new EntityInfo { Name = new FixedString64("Truck-9") }); // different prefix

        int result = UniqueNameGenerator.GetMaxIndex(world, "Tank-");

        Assert.Equal(3, result);  // Truck-9 must not influence Tank- index
    }

    [Fact]
    public void GetMaxIndex_IsCaseInsensitive()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>();
        var e = world.CreateEntity();
        world.SetComponent(e, new EntityInfo { Name = new FixedString64("TANK-7") });

        int result = UniqueNameGenerator.GetMaxIndex(world, "tank-");

        Assert.Equal(7, result);
    }

    [Fact]
    public void GetMaxIndex_EntityWithNonNumericSuffix_IsIgnored()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>();
        var e1 = world.CreateEntity();
        world.SetComponent(e1, new EntityInfo { Name = new FixedString64("Tank-Alpha") }); // non-numeric suffix
        var e2 = world.CreateEntity();
        world.SetComponent(e2, new EntityInfo { Name = new FixedString64("Tank-2") });

        int result = UniqueNameGenerator.GetMaxIndex(world, "Tank-");

        Assert.Equal(2, result);  // Tank-Alpha is skipped; only Tank-2 counts
    }

    [Fact]
    public void GetMaxIndex_NullPrefix_ReturnsZero()
    {
        using var world = new EntityRepository();

        int result = UniqueNameGenerator.GetMaxIndex(world, string.Empty);

        Assert.Equal(0, result);  // empty prefix guard
    }

    // ── CreateSessionGenerator ─────────────────────────────────────────────────

    [Fact]
    public void CreateSessionGenerator_FirstCall_ReturnsPrefixPlusBaseIndexPlusOne()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>();
        var e = world.CreateEntity();
        world.SetComponent(e, new EntityInfo { Name = new FixedString64("Unit-4") });

        var gen = UniqueNameGenerator.CreateSessionGenerator(world, "Unit-");

        Assert.Equal("Unit-5", gen());  // base=4 → first call returns 4+1=5
    }

    [Fact]
    public void CreateSessionGenerator_MultipleCallsIncrement()
    {
        using var world = new EntityRepository();
        var gen = UniqueNameGenerator.CreateSessionGenerator(world, "Helo-");

        // No existing entities → base=0 → calls return 1, 2, 3
        Assert.Equal("Helo-1", gen());
        Assert.Equal("Helo-2", gen());
        Assert.Equal("Helo-3", gen());
    }

    [Fact]
    public void CreateSessionGenerator_MultipleGeneratorsAreIndependent()
    {
        using var world = new EntityRepository();

        var gen1 = UniqueNameGenerator.CreateSessionGenerator(world, "Tank-");
        var gen2 = UniqueNameGenerator.CreateSessionGenerator(world, "Tank-");

        // Each generator has its own counter; both start from the same base.
        Assert.Equal("Tank-1", gen1());
        Assert.Equal("Tank-1", gen2());  // independent counter
        Assert.Equal("Tank-2", gen1());  // gen1 advances separately
    }

    // ── ArgumentNullException guard ────────────────────────────────────────────

    [Fact]
    public void GetMaxIndex_NullWorld_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UniqueNameGenerator.GetMaxIndex(null!, "Tank-"));
    }
}
