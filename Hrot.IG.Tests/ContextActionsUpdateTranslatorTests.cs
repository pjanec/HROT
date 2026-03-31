using System.Collections.Generic;
using Hrot.IG.Translators;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="ContextActionsUpdateTranslator.ParseActions"/> (BUG2-E002).
/// Validates that well-known ExCon numeric IDs are mapped to IG-local action names.
/// </summary>
public class ContextActionsUpdateTranslatorTests
{
    /// <summary>
    /// An action with <c>"id": 1</c> must map to <c>"IG_CenterOnEntity"</c>.
    /// </summary>
    [Fact]
    public void ParseActions_Id1_ReturnsIgCenterOnEntity()
    {
        const string json = """[{"label":"Center","id":1}]""";

        var actions = ContextActionsUpdateTranslator.ParseActions(json);

        Assert.Single(actions);
        Assert.Equal("IG_CenterOnEntity", actions[0].ActionName);
    }

    /// <summary>
    /// An action with <c>"id": 10</c> must map to <c>"IG_DeleteEntity"</c>
    /// (ExCon <c>ContextMenuActions.Delete = 10</c>).
    /// </summary>
    [Fact]
    public void ParseActions_Id10_ReturnsIgDeleteEntity()
    {
        const string json = """[{"label":"Delete","id":10}]""";

        var actions = ContextActionsUpdateTranslator.ParseActions(json);

        Assert.Single(actions);
        Assert.Equal("IG_DeleteEntity", actions[0].ActionName);
    }

    /// <summary>
    /// An action with an unmapped numeric ID must fall through to the numeric string.
    /// </summary>
    [Fact]
    public void ParseActions_UnknownId_ReturnsIdAsString()
    {
        const string json = """[{"label":"Custom","id":99}]""";

        var actions = ContextActionsUpdateTranslator.ParseActions(json);

        Assert.Single(actions);
        Assert.Equal("99", actions[0].ActionName);
    }

    /// <summary>
    /// A null or empty JSON string must return an empty action list without throwing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseActions_NullOrEmpty_ReturnsEmpty(string? menuJson)
    {
        var actions = ContextActionsUpdateTranslator.ParseActions(menuJson);

        Assert.Empty(actions);
    }
}
