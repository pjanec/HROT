using System;
using System.Reflection;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Common.Infrastructure;
using Xunit;
using Fdp.Core;

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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround);

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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
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
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
            .WithTimeRole(TimeRole.Master)
            .Build();

        Assert.IsType<MasterSyncController>(TimeControllerOf(ctx));
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-203</c> — <b>the context EXPOSES the controller it built, and it is THE SAME OBJECT the
    /// kernel got.</b>
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Reference equality, not "is not null".</b> A property that returned a second, freshly-built
    /// controller would satisfy a null check and hand the editor a clock nobody ticks — the silent-default
    /// shape this programme has measured nine times. ⇒ the rail asserts the identity, which is the only
    /// thing that makes the editor's adoption safe.
    /// </remarks>
    [Fact]
    public void Build_ExposesTheSameTimeControllerTheKernelReceived()
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig())
            .WithRole("Test", Fdp.Core.NodeRole.MuscleGround)
            .Build();

        Assert.NotNull(ctx.TimeController);
        Assert.Same(TimeControllerOf(ctx), ctx.TimeController);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-203</c> — <b>the EDITOR's exact configuration</b>: <c>TimeRole.Standalone</c> yields a
    /// master, reachable off the context.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>Standalone</c> and not <c>Master</c>: 📐 measured, <c>EditorSubsystem</c> passed
    /// <c>new TimeControllerConfig { Role = TimeRole.Standalone }</c>, and
    /// <c>TimeControllerFactory</c> routes BOTH roles to <c>MasterSyncController</c>. ⛔ The rail above
    /// covers <c>Master</c>; without this one the role the editor actually uses is untested, and the
    /// editor casts the result to the concrete master — an unchecked cast that would throw at boot.
    /// </remarks>
    [Fact]
    public void Build_WithTimeRoleStandalone_ExposesAMasterOnTheContext()
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig())
            .WithRole("Editor", Fdp.Core.NodeRole.None)
            .WithTimeRole(TimeRole.Standalone)
            .Build();

        Assert.IsType<MasterSyncController>(ctx.TimeController);
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

    /// <summary>
    /// ⭐⭐ CE-3126 — ONE geo transform per node: the builder adopts the one the composition root supplied (the network
    /// factory's, else <c>ExternalGeoTransform</c>), and publishes it as the world singleton — the instance a terrain commit
    /// sets the origin on. Before, the builder always made its own, so the wire and the world could disagree.
    /// </summary>
    [Fact]
    public void CE3126_Build_UsesTheSuppliedGeoTransform_AndPublishesItOnTheWorld()
    {
        var supplied = new Fdp.Modules.Geographic.Transforms.WGS84Transform();
        var config = HeadlessConfig();
        config.ExternalGeoTransform = supplied;
        var ctx = new HrotNodeBuilder(config).WithRole("Test", Fdp.Core.NodeRole.MuscleGround).Build();

        Assert.Same(supplied, ctx.GeoTransform);
        Assert.Same(supplied, ctx.World.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>());

        var plain = new HrotNodeBuilder(HeadlessConfig(98)).WithRole("Test", Fdp.Core.NodeRole.MuscleGround).Build();
        Assert.Same(plain.GeoTransform, plain.World.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>());
        Assert.Equal((0.0, 0.0, 0.0), plain.GeoTransform!.Origin);   // ⛔ no Berlin default
    }

    // ── CE-3330 — every ECS node keeps a LIVE event-timeline index, served on its debug surface ──────────

    private static HrotNodeContext InitialisedNode(int nodeId)
    {
        var ctx = new HrotNodeBuilder(HeadlessConfig(nodeId)).WithRole("Test", Fdp.Core.NodeRole.MuscleGround).Build();
        foreach (var m in ctx.BaseModules) ctx.Kernel.RegisterModule(m);
        ctx.Kernel.Initialize();
        return ctx;
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> — the node's OWN frame (<see cref="HrotNodeContext.Tick"/>, the one every host runs) steps the live timeline: a unit
    /// that gains a network id in frame 1 and is destroyed in frame 3 is a <c>Spawned</c> event at frame 1 and a <c>Left</c> event at frame 3,
    /// with no host wiring. 🔴 Red-proof: the <c>Timeline.Step</c> call in <c>Tick</c> behind a runtime-false guard ⇒ no frame is indexed.
    /// </summary>
    [Fact]
    public void CE3330_Node_Tick_IndexesTheShippedLanes_LiveOnTheNodesOwnFrame()
    {
        using var ctx = InitialisedNode(97);
        var world = ctx.World;
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>())
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        Entity unit = default;

        ctx.Tick();                                                                   // frame 0
        ctx.Tick(afterKernel: () =>                                                   // frame 1: the unit appears
        {
            unit = world.CreateEntity();
            world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4242));
        });
        ctx.Tick();                                                                   // frame 2
        ctx.Tick(afterKernel: () => world.DestroyEntity(unit));                       // frame 3: it dies

        var live = ctx.Timeline;
        live.Step(world, DateTime.UtcNow.Ticks, force: true);                         // publish now (the throttle is a quarter second)
        Assert.Equal(4, live.Frames);
        var spawned = Assert.Single(live.Index.Lane(Fdp.Toolkit.Replication.ReplicationTimelineLanes.Spawned.Id));
        Assert.Equal((1, 4242L), (spawned.Frame, spawned.Subject));
        var left = Assert.Single(live.Index.Lane(Fdp.Toolkit.Replication.ReplicationTimelineLanes.Left.Id));
        Assert.Equal((3, 4242L), (left.Frame, left.Subject));
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> — the ONE node debug provider serves that source: <c>timeline.read</c> is true, <c>Timeline.Source()</c> IS the
    /// context's live source and the catalogue its lanes; over no context (before Initialize / after Shutdown) there is no timeline and nothing throws.
    /// 🔴 Red-proof: the provider's <c>timeline:</c> argument returns null behind a runtime-false guard ⇒ the capability reads false.
    /// </summary>
    [Fact]
    public void CE3330_Node_TheEcsNodeDebugProvider_ServesTheContextsLiveTimeline()
    {
        using var ctx = InitialisedNode(96);
        var provider = Hrot.Presentation.DebugApi.EcsNodeDebugProvider.For("TestNode", "TestNode", () => ctx);

        Assert.True(provider.DescribeCapabilities()[Hrot.Presentation.DebugApi.DebugCapabilities.TimelineRead]);
        var access = provider.Timeline!;
        Assert.Same(ctx.Timeline, access.Source());
        Assert.Same(ctx.TimelineLanes, access.Catalog);
        Assert.True(access.Context.Live);
        Assert.Same(access, provider.Timeline);                                       // one access per context, not one per request

        HrotNodeContext? none = null;
        var gone = Hrot.Presentation.DebugApi.EcsNodeDebugProvider.For("TestNode", "TestNode", () => none);
        Assert.Null(gone.Timeline);
        Assert.False(gone.DescribeCapabilities()[Hrot.Presentation.DebugApi.DebugCapabilities.TimelineRead]);
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> — a host with its OWN frame (the editor) steps the node's timeline through <see cref="HrotNodeContext.StepTimeline"/>, with
    /// no <c>Tick</c>: the entity that gained a network id is indexed into <c>ctx.Timeline</c>, and the access every host serves
    /// (<c>EcsNodeDebugProvider.TimelineOf</c>) is the SAME for the context and reads that same source.
    /// 🔴 Red-proof: <c>StepTimeline</c>'s body behind a runtime-false guard ⇒ nothing is indexed.
    /// </summary>
    [Fact]
    public void CE3330_Node_StepTimeline_IndexesTheNodesTimeline_WithoutATick_AndTheAccessIsTheContextsOwn()
    {
        using var ctx = InitialisedNode(95);
        if (!ctx.World.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>())
            ctx.World.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        ctx.World.SetSingleton(new GlobalTime { FrameNumber = 1 });                   // one sim frame
        var unit = ctx.World.CreateEntity();
        ctx.World.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(777));

        ctx.StepTimeline();                                                           // the editor's frame, not ctx.Tick
        Assert.Equal(1, ctx.Timeline.Frames);                                         // (a source's first step publishes at once)
        var spawned = Assert.Single(ctx.Timeline.Index.Lane(Fdp.Toolkit.Replication.ReplicationTimelineLanes.Spawned.Id));
        Assert.Equal(777L, spawned.Subject);

        var access = Hrot.Presentation.DebugApi.EcsNodeDebugProvider.TimelineOf(ctx);
        Assert.Same(access, Hrot.Presentation.DebugApi.EcsNodeDebugProvider.TimelineOf(ctx));
        Assert.Same(ctx.Timeline, access.Source());
        Assert.Same(ctx.TimelineLanes, access.Catalog);
    }
    // -- CE-3330 E1 (docs/DESIGN_Director_Timeline_Editor.md 2e) - StepTimeline by the node's cluster state --------------------------------

    private static void CommitState(HrotNodeContext ctx, Fdp.Toolkit.Orchestration.ClusterState to)
        => ctx.ClusterSlave.EnqueueIntentForTest(new Fdp.Toolkit.Orchestration.ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = ctx.NodeId, Operation = Fdp.Toolkit.Orchestration.NodeOpType.CommitState,
            DomainPayload = new Fdp.Toolkit.Orchestration.CommitStatePayload(to),
        });

    private static void SimFrame(HrotNodeContext ctx, int n)
    {
        ctx.World.SetSingleton(new GlobalTime { FrameNumber = n, TotalTime = n * 0.1 });
        ctx.StepTimeline();
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> E1 — in a REVIEW (loading, operating, unloading) or a replay the node's timeline is FROZEN: <c>StepTimeline</c> adds no frame,
    /// though the world's sim frame advances. 🔴 Red-proof: the freeze return behind a runtime-false guard (<c>DateTime.UtcNow.Ticks &lt; 0</c>) ⇒ the
    /// review adds frames.
    /// </summary>
    [Fact]
    public void CE3330_Node_StepTimeline_AddsNoFrame_InAReviewOrAReplay()
    {
        using var ctx = InitialisedNode(94);
        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive);
        SimFrame(ctx, 1); SimFrame(ctx, 2);
        Assert.Equal(2, ctx.Timeline.Frames);

        foreach (var state in new[]
        {
            Fdp.Toolkit.Orchestration.ClusterState.LoadingReview, Fdp.Toolkit.Orchestration.ClusterState.OperatingReview,
            Fdp.Toolkit.Orchestration.ClusterState.UnloadingReview, Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay,
        })
        {
            CommitState(ctx, state);
            Assert.Equal(state, ctx.ClusterSlave.LocalClusterState);
            SimFrame(ctx, 10 + (int)state);
            Assert.Equal(2, ctx.Timeline.Frames);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> E1 — Live → Review → Live keeps the frames from before the review (no reset: the index continues from the live point, even
    /// through the unloading state between) and steps on. 🔴 Red-proof: the "came from a review" test ignored (behind a runtime condition)
    /// ⇒ the return to live resets.
    /// </summary>
    [Fact]
    public void CE3330_Node_StepTimeline_ALiveReviewLiveRoundTrip_KeepsTheIndex()
    {
        using var ctx = InitialisedNode(93);
        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive);
        SimFrame(ctx, 1); SimFrame(ctx, 2); SimFrame(ctx, 3);
        Assert.Equal(3, ctx.Timeline.Frames);

        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.OperatingReview);
        SimFrame(ctx, 2);                                                             // the review rewound the world's frame counter
        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.UnloadingReview);
        SimFrame(ctx, 2);
        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive);
        SimFrame(ctx, 4);

        Assert.Equal(4, ctx.Timeline.Frames);                                         // 3 before the review + the first one after it
        ctx.Timeline.Step(ctx.World, System.DateTime.UtcNow.Ticks, force: true);      // publish now
        Assert.Equal(4, ctx.Timeline.Index.FrameSim.Count);
        Assert.Equal(0.1, ctx.Timeline.Index.FrameSim[0], 6);                         // still the run's first frame
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> E1 — entering <c>OperatingLive</c> from anything but a review (here an edit session that indexed frames) starts the index
    /// AFRESH: frame 0 is the run's first frame. 🔴 Red-proof: the reset skipped (behind a runtime condition) ⇒ the edit's frames stay.
    /// </summary>
    [Fact]
    public void CE3330_Node_StepTimeline_EnteringLiveFromEdit_StartsAFreshIndex()
    {
        using var ctx = InitialisedNode(92);
        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit);
        SimFrame(ctx, 1); SimFrame(ctx, 2); SimFrame(ctx, 3);
        Assert.Equal(3, ctx.Timeline.Frames);                                         // an edit session indexes (not frozen)

        CommitState(ctx, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive);
        SimFrame(ctx, 4);                                                             // the live run's first frame (the counter did NOT go back)

        Assert.Equal(1, ctx.Timeline.Frames);
        Assert.Equal(0.4, ctx.Timeline.Index.FrameSim[0], 6);
    }
}
