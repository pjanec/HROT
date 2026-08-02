using System.Text.Json;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-07a — proves <see cref="ComponentFieldDecl"/>'s byte-stability discipline: a pre-CA-07a
/// scalar decl (just <c>Name</c>+<c>TypeId</c>) still serializes to EXACTLY
/// <c>{"Name":...,"TypeId":...}</c> -- the new <c>IsCollection</c>/<c>ElementTypeId</c>/
/// <c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c> members must stay entirely absent from JSON in
/// their default state (see the <c>JsonIgnore(WhenWritingDefault/WhenWritingNull)</c> attributes on
/// those members) -- and that a collection decl round-trips its new fields faithfully.
/// </summary>
public sealed class ComponentFieldDeclJsonTests
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    [Fact]
    public void ScalarDecl_SerializesToExactlyNameAndTypeId_ByteIdentical()
    {
        var decl = new ComponentFieldDecl { Name = "Health", TypeId = "System.Int32" };

        var json = JsonSerializer.Serialize(decl, Options);

        Assert.Equal("""{"Name":"Health","TypeId":"System.Int32"}""", json);
    }

    [Fact]
    public void ScalarDecl_RoundTrips()
    {
        var decl = new ComponentFieldDecl { Name = "Health", TypeId = "System.Int32" };

        var json = JsonSerializer.Serialize(decl, Options);
        var back = JsonSerializer.Deserialize<ComponentFieldDecl>(json, Options)!;

        Assert.Equal(decl.Name, back.Name);
        Assert.Equal(decl.TypeId, back.TypeId);
        Assert.False(back.IsCollection);
        Assert.Null(back.ElementTypeId);
        Assert.Null(back.CountAccessorFqn);
        Assert.Null(back.ItemAccessorFqn);
    }

    [Fact]
    public void CollectionDecl_SerializesWithAllFourCollectionMembers()
    {
        var decl = new ComponentFieldDecl
        {
            Name             = "Values",
            TypeId           = "",
            IsCollection     = true,
            ElementTypeId    = "System.Int32",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
        };

        var json = JsonSerializer.Serialize(decl, Options);

        Assert.Contains("\"IsCollection\":true", json);
        Assert.Contains("\"ElementTypeId\":\"System.Int32\"", json);
        Assert.Contains("\"CountAccessorFqn\":\"Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count\"", json);
        Assert.Contains("\"ItemAccessorFqn\":\"Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item\"", json);
    }

    [Fact]
    public void CollectionDecl_RoundTrips()
    {
        var decl = new ComponentFieldDecl
        {
            Name             = "Values",
            TypeId           = "",
            IsCollection     = true,
            ElementTypeId    = "System.Int32",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
        };

        var json = JsonSerializer.Serialize(decl, Options);
        var back = JsonSerializer.Deserialize<ComponentFieldDecl>(json, Options)!;

        Assert.Equal(decl.Name, back.Name);
        Assert.Equal(decl.TypeId, back.TypeId);
        Assert.True(back.IsCollection);
        Assert.Equal(decl.ElementTypeId, back.ElementTypeId);
        Assert.Equal(decl.CountAccessorFqn, back.CountAccessorFqn);
        Assert.Equal(decl.ItemAccessorFqn, back.ItemAccessorFqn);
    }
}
