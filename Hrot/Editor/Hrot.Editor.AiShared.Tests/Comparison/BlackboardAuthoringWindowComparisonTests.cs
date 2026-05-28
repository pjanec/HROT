using System;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class BlackboardAuthoringWindowComparisonTests
{
    private static ComparisonChange MakeChange(
        string kind,
        string? elementId,
        string? oldValue = null,
        string? newValue = null,
        string description = "desc") =>
        new ComparisonChange(kind, elementId, description, null, oldValue, newValue, "behavior", "detail");

    private static ComparisonResponse MakeResponse(params ComparisonChange[] changes) =>
        new ComparisonResponse(null, "Summary.", changes, Array.Empty<string>());

    private static ComparisonSessionState MakeSession(params ComparisonChange[] changes) =>
        new ComparisonSessionState(Guid.NewGuid(), MakeResponse(changes));

    // ---- variable_added -----------------------------------------------------

    [Fact]
    public void VariableAdded_MatchingField_IsAddedTrue()
    {
        var session = MakeSession(MakeChange("variable_added", "AmmoCount"));
        var dec     = BlackboardComparisonDecorator.GetDecoration("AmmoCount", session);

        Assert.True(dec.IsAdded);
    }

    // ---- variable_removed ---------------------------------------------------

    [Fact]
    public void VariableRemoved_MatchingField_IsRemovedTrue()
    {
        var session = MakeSession(MakeChange("variable_removed", "OldField"));
        var dec     = BlackboardComparisonDecorator.GetDecoration("OldField", session);

        Assert.True(dec.IsRemoved);
    }

    // ---- variable_retyped ---------------------------------------------------

    [Fact]
    public void VariableRetyped_MatchingField_IsRetypedTrueWithNewType()
    {
        var session = MakeSession(MakeChange("variable_retyped", "Health", newValue: "float"));
        var dec     = BlackboardComparisonDecorator.GetDecoration("Health", session);

        Assert.True(dec.IsRetyped);
        Assert.Equal("float", dec.NewType);
    }

    // ---- variable_renamed ---------------------------------------------------

    [Fact]
    public void VariableRenamed_MatchingField_IsRenamedTrueWithOldName()
    {
        var session = MakeSession(MakeChange("variable_renamed", "BurstShotsRemaining", oldValue: "AmmoCount"));
        var dec     = BlackboardComparisonDecorator.GetDecoration("BurstShotsRemaining", session);

        Assert.True(dec.IsRenamed);
        Assert.Equal("AmmoCount", dec.OldName);
    }

    // ---- no session ---------------------------------------------------------

    [Fact]
    public void NoSession_AllFalse_NullValues()
    {
        var dec = BlackboardComparisonDecorator.GetDecoration("anything", null);

        Assert.False(dec.IsAdded);
        Assert.False(dec.IsRemoved);
        Assert.False(dec.IsRetyped);
        Assert.False(dec.IsRenamed);
        Assert.Null(dec.OldName);
        Assert.Null(dec.NewType);
    }
}
