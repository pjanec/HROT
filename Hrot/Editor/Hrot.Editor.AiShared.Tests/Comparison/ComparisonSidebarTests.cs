using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ComparisonSidebarTests
{
    private static ComparisonChange MakeChange(string severity, string? elementId) =>
        new ComparisonChange("node_modified", elementId, "desc", null, null, null, severity, "detail");

    private static ComparisonResponse MakeResponse(params ComparisonChange[] changes) =>
        new ComparisonResponse(null, "Summary.", changes, Array.Empty<string>());

    private static ComparisonSessionState MakeSession(params ComparisonChange[] changes)
    {
        // Default: behavior enabled, cosmetic disabled.
        return new ComparisonSessionState(Guid.NewGuid(), MakeResponse(changes));
    }

    // ---- VisibleChanges filters by enabled severities -----------------------

    [Fact]
    public void VisibleChanges_FiltersOutDisabledSeverity()
    {
        var session = MakeSession(
            MakeChange("behavior", Guid.NewGuid().ToString()),
            MakeChange("behavior", Guid.NewGuid().ToString()),
            MakeChange("cosmetic", Guid.NewGuid().ToString())); // cosmetic off by default

        var state = new ComparisonSidebarState(session);

        Assert.Equal(2, state.VisibleChanges.Count);
    }

    // ---- Toggle severity updates VisibleChanges dynamically -----------------

    [Fact]
    public void ToggleSeverity_CosmeticOn_IncreasesVisibleChanges()
    {
        var session = MakeSession(
            MakeChange("behavior", null),
            MakeChange("behavior", null),
            MakeChange("cosmetic", null));

        var state = new ComparisonSidebarState(session);

        Assert.Equal(2, state.VisibleChanges.Count);

        // Toggle cosmetic on.
        session.ToggleSeverity("cosmetic");
        Assert.Equal(3, state.VisibleChanges.Count);

        // Toggle cosmetic off again.
        session.ToggleSeverity("cosmetic");
        Assert.Equal(2, state.VisibleChanges.Count);
    }

    // ---- FocusChange invokes callback with elementId -----------------------

    [Fact]
    public void FocusChange_NonNullElementId_InvokesCallback()
    {
        var session = MakeSession();

        string? received = null;
        var state = new ComparisonSidebarState(session, id => received = id);

        var change = MakeChange("behavior", "abc");
        state.FocusChange(change);

        Assert.Equal("abc", received);
    }

    // ---- FocusChange with null elementId does not invoke callback -----------

    [Fact]
    public void FocusChange_NullElementId_DoesNotInvokeCallback()
    {
        var session = MakeSession();

        bool called = false;
        var state = new ComparisonSidebarState(session, _ => called = true);

        var change = MakeChange("behavior", null);
        state.FocusChange(change); // should not throw or invoke callback

        Assert.False(called);
    }
}
