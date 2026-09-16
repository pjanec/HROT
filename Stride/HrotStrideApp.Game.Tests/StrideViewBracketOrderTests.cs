#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Stride.Animation;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Game.Tests;

/// <summary>
/// Rails for <see cref="StrideViewBracket"/> — the view tier extracted out of
/// <c>EditorStrideSubsystem</c> so both shells drive one ordered unit
/// (<c>CE-207</c> / <c>S3</c>; <c>DESIGN_Stride_Node_Modes.md</c> §7.3a, ruling <c>R-S13</c>).
///
/// <para>
/// <b>⭐ THE LOAD-BEARING PROPERTY IS THE ORDER, AND IT HAS ALREADY BEEN GOT WRONG ONCE.</b> The
/// editor's selection highlight and move marker were originally emitted AFTER the gizmo render, and
/// they drew one tick late — a visible trail when dragging fast. That was fixed by moving the emission
/// earlier ("BATCH-S2-AG"), and until this extraction the fix lived only as statement order inside a
/// 1&#160;000-line method, duplicated across two tick paths. Encoding it in a class is worth nothing
/// unless something asserts it, so the first rail below reproduces the original defect: it emits from
/// the host callback and requires the primitive to reach the sink IN THE SAME FRAME.
/// </para>
///
/// <para>
/// ⚠ <c>R-142</c> checked: there is no existing view-tier suite to fold these into.
/// <c>EditorStrideSubsystemTests</c> covers boot and "does not throw" pumping;
/// <c>ReverseSyncOrderingTests</c> covers the PHYSICS bracket's ordering. Neither asserts anything
/// about the post-kernel view order, which is why the defect above was only ever found by eye.
/// </para>
/// </summary>
public sealed class StrideViewBracketOrderTests
{
    /// <summary>Records what actually reached the GPU sink, and when.</summary>
    private sealed class RecordingSink : IDebugDrawSink3D
    {
        public int FrameIndex { get; private set; }

        /// <summary>One entry per line drawn: the frame it was drawn in.</summary>
        public List<int> LinesByFrame { get; } = new();
        public List<string> Calls { get; } = new();

        public void BeginFrame() => Calls.Add("BeginFrame");
        public void EndFrame()   { Calls.Add("EndFrame"); FrameIndex++; }
        public void DrawLine(in DebugDrawLine3D line)   => LinesByFrame.Add(FrameIndex);
        public void DrawShape(in DebugDrawShape3D shape) { }
    }

    private static StrideViewBracket NewBracket(RecordingSink sink, GizmoPrimitiveBuffer buffer)
        => new StrideViewBracket(
            animationBridge: new StrideAnimationBridge(
                new StrideAnimationBackend(),
                isAnimatedClass:    static _ => false,
                jumpStartMontageId: 1001,
                jumpLoopMontageId:  1002,
                jumpEndMontageId:   1003),
            animationBinder: null,                       // null outside the real GPU app — the production shape
            gizmoRenderer:   new DebugPrimitiveRenderer3D(sink),
            producerBuffer:  buffer);

