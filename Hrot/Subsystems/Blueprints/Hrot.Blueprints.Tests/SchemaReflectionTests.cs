using System.Reflection;
using System.Text.Json.Serialization;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Guard 1 of the blueprint node-kind safety net (see NodeCoverageTests.cs for Guards 2-3):
/// discriminator coverage + round-trip, fully reflection-driven off
/// <c>typeof(Node).GetCustomAttributes&lt;JsonDerivedTypeAttribute&gt;()</c> and the set of
/// concrete <see cref="Node"/> subtypes in this assembly -- NOT a hand-maintained list. Adding a
/// new node kind with no <c>[JsonDerivedType]</c> attribute (or a kind that fails to round-trip
/// through the real <see cref="BlueprintJsonServices"/> serializer options) fails this suite
/// automatically; there is no magic count to bump.
/// </summary>
public sealed class SchemaReflectionTests
{
    // ── reflection helpers ────────────────────────────────────────────────

    private static IReadOnlyList<Type> ConcreteNodeSubtypes() =>
        typeof(Node).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Node)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<JsonDerivedTypeAttribute> NodeDiscriminators() =>
        typeof(Node).GetCustomAttributes<JsonDerivedTypeAttribute>().ToList();

    public static IEnumerable<object[]> ConcreteNodeSubtypeTheoryData() =>
        ConcreteNodeSubtypes().Select(t => new object[] { t });

    // ── Guard 1a: every concrete subtype has exactly one discriminator ────
    // Replaces the old brittle magic-number ConcreteNodeSubtypeCount_Is30: a new node kind no
    // longer needs a count bump, but it MUST have exactly one [JsonDerivedType] on Node.

    [Fact]
    public void AllConcreteNodeSubtypes_HaveExactlyOneDiscriminator()
    {
        var countByType = NodeDiscriminators()
            .GroupBy(a => a.DerivedType)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var subtype in ConcreteNodeSubtypes())
        {
            countByType.TryGetValue(subtype, out int count);
            Assert.True(count == 1,
                $"{subtype.Name} must have exactly one [JsonDerivedType(typeof({subtype.Name}), \"...\")] " +
                $"on the Node class attribute list, but found {count}.");
        }
    }

    // ── Guard 1b: no discriminator points at a missing/abstract/non-Node type ──

    [Fact]
    public void AllDiscriminators_PointAtConcreteNodeSubtypes()
    {
        foreach (var attr in NodeDiscriminators())
        {
            Assert.True(attr.DerivedType.IsSubclassOf(typeof(Node)),
                $"[JsonDerivedType] \"{attr.TypeDiscriminator}\" points at " +
                $"'{attr.DerivedType.Name}', which is not a subclass of Node.");
            Assert.False(attr.DerivedType.IsAbstract,
                $"[JsonDerivedType] \"{attr.TypeDiscriminator}\" points at abstract type " +
                $"'{attr.DerivedType.Name}'.");
        }
    }

    // ── Guard 1c: discriminator strings are unique ─────────────────────────

    [Fact]
    public void AllDiscriminatorStrings_AreUnique()
    {
        var duplicates = NodeDiscriminators()
            .GroupBy(a => (string)a.TypeDiscriminator!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Duplicate [JsonDerivedType] discriminator string(s): " + string.Join(", ", duplicates));
    }

    // ── Guard 1d: reflection-driven round-trip theory (replaces the hand [InlineData] list) ──

    [Theory]
    [MemberData(nameof(ConcreteNodeSubtypeTheoryData))]
    public void DiscriminatorRoundTrip_EachNodeKind(Type nodeType)
    {
        var discriminators = NodeDiscriminators().Where(a => a.DerivedType == nodeType).ToList();
        Assert.True(discriminators.Count == 1,
            $"{nodeType.Name}: expected exactly one [JsonDerivedType], found {discriminators.Count} " +
            "(see AllConcreteNodeSubtypes_HaveExactlyOneDiscriminator).");
        string expectedDiscriminator = (string)discriminators[0].TypeDiscriminator!;

        var node = (Node)Activator.CreateInstance(nodeType)!;
        node.Id = Guid.NewGuid();

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "Test",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = Guid.NewGuid(),
                    Name  = "Main",
                    Kind  = GraphKind.Function,
                    Nodes = [node],
                },
            ],
        };

        // Uses the REAL blueprint serializer options (BlueprintJsonServices), the same ones
        // production .bp.json load/save goes through -- not a fresh JsonSerializerOptions.
        var json = BlueprintJsonServices.Serialize(asset);
        Assert.Contains($"\"kind\":\"{expectedDiscriminator}\"", json);

        var deserialized = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.IsType(nodeType, deserialized.Graphs[0].Nodes[0]);
    }

    [Fact]
    public void UnknownFieldsTolerance_DoesNotThrow()
    {
        const string json = """
            {
                "Name":"Test",
                "Dispatch":"Library",
                "AssetId":"00000000-0000-0000-0000-000000000001",
                "unknownField":"ignored",
                "Graphs":[]
            }
            """;

        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);
        Assert.Equal("Test", asset.Name);
        Assert.Equal(BlueprintDispatchKind.Library, asset.Dispatch);
    }

    [Fact]
    public void MissingFieldsDefaultToEmpty()
    {
        const string json = """
            {
                "Name":"Y",
                "Dispatch":"Instance",
                "AssetId":"00000000-0000-0000-0000-000000000002"
            }
            """;

        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);
        Assert.NotNull(asset.Variables);
        Assert.Empty(asset.Variables);
        Assert.NotNull(asset.Graphs);
        Assert.Empty(asset.Graphs);
        Assert.NotNull(asset.EventDispatchers);
        Assert.Empty(asset.EventDispatchers);
    }

    [Fact]
    public void EqsSensorHandle_IsPermittedVariableType()
    {
        var typeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" };
        bool resolved = StaticTypeRegistry.Instance.TryResolve(typeRef, out var irType);

        Assert.True(resolved);
        Assert.Equal("FDP.Eqs.EqsSensorHandle", irType.FullName);
        Assert.True(irType.IsUnmanaged);
        Assert.Equal(8, irType.SizeBytes);
    }
}
