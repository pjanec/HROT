using System;
using Hrot.Common.Infrastructure;
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
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround)
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
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround)
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
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround);

        builder.Build();   // first call — succeeds

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    // ── SC4 — Code-review: BuildOrchestration NOT called ─────────────────────
    // This is a static structural assertion — the source file must not contain
    // "BuildOrchestration". Verified at implementation time, not runtime.

    // ── T3b — the node bus must carry the orchestration intent registrations ──

    /// <summary>
    /// `T3b`. Every non-orchestrator node publishes its time-control intents onto
    /// <c>HrotNodeContext.EventBus</c> — <c>ClusterTimeTransportAdapter</c> issues
    /// <c>PauseTimeIntent</c>, <c>ResumeTimeIntent</c>, <c>StepTimeIntent</c>,
    /// <c>SetTimeScaleIntent</c> and <c>TransitionStateIntent</c> through it.
    ///
    /// <para><c>Hrot.ClusterRunner/Program.cs:52</c> sets
    /// <c>FdpConfig.EnforceExplicitEventRegistration = true</c> in production, and under that flag
    /// <c>PublishManaged&lt;T&gt;</c> THROWS for a type whose stream was never registered. So if the
    /// builder's bus has not had <c>OrchestrationEventRegistry.RegisterAll</c> called on it, pressing
    /// pause on a CGF/SimHost/IG toolbar throws rather than pausing.</para>
    ///
    /// <para>The design guessed this would "make a toolbar silently do nothing". It is worse and
    /// louder than that: it throws. Either way the registration is the thing to prove.</para>
    /// </summary>
    [Fact]
    public void Build_NodeEventBus_HasTheTimeControlIntentsRegistered()
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig())
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround)
            .Build();

        bool previous = Fdp.Core.FdpConfig.EnforceExplicitEventRegistration;
        Fdp.Core.FdpConfig.EnforceExplicitEventRegistration = true;
        try
        {
            // Exactly what ClusterTimeTransportAdapter does when the operator presses pause/step.
            var ex = Record.Exception(() =>
            {
                ctx.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.PauseTimeIntent());
                ctx.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.ResumeTimeIntent());
                ctx.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.StepTimeIntent { DeltaSeconds = 1f / 60f });
                ctx.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.SetTimeScaleIntent { TimeScale = 1f });
            });

            Assert.True(ex is null,
                "the node's EventBus must have OrchestrationEventRegistry.RegisterAll applied — " +
                "ClusterTimeTransportAdapter publishes these onto it, and production runs strict " +
                $"registration. Got: {ex?.GetType().Name}: {ex?.Message}");
        }
        finally
        {
            Fdp.Core.FdpConfig.EnforceExplicitEventRegistration = previous;
        }
    }

    /// <summary>
    /// And the intents must actually round-trip on that bus — registration alone is not the point,
    /// the drainer (<c>ClusterOpEgressTranslator</c>, or the master on an all-in-one node) has to
    /// see them.
    /// </summary>
    [Fact]
    public void Build_NodeEventBus_RoundTripsATimeIntent()
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig())
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround)
            .Build();

        ctx.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.StepTimeIntent { DeltaSeconds = 0.5f });
        ctx.EventBus.SwapBuffers();

        var read = ctx.EventBus.ReadManaged<Fdp.Toolkit.Time.Domain.StepTimeIntent>();
        Assert.Single(read);
        Assert.Equal(0.5f, read[0].DeltaSeconds, 3);
    }
}