    private static EntityRepository NewWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        return world;
    }

    private static void EmitOneLine(GizmoPrimitiveBuffer buffer)
    {
        var line = DebugPrimitive.MakeLine(
            new Vector3(0, 0, 0), new Vector3(1, 0, 0),
            new Rgba32(0, 255, 255, 255),
            sizeMode: SizeMode.WorldMeters,
            target:   PipelineTarget.All);
        line.Space           = CoordinateSpace.World;
        line.LifetimeSeconds = 0f;   // transient — this frame only, like the selection box
        buffer.EmitRaw(line);
    }

    /// <summary>
    /// ⭐ THE ONE THAT MATTERS — the BATCH-S2-AG regression, as a rail. What the host emits from the
    /// callback must be rendered by THIS frame's render, not the next one.
    ///
    /// <para>
    /// ⚠ Inverse-edit red-proof: moving <c>emitHostGizmos?.Invoke()</c> below the
    /// <c>GizmoRenderer.Render</c> call in <see cref="StrideViewBracket.RunPostKernelStep"/> makes this
    /// fail — the line arrives in frame 1 instead of frame 0, which is exactly the one-tick trail.
    /// </para>
    /// </summary>
    [Fact]
    public void HostGizmosEmittedInTheCallback_AreRenderedInTheSameFrame()
    {
        using var world = NewWorld();
        var sink   = new RecordingSink();
        var buffer = new GizmoPrimitiveBuffer();
        StrideViewBracket bracket = NewBracket(sink, buffer);

        bracket.RunAnimationStep(world, 1f / 60f);
        bracket.RunPostKernelStep(world, 1f / 60f, emitHostGizmos: () => EmitOneLine(buffer));

        Assert.Single(sink.LinesByFrame);
        Assert.Equal(0, sink.LinesByFrame[0]);   // frame 0 — the same frame it was emitted in
    }

    /// <summary>
    /// The render brackets every frame with Begin/End, in that order. The pooled sink relies on
    /// <c>BeginFrame</c> to hide the previous frame's pool entities, so a missing or reordered call
    /// leaves stale geometry on screen — invisible to any assertion about what was drawn.
    /// </summary>
    [Fact]
    public void EveryFrameIsBracketedByBeginAndEndFrame_InThatOrder()
    {
        using var world = NewWorld();
        var sink   = new RecordingSink();
        var buffer = new GizmoPrimitiveBuffer();
        StrideViewBracket bracket = NewBracket(sink, buffer);

        for (int i = 0; i < 3; i++)
        {
            bracket.RunAnimationStep(world, 1f / 60f);
            bracket.RunPostKernelStep(world, 1f / 60f, emitHostGizmos: null);
        }

        Assert.Equal(
            new[] { "BeginFrame", "EndFrame", "BeginFrame", "EndFrame", "BeginFrame", "EndFrame" },
            sink.Calls);
    }

    /// <summary>
    /// A host with nothing to emit passes <see langword="null"/> and the frame still renders. This is
    /// mode 2's shape today, and the reason the parameter is REQUIRED-but-nullable rather than
    /// defaulted: omitting it has to be a written decision, not an accident.
    /// </summary>
    [Fact]
    public void NullHostEmitter_StillRendersTheFrame()
    {
        using var world = NewWorld();
        var sink   = new RecordingSink();
        var buffer = new GizmoPrimitiveBuffer();
        StrideViewBracket bracket = NewBracket(sink, buffer);

        bracket.RunPostKernelStep(world, 1f / 60f, emitHostGizmos: null);

        Assert.Equal(new[] { "BeginFrame", "EndFrame" }, sink.Calls);
        Assert.Empty(sink.LinesByFrame);
    }

    /// <summary>
    /// ⚠ The complement of the first rail, and the reason a naive "did the sink get a line?" assertion
    /// is not enough: a primitive emitted AFTER the bracket returns must NOT appear in the frame that
    /// already rendered. If this ever passed, the first rail would be vacuous.
    /// </summary>
    [Fact]
    public void GizmosEmittedAfterTheBracketReturns_AreNotInThatFrame()
    {
        using var world = NewWorld();
        var sink   = new RecordingSink();
        var buffer = new GizmoPrimitiveBuffer();
        StrideViewBracket bracket = NewBracket(sink, buffer);

        bracket.RunPostKernelStep(world, 1f / 60f, emitHostGizmos: null);
        EmitOneLine(buffer);                                    // the late emission

        Assert.Empty(sink.LinesByFrame);                        // frame 0 drew nothing

        bracket.RunPostKernelStep(world, 1f / 60f, emitHostGizmos: null);
        Assert.Single(sink.LinesByFrame);
        Assert.Equal(1, sink.LinesByFrame[0]);                  // it landed one frame late
    }

    /// <summary>
    /// The bridge is required, the binder is not. Asserted because the two nullabilities are a
    /// deliberate distinction — the binder is genuinely absent headlessly, the bridge never is.
    /// </summary>
    [Fact]
    public void ConstructionRejectsAMissingBridge_ButAcceptsAMissingBinder()
    {
        var sink   = new RecordingSink();
        var buffer = new GizmoPrimitiveBuffer();

        Assert.Throws<ArgumentNullException>(() => new StrideViewBracket(
            animationBridge: null!,
            animationBinder: null,
            gizmoRenderer:   new DebugPrimitiveRenderer3D(sink),
            producerBuffer:  buffer));

        // The binder-less form is the one every headless path uses.
        Assert.NotNull(NewBracket(sink, buffer));
    }
}
