using System;
using System.Reflection;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
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

    // ── N₀ / CE-201 — the TIME ROLE is an input, and its default is the old behaviour ──────
    //
    // ⭐⭐⭐ WHY THIS MATTERS BEYOND THE BUILDER. Build() HARDWIRED TimeRole.Slave, while the editor
    // is the time MASTER — it drives a MasterSyncController. So the editor could not adopt this
    // builder without silently becoming a slave to a cluster it is meant to drive, and every
    // editor-adoption item was blocked on that one line.
    // 🔒 User, 2026-09-03: "add the time role change to the plan to unblock editor."

    /// <summary>
    /// ⭐ The DEFAULT is Slave — exactly what every caller got before N₀, so the three hosts that
    /// already use this builder are unaffected. A default that changed behaviour would be a
    /// migration disguised as a parameter.
    /// </summary>
    [Fact]
    public void Build_WithoutDeclaringATimeRole_IsStillASlave()
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig())
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround)
            .Build();

        Assert.IsType<SlaveSyncController>(TimeControllerOf(ctx));
    }

    /// <summary>
    /// ⭐⭐ THE RAIL THE EDITOR NEEDS: a declared Master is honoured, not quietly downgraded.
    /// ⛔ Without this the unblocking is unverified — a WithTimeRole that was accepted and ignored
    /// would compile, read as done, and leave the editor a slave.
    /// </summary>
    [Fact]
    public void Build_WithTimeRoleMaster_TheNodeOwnsTheClock()
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig())
            .WithRole("Test", Hrot.Common.NodeRole.MuscleGround)
            .WithTimeRole(TimeRole.Master)
            .Build();

        Assert.IsType<MasterSyncController>(TimeControllerOf(ctx));
    }

    /// <summary>The time controller the kernel was actually built with.</summary>
    /// <remarks>
    /// ⭐⭐ <b>The role is expressed as the controller TYPE, not as a stored field</b> — measured:
    /// <c>TimeControllerFactory.Create</c> switches <c>TimeRole.Master =&gt; CreateMaster</c> /
    /// <c>Slave =&gt; CreateSlave</c>, and neither controller keeps the enum. ⇒ asserting the type is
    /// both simpler and STRONGER than reading a field back: it is the object whose behaviour differs.
    ///
    /// <para>⚠ Via reflection deliberately: the assertion must be about the CONSTRUCTED controller,
    /// never about the config the test itself passed in — that is the <c>CE-053</c> shape, a rail
    /// that supplies the input it is testing and therefore passes whatever the builder does.</para>
    /// </remarks>
    private static object TimeControllerOf(HrotNodeContext ctx)
    {
        foreach (var f in ctx.Kernel.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            object? v = f.GetValue(ctx.Kernel);
            if (v is ITimeController controller) return controller;
        }

        throw new Xunit.Sdk.XunitException(
            "The kernel holds no ITimeController. The rail must assert the CONSTRUCTED controller — " +
            "fix the reader, do not weaken the assertion.");
    }
}
