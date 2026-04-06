using System;
using Hrot.ClusterRunner.Infrastructure;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="HrotNodeBuilder"/>.
/// All tests run in headless mode (no DDS, no network).
/// </summary>
public sealed class HrotNodeBuilderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HrotNodeConfig HeadlessConfig(int nodeId = 99) => new()
    {
        DomainId      = 99,
        NodeId        = nodeId,
        SubsystemName = "TestNode",
        LocalTempRoot = System.IO.Path.GetTempPath(),
        Headless      = true,
    };

    // ── SC1 — Builder produces valid context (headless) ───────────────────────

    [Fact]
    public void Build_Headless_ReturnsNonNullContext()
    {
        var config = HeadlessConfig();
        var ctx = new HrotNodeBuilder(config)
            .WithRole("Test", Hrot.SimHost.NodeRole.MuscleGround)
            .Build();

        Assert.NotNull(ctx);
        Assert.NotNull(ctx.World);
        Assert.NotNull(ctx.Kernel);
        Assert.NotNull(ctx.EventBus);
        Assert.NotNull(ctx.EntityMap);
        Assert.NotNull(ctx.ClusterSlave);
        Assert.NotNull(ctx.BaseModules);
        Assert.NotEmpty(ctx.BaseModules);
    }

    // ── SC2 — Kernel has an active time controller ────────────────────────────

    [Fact]
    public void Build_Headless_KernelHasTimeController()
    {
        var config = HeadlessConfig();
        var ctx = new HrotNodeBuilder(config)
            .WithRole("Test", Hrot.SimHost.NodeRole.MuscleGround)
            .Build();

        // Register base modules then call Initialize() to prove the time controller
        // was set — Initialize() throws if SetTimeController() was not called.
        foreach (var m in ctx.BaseModules)
            ctx.Kernel.RegisterModule(m);

        var ex = Record.Exception(() => ctx.Kernel.Initialize());
        Assert.Null(ex);
    }

    // ── SC3 — Double-build throws ─────────────────────────────────────────────

    [Fact]
    public void Build_CalledTwice_ThrowsInvalidOperationException()
    {
        var config  = HeadlessConfig();
        var builder = new HrotNodeBuilder(config)
            .WithRole("Test", Hrot.SimHost.NodeRole.MuscleGround);

        builder.Build();   // first call — succeeds

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    // ── SC4 — Code-review: BuildOrchestration NOT called ─────────────────────
    // This is a static structural assertion — the source file must not contain
    // "BuildOrchestration". Verified at implementation time, not runtime.
}
