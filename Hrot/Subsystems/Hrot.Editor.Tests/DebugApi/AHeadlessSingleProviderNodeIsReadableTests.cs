using Fdp.Core;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐ A process with ONE debug provider whose perspective no window claims still answers world reads through it.
/// <para>📌 Measured 2026-10-17 on a five-process cluster: the headless traffic node's window manager reported "Default", no
/// provider claimed it, and every <c>/entities</c> read answered NOT_SUPPORTED_HERE — the node could not be observed. ⭐ With
/// SEVERAL providers the fallback stays off: answering for the first would answer for the wrong node.</para>
/// </summary>
public sealed class AHeadlessSingleProviderNodeIsReadableTests
{
    [Fact]
    public void OneProvider_UnclaimedPerspective_ResolvesToThatProvider()
    {
        var world = new EntityRepository();
        var dispatcher = new PerspectiveScopedDispatcher(
            new[] { new SubsystemDebugProvider("Traffic", "Traffic", world: () => world) },
            currentPerspective: () => "Default", acksPending: null);

        Assert.Equal("Traffic", dispatcher.CurrentPerspective);
        Assert.Same(world, dispatcher.World);
    }

    [Fact]
    public void SeveralProviders_UnclaimedPerspective_ResolvesToNone()
    {
        var dispatcher = new PerspectiveScopedDispatcher(
            new[] { new SubsystemDebugProvider("IG", "IG"), new SubsystemDebugProvider("CGF", "Scenario") },
            currentPerspective: () => "Default", acksPending: null);

        Assert.Equal("Default", dispatcher.CurrentPerspective);
        Assert.Null(dispatcher.Active());
    }

    [Fact]
    public void OneProvider_ClaimedPerspective_IsUnchanged()
    {
        var dispatcher = new PerspectiveScopedDispatcher(
            new[] { new SubsystemDebugProvider("SimHost", "SimHost") },
            currentPerspective: () => "SimHost", acksPending: null);

        Assert.Equal("SimHost", dispatcher.CurrentPerspective);
        Assert.NotNull(dispatcher.Active());
    }

    // ── ⭐⭐⭐ CE-4094 (AQ108 M7/D22) — the `Start` ALIAS: HTTP keeps answering while the Start perspective shows ─────────────────────────

    /// <summary>
    /// ⭐ With `Start` current on the `--mode all` shape (several providers) every read routes to the REMEMBERED perspective's provider, and the
    /// dispatcher still REPORTS `Start`. 🔴 Mutation: <c>Active()</c> resolving <c>CurrentPerspective</c> instead of <c>RoutedTo</c> ⇒ null ⇒ red.
    /// </summary>
    [Fact]
    public void SeveralProviders_StartRoutesToTheRememberedPerspective_AndReportsStart()
    {
        var world = new EntityRepository();
        string? remembered = "Scenario";
        var dispatcher = new PerspectiveScopedDispatcher(
            new[] { new SubsystemDebugProvider("IG", "IG"), new SubsystemDebugProvider("CGF", "Scenario", world: () => world) },
            currentPerspective: () => "Start", acksPending: null, rememberedPerspective: () => remembered);

        Assert.Equal("Start", dispatcher.CurrentPerspective);
        Assert.Equal("Scenario", dispatcher.RoutedTo);
        Assert.Equal("Scenario", dispatcher.Active()!.Perspective);
        Assert.Same(world, dispatcher.World);

        remembered = "IG";                                    // read LIVE, never latched
        Assert.Equal("IG", dispatcher.Active()!.Perspective);
    }

    /// <summary>⭐ On a one-provider host `Start` is reported as `Start` (not renamed to the provider's perspective), and the reads still answer.</summary>
    [Fact]
    public void OneProvider_StartIsReportedAsStart_AndStillReadable()
    {
        var world = new EntityRepository();
        var dispatcher = new PerspectiveScopedDispatcher(
            new[] { new SubsystemDebugProvider("SimHost", "SimHost", world: () => world) },
            currentPerspective: () => "Start", acksPending: null, rememberedPerspective: () => "SimHost");

        Assert.Equal("Start", dispatcher.CurrentPerspective);
        Assert.Same(world, dispatcher.World);
    }

    /// <summary>⭐ No first-provider fallback is introduced by the alias: `Start` with nothing remembered, on several providers, resolves to none.</summary>
    [Fact]
    public void SeveralProviders_StartWithNothingRemembered_ResolvesToNone()
    {
        var dispatcher = new PerspectiveScopedDispatcher(
            new[] { new SubsystemDebugProvider("IG", "IG"), new SubsystemDebugProvider("CGF", "Scenario") },
            currentPerspective: () => "Start", acksPending: null, rememberedPerspective: () => null);

        Assert.Equal("Start", dispatcher.CurrentPerspective);
        Assert.Null(dispatcher.Active());
    }
}
