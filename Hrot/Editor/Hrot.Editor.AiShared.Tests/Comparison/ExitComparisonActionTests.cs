using System;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.Rendering;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ExitComparisonActionTests
{
    private static ComparisonSessionState MakeSession(Guid assetId) =>
        new ComparisonSessionState(assetId, new ComparisonResponse(
            null, "Summary.", Array.Empty<ComparisonChange>(), Array.Empty<string>()));

    // ---- Exit clears session from registry ----------------------------------

    [Fact]
    public void Exit_ActiveSession_SessionRemovedFromRegistry()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(MakeSession(assetId));

        var action = new ExitComparisonAction(registry);
        action.Exit(assetId);

        Assert.Null(registry.GetSession(assetId));
    }

    // ---- Exit on asset with no session does not throw -----------------------

    [Fact]
    public void Exit_NoSession_DoesNotThrow()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        var action   = new ExitComparisonAction(registry);

        var ex = Record.Exception(() => action.Exit(assetId));
        Assert.Null(ex);
    }

    // ---- Asset content unchanged after exit (registry is the only thing touched) ----

    [Fact]
    public void Exit_DoesNotModifyOtherSessions()
    {
        var assetIdA = Guid.NewGuid();
        var assetIdB = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(MakeSession(assetIdA));
        registry.SetSession(MakeSession(assetIdB));

        var action = new ExitComparisonAction(registry);
        action.Exit(assetIdA);

        // assetA cleared, assetB untouched.
        Assert.Null(registry.GetSession(assetIdA));
        Assert.NotNull(registry.GetSession(assetIdB));
    }

    // ---- Annotation renderer IsActive false after exit ----------------------

    [Fact]
    public void Exit_AnnotationRendererIsActiveFalse()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(MakeSession(assetId));

        var renderer = new ComparisonAnnotationRenderer(registry);
        renderer.SetActiveAsset(assetId);

        Assert.True(renderer.IsActive);

        var action = new ExitComparisonAction(registry);
        action.Exit(assetId);

        Assert.False(renderer.IsActive);
    }
}
