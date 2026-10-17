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
}
