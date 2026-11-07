using Hrot.Common.Infrastructure;
using Hrot.Player;
using Hrot.Tests.ExtensionStaging;
using Xunit;

namespace Hrot.Player.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NodeExtensionsStaticsCollection { public const string Name = "NodeExtensionsStatics"; }

/// <summary>
/// ⭐ CE-4296 (design section 7) — <c>PlayerApplication.Shutdown</c> ends the node through the base's <c>DisposeResources()</c> like every other host, so an
/// installed extension's capability is freed. Asserted on the CONSTRUCTED host (a headless embedded Player, as <c>ContinuousDragTests</c> builds it).
/// 🔴 Red-proof: <c>Shutdown</c> not calling <c>DisposeResources</c> ⇒ the count does not move. NodeExtensions is process-wide: this class runs alone.
/// </summary>
[Collection(NodeExtensionsStaticsCollection.Name)]
public sealed class ThePlayerTearsDownNodeExtensionsTests
{
    [Fact(Timeout = 60_000)]
    public void CE4296_PlayerShutdown_DisposesTheExtensionsCapability()
    {
        NodeExtensions.ResetForTests();
        try
        {
            NodeExtensions.LoadAll(SampleExtensionStaging.SharedBaseDirectory);
            var app = new PlayerApplication();
            app.InitializeEmbedded(headless: true, domainIdOverride: 228, networkFactory: PlayerTestFactory.CreateHeadless());
            var disposals = NodeExtensions.Discovered[0].GetType().GetField("Disposals")!;
            int before = (int)disposals.GetValue(null)!;

            app.Shutdown(ownsWindow: false);

            Assert.Equal(before + 1, (int)disposals.GetValue(null)!);
        }
        finally { NodeExtensions.ResetForTests(); }
    }
}
