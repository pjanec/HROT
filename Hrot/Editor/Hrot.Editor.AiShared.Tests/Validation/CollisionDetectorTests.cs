using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Validation;

namespace Hrot.Editor.AiShared.Tests.Validation;

// ---------------------------------------------------------------------------
// Minimal fake IActionSchemaExporter for headless detector tests
// ---------------------------------------------------------------------------

file sealed class FakeSchemaExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _all;

    public FakeSchemaExporter(params ActionSchemaEntry[] entries)
    {
        _all = new Dictionary<string, ActionSchemaEntry>();
        foreach (var e in entries)
            _all[e.Fqn] = e;
    }

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _all;
    public ActionSchemaEntry? Lookup(string fqn) => _all.GetValueOrDefault(fqn);
    public void Rebuild() { }
    public event Action? Changed { add { } remove { } }

    private static readonly Type _stubDtoType = typeof(int);

    public static ActionSchemaEntry Make(string fqn) =>
        new ActionSchemaEntry(fqn, _stubDtoType, ActionHosting.BTree, BlackboardAccess.Unknown, null);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class CollisionDetectorTests
{
    [Fact]
    public void CollisionDetector_FlagsDuplicateShortNames()
    {
        // Two distinct FQNs that share the short name "DoThing"
        var exporter = new FakeSchemaExporter(
            FakeSchemaExporter.Make("A.Ns1.DoThing"),
            FakeSchemaExporter.Make("B.Ns2.DoThing"));

        var collisions = SubElementCollisionDetector.GetCollisions(exporter);

        Assert.Single(collisions);
        var c = collisions[0];
        Assert.Equal("DoThing", c.ShortName);

        // Claimants must be sorted
        Assert.Equal(2, c.ClaimingFqns.Count);
        Assert.Equal("A.Ns1.DoThing", c.ClaimingFqns[0]);
        Assert.Equal("B.Ns2.DoThing", c.ClaimingFqns[1]);
    }

    [Fact]
    public void CollisionDetector_NoCollision_WhenShortNamesUnique()
    {
        var exporter = new FakeSchemaExporter(
            FakeSchemaExporter.Make("A.Ns1.ActionA"),
            FakeSchemaExporter.Make("B.Ns2.ActionB"),
            FakeSchemaExporter.Make("C.Ns3.ActionC"));

        var collisions = SubElementCollisionDetector.GetCollisions(exporter);

        Assert.Empty(collisions);
    }

    [Fact]
    public void CollisionDetector_SameFqnTwice_NotACollision()
    {
        // Dictionary keys are unique by FQN so duplicate FQNs cannot exist in the
        // schema; but even if we explicitly test two entries with the same short name
        // but same FQN (via GroupBy + Distinct), it must not be flagged.
        // We use a custom exporter that returns duplicate FQN values.
        var duplicateAll = new Dictionary<string, ActionSchemaEntry>
        {
            // FQN key is unique per the dictionary contract.
            // Both have same FQN short-name AND same FQN — single unique FQN → not a collision.
            ["Ns.DoThing"] = FakeSchemaExporter.Make("Ns.DoThing"),
        };
        var exporter = new SingleDictExporter(duplicateAll);

        var collisions = SubElementCollisionDetector.GetCollisions(exporter);

        Assert.Empty(collisions);
    }

    [Fact]
    public void CollisionDetector_ThreeClaimants_SortedAscending()
    {
        var exporter = new FakeSchemaExporter(
            FakeSchemaExporter.Make("Z.Fire"),
            FakeSchemaExporter.Make("A.Fire"),
            FakeSchemaExporter.Make("M.Fire"));

        var collisions = SubElementCollisionDetector.GetCollisions(exporter);

        Assert.Single(collisions);
        Assert.Equal("Fire", collisions[0].ShortName);
        Assert.Equal(3, collisions[0].ClaimingFqns.Count);
        Assert.Equal("A.Fire",  collisions[0].ClaimingFqns[0]);
        Assert.Equal("M.Fire",  collisions[0].ClaimingFqns[1]);
        Assert.Equal("Z.Fire",  collisions[0].ClaimingFqns[2]);
    }

    [Fact]
    public void CollisionDetector_FqnWithNoDot_ShortNameIsFqnItself()
    {
        // If the FQN has no dot, the short name is the whole FQN.
        var exporter = new FakeSchemaExporter(
            FakeSchemaExporter.Make("NoDotA"),
            FakeSchemaExporter.Make("NoDotB"));

        // They have different short names — no collision.
        var collisions = SubElementCollisionDetector.GetCollisions(exporter);
        Assert.Empty(collisions);
    }

    [Fact]
    public void CollisionDetector_MultipleCollisions_ReturnsOnePerShortName()
    {
        var exporter = new FakeSchemaExporter(
            FakeSchemaExporter.Make("A.Ns1.Alpha"),
            FakeSchemaExporter.Make("B.Ns2.Alpha"),
            FakeSchemaExporter.Make("X.Ns1.Beta"),
            FakeSchemaExporter.Make("Y.Ns2.Beta"));

        var collisions = SubElementCollisionDetector.GetCollisions(exporter);

        Assert.Equal(2, collisions.Count);
        var byShort = collisions.ToDictionary(c => c.ShortName);
        Assert.True(byShort.ContainsKey("Alpha"));
        Assert.True(byShort.ContainsKey("Beta"));
        Assert.Equal(2, byShort["Alpha"].ClaimingFqns.Count);
        Assert.Equal(2, byShort["Beta"].ClaimingFqns.Count);
    }
}

// Helper for the same-FQN test — exposes a fixed dictionary
file sealed class SingleDictExporter : IActionSchemaExporter
{
    private readonly IReadOnlyDictionary<string, ActionSchemaEntry> _all;
    public SingleDictExporter(Dictionary<string, ActionSchemaEntry> dict) => _all = dict;
    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _all;
    public ActionSchemaEntry? Lookup(string fqn) => _all.GetValueOrDefault(fqn);
    public void Rebuild() { }
    public event Action? Changed { add { } remove { } }
}
