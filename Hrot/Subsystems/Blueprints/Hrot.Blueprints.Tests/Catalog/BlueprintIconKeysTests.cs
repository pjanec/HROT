using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Tests.Catalog;

/// <summary>
/// Punch-list #9: a blueprint's Open-Asset picker icon is derived from its header Dispatch +
/// Primitive.Intent so Action / Condition / Function are visually distinct.
/// </summary>
public sealed class BlueprintIconKeysTests
{
    [Theory]
    [InlineData("AiPrimitive", "Action", "asset/blueprint_action")]
    [InlineData("AiPrimitive", "Condition", "asset/blueprint_condition")]
    [InlineData("Library", null, "asset/blueprint_function")]
    [InlineData("library", "Action", "asset/blueprint_function")] // dispatch wins; case-insensitive
    public void ForHeader_MapsKnownShapes(string dispatch, string? intent, string expected)
        => Assert.Equal(expected, BlueprintIconKeys.ForHeader(dispatch, intent));

    [Theory]
    [InlineData("Instance", "Action")]  // Instance blueprints have no per-intent icon
    [InlineData("AiPrimitive", null)]   // AiPrimitive without a readable intent
    [InlineData(null, null)]
    [InlineData("AiPrimitive", "Weird")]
    public void ForHeader_FallsBackToNull(string? dispatch, string? intent)
        => Assert.Null(BlueprintIconKeys.ForHeader(dispatch, intent));

    [Fact]
    public void MappedKeys_MatchSharedConstants()
    {
        Assert.Equal(AssetKindIcons.BlueprintActionIconKey,    BlueprintIconKeys.ForHeader("AiPrimitive", "Action"));
        Assert.Equal(AssetKindIcons.BlueprintConditionIconKey, BlueprintIconKeys.ForHeader("AiPrimitive", "Condition"));
        Assert.Equal(AssetKindIcons.BlueprintFunctionIconKey,  BlueprintIconKeys.ForHeader("Library", null));
    }
}
