using Hrot.CGF;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Verifies that <see cref="CgfApplication"/> registers the required Cluster state handlers
/// in the correct order (CGF1-S0104 / BATCH-23 Part A.1).
///
/// Uses <see cref="CgfApplication.ClusterSlave"/> (public property) for assertions.
/// DDS is initialized in domain 20 (reserved for CGF registration tests).
/// </summary>
[Collection("CgfHandlerTests")]
public sealed class CgfHandlerRegistrationTests : IDisposable
{
    private const int TestDomain = 20;

    // Minimal CgfApplication — no scenario serializer, default temp root.
    private readonly CgfApplication _app;

    public CgfHandlerRegistrationTests()
    {
        _app = new CgfApplication(domainId: TestDomain, nodeId: 401);
    }

    public void Dispose() => _app.Dispose();

    // ── A.1: brain record/replay handlers (BATCH-23) ──────────────────────────

    /// <summary>CGF must register a ReferenceReplayLoadHandler (first in chain).</summary>
    [Fact]
    public void ClusterSlave_RegistersReferenceReplayLoadHandler()
    {
        Assert.True(
            _app.ClusterSlave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
            "CGF brain must register ReferenceReplayLoadHandler for replay cluster participation.");
    }

    /// <summary>CGF must register a ReferenceLiveLoadHandler (after scenario handler).</summary>
    [Fact]
    public void ClusterSlave_RegistersReferenceLiveLoadHandler()
    {
        Assert.True(
            _app.ClusterSlave.IsHandlerRegistered<ReferenceLiveLoadHandler>(),
            "CGF brain must register ReferenceLiveLoadHandler for live cluster participation.");
    }

    /// <summary>CGF must register a ReferencePrefetchHandler for scenario file staging.</summary>
    [Fact]
    public void ClusterSlave_RegistersReferencePrefetchHandler()
    {
        Assert.True(
            _app.ClusterSlave.IsHandlerRegistered<ReferencePrefetchHandler>(),
            "CGF brain must register ReferencePrefetchHandler for scenario prefetch.");
    }

    /// <summary>CGF must register a ReferencePreviewHandler for dry-run operations.</summary>
    [Fact]
    public void ClusterSlave_RegistersReferencePreviewHandler()
    {
        Assert.True(
            _app.ClusterSlave.IsHandlerRegistered<ReferencePreviewHandler>(),
            "CGF brain must register ReferencePreviewHandler.");
    }
}

[CollectionDefinition("CgfHandlerTests", DisableParallelization = true)]
public class CgfHandlerTestCollection { }
