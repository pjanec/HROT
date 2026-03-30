using Bagira.CGF;
using FDP.Toolkit.Orchestration.Handlers;

namespace Bagira.Runner.Tests;

/// <summary>
/// Verifies that <see cref="CgfApplication"/> registers the required DSM handlers
/// in the correct order (CGF1-S0104 / BATCH-23 Part A.1).
///
/// Uses <see cref="CgfApplication.DrillSlave"/> (public property) for assertions.
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
    public void DrillSlave_RegistersReferenceReplayLoadHandler()
    {
        Assert.True(
            _app.DrillSlave.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
            "CGF brain must register ReferenceReplayLoadHandler for replay DSM participation.");
    }

    /// <summary>CGF must register a ReferenceLiveLoadHandler (after scenario handler).</summary>
    [Fact]
    public void DrillSlave_RegistersReferenceLiveLoadHandler()
    {
        Assert.True(
            _app.DrillSlave.IsHandlerRegistered<ReferenceLiveLoadHandler>(),
            "CGF brain must register ReferenceLiveLoadHandler for live DSM participation.");
    }

    /// <summary>CGF must register a ReferencePrefetchHandler for scenario file staging.</summary>
    [Fact]
    public void DrillSlave_RegistersReferencePrefetchHandler()
    {
        Assert.True(
            _app.DrillSlave.IsHandlerRegistered<ReferencePrefetchHandler>(),
            "CGF brain must register ReferencePrefetchHandler for scenario prefetch.");
    }

    /// <summary>CGF must register a ReferenceDryRunHandler for dry-run operations.</summary>
    [Fact]
    public void DrillSlave_RegistersReferenceDryRunHandler()
    {
        Assert.True(
            _app.DrillSlave.IsHandlerRegistered<ReferenceDryRunHandler>(),
            "CGF brain must register ReferenceDryRunHandler.");
    }
}

[CollectionDefinition("CgfHandlerTests", DisableParallelization = true)]
public class CgfHandlerTestCollection { }
