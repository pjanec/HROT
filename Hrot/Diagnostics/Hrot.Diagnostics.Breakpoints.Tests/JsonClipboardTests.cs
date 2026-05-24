using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

public sealed class JsonClipboardTests
{
    // No [Collection("ComponentRegistry")] needed -- no ECS component registration here.

    [Fact]
    public void JSON_CopyPaste_RoundTrip_PreservesAllFields()
    {
        // Build a compound predicate with mixed child types
        var original = new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto>
            {
                new PropertyMatchDto
                {
                    PropertyPath = "Health",
                    Predicate    = new NumericPredicateDto { MinValue = 0, MaxValue = 50 },
                },
                new BehaviorParamPredicateDto
                {
                    BehaviorId = 12345,
                },
                new ExternalHitTagPredicateDto { Tag = "BP:abc123" },
            },
            ReadOnlyChildIndices = new List<int> { 0 },
        };

        // Serialize -> deserialize
        string json = BreakpointJsonClipboard.Serialize(original);
        var restored = BreakpointJsonClipboard.TryDeserialize(json);

        Assert.NotNull(restored);
        var compound = Assert.IsType<CompoundPredicateDto>(restored);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(3, compound.Conditions.Count);

        // Child 0: PropertyMatchDto
        var child0 = Assert.IsType<PropertyMatchDto>(compound.Conditions[0]);
        Assert.Equal("Health", child0.PropertyPath);
        var numPred = Assert.IsType<NumericPredicateDto>(child0.Predicate);
        Assert.Equal(0.0, numPred.MinValue);
        Assert.Equal(50.0, numPred.MaxValue);

        // Child 1: BehaviorParamPredicateDto
        var child1 = Assert.IsType<BehaviorParamPredicateDto>(compound.Conditions[1]);
        Assert.Equal(12345, child1.BehaviorId);

        // Child 2: ExternalHitTagPredicateDto
        var child2 = Assert.IsType<ExternalHitTagPredicateDto>(compound.Conditions[2]);
        Assert.Equal("BP:abc123", child2.Tag);

        // ReadOnlyChildIndices preserved
        Assert.Single(compound.ReadOnlyChildIndices);
        Assert.Equal(0, compound.ReadOnlyChildIndices[0]);
    }

    [Fact]
    public void JSON_TryDeserialize_InvalidJson_ReturnsNull()
    {
        var result = BreakpointJsonClipboard.TryDeserialize("{ not valid json {{{");
        Assert.Null(result);
    }

    [Fact]
    public void JSON_TryDeserialize_UnknownType_ReturnsNull()
    {
        // A JSON object with a $type discriminator that doesn't match any known DTO
        const string badJson = """{"$type":"UnknownType","someField":1}""";
        var result = BreakpointJsonClipboard.TryDeserialize(badJson);
        Assert.Null(result);
    }

    [Fact]
    public void JSON_Serialize_ExternalHitTag_ProducesCorrectDiscriminator()
    {
        var dto = new ExternalHitTagPredicateDto { Tag = "my-tag" };
        string json = BreakpointJsonClipboard.Serialize(dto);

        // Verify the $type discriminator is included (needed for poly deserialization)
        Assert.Contains("ExternalHitTag", json);
        Assert.Contains("my-tag", json);
    }
}
