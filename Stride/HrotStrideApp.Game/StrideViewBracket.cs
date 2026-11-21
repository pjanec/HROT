#nullable enable
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;   // ISimulationView
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Navigation;
using Hrot.Stride.Animation;
using Hrot.Stride.Core;

namespace HrotStrideApp;

/// <summary>
/// <b>StrideViewBracket</b> — the cohesive host-driven <b>view</b> steps that must run at a fixed
/// point relative to <c>Kernel.Update()</c> and to <see cref="StridePhysicsBracket"/>'s post step
/// (<c>CE-207</c> / <c>S3</c>; design <c>DESIGN_Stride_Node_Modes.md</c> §7.3a, ruling <c>R-S13</c>).
///
/// <para>
/// It is the sibling of <see cref="StridePhysicsBracket"/> and deliberately mirrors it: it is
/// <b>not</b> a composition root (owns no modules, resolves no capabilities, builds no world), not a
/// system and not an <c>IEcsModule</c> — the kernel never sees it. It is constructed by the shell,
/// handed its collaborators, and its entry points' numbered lists ARE the contract. The caller decides
/// <i>whether</i> a step runs; the bracket decides <i>in what order</i>.
/// </para>
///
/// <para>
/// <b>⭐ Why it exists.</b> The order between these units is load-bearing and, until this extraction,
/// was an accident of statement order inside a 1&#160;000-line subsystem — duplicated across
/// <c>EditorStrideSubsystem</c>'s two tick paths, where mode 2's shell would have made a third copy.
/// No rendering logic is new here: every unit already existed and both existing paths already called
/// all of them.
/// </para>
///
/// <para>
/// <b>⛔ TWO ENTRY POINTS, AND THE INTERLEAVING IS THE POINT.</b> The physics bracket's post step runs
/// <i>between</i> them, so this is not one contiguous call:
/// </para>
/// <list type="number">
///   <item><see cref="RunAnimationStep"/> — the bridge registers mannequins with the backend.</item>
///   <item><c>StridePhysicsBracket.RunPostKernelStep</c> — split-authority forward sync, whose Pass A
///     is the visual-set reconciliation that CREATES those mannequins' <c>AnimationComponent</c>s.</item>
///   <item><see cref="RunPostKernelStep"/> — the binder can now bind them, and the frame is rendered.</item>
/// </list>
/// <para>
/// ⚠ Collapsing these into one call would break the binder, which needs BOTH ① and ② to have run.
/// That dependency was recorded in <c>EditorStrideSubsystem</c> ("STR-P4, BATCH-16 Fix A") and is
/// preserved verbatim rather than re-derived.
/// </para>
///
/// <para>
/// <b>⛔ WHAT THIS BRACKET DOES NOT OWN</b> — the list a reviewer uses to catch scope creep:
/// </para>
/// <list type="bullet">
///   <item><b><see cref="StrideVisualBindingSystem"/></b> — ⚠ §7.3a lists it as this bracket's step ①.
///     <b>Measured <c>2026-09-07</c>: it is already bracketed, by the PHYSICS bracket.</b> It is not
///     called directly by anything; it is Pass A of <c>SplitAuthorityStrideSyncScript.Sync</c>, whose
///     Pass B is authority forward-sync. Prying Pass A out to satisfy the diagram would be logic
///     surgery on a live render path, and §7.3a's own justification for the extraction is that it
///     "moves call sites, not logic". §7.3a is corrected instead.</item>
///   <item><c>Kernel.Update()</c>, the orchestration pump, the physics bracket — the shell's job.</item>
///   <item>Anything that writes <c>SimTransform</c>. This bracket READS the world and renders it.</item>
///   <item>The 2-D companion window, and the editor's own selection/marker <i>emission</i> — see
///     <paramref name="emitHostGizmos"/> on <see cref="RunPostKernelStep"/>.</item>
/// </list>
///
/// <para>
/// <b>⚠ <c>wallDt</c>, deliberately.</b> The view tier is free-running and interpolates <i>between</i>
/// sim frames — it is the one consumer that legitimately wants wall time. ⛔ Contrast <c>R-S12</c> /
/// <c>CE-219</c>: the PHYSICS bracket must never be given the wall delta, or bodies integrate while the
/// cluster is paused.
/// </para>
/// </summary>
public sealed class StrideViewBracket
{
    /// <summary>The ECS→Stride animation bridge. Required — the view tier has no meaning without it.</summary>
    public StrideAnimationBridge AnimationBridge { get; }

    /// <summary>
    /// Live animation glue. <see langword="null"/> outside the real GPU app, which is why the reconcile
    /// is null-conditional rather than guarded by a flag.
    /// </summary>
    public MannequinAnimationBinder? AnimationBinder { get; }

    /// <summary>The 3-D gizmo renderer.</summary>
    public DebugPrimitiveRenderer3D GizmoRenderer { get; }

    /// <summary>
    /// The frame's gizmo primitive buffer — written by producers all frame, drained by
    /// <see cref="RunPostKernelStep"/>, then advanced.
    /// </summary>
    public GizmoPrimitiveBuffer ProducerBuffer { get; }

    public StrideViewBracket(
        StrideAnimationBridge      animationBridge,
        MannequinAnimationBinder?  animationBinder,
        DebugPrimitiveRenderer3D   gizmoRenderer,
        GizmoPrimitiveBuffer       producerBuffer)
    {
        AnimationBridge = animationBridge ?? throw new ArgumentNullException(nameof(animationBridge));
        AnimationBinder = animationBinder;
        GizmoRenderer   = gizmoRenderer   ?? throw new ArgumentNullException(nameof(gizmoRenderer));
        ProducerBuffer  = producerBuffer  ?? throw new ArgumentNullException(nameof(producerBuffer));
    }

    /// <summary>
    /// ① Animation bridge — <b>after</b> <c>Kernel.Update()</c> so it reads post-physics state, and
    /// <b>before</b> the physics bracket's post step so the visual sync can create the
    /// <c>AnimationComponent</c>s for whatever it registers.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="wallDt">Wall/render delta in seconds — see the class remarks.</param>
    public void RunAnimationStep(EntityRepository world, float wallDt)
    {
        // Traversal events are dispatched before Execute so a jump montage starts on the same frame
        // the off-mesh link was entered (EditorStrideSubsystem step 4b, preserved verbatim).
        ReadOnlySpan<OffMeshTraversalStartedEvent> traversals =
            ((ISimulationView)world).ReadEvents<OffMeshTraversalStartedEvent>();
        AnimationBridge.DispatchTraversals(traversals);
        AnimationBridge.Execute(world, wallDt);
    }

    /// <summary>
    /// ③ The rest of the view frame, in this order:
    /// <list type="number">
    ///   <item><b>Animation binder reconcile</b> — binds a blend-tree builder to each new mannequin and
    ///     releases vanished ones. Needs <see cref="RunAnimationStep"/> AND the physics bracket's post
    ///     step to have run this frame.</item>
    ///   <item><b><paramref name="emitHostGizmos"/></b> — the host's last chance to write into THIS
    ///     frame's buffer.</item>
    ///   <item><b>Gizmo render</b> — <c>BeginFrame</c> hides last frame's pool entities,
    ///     <c>Render</c> resolves and activates what this frame needs, <c>EndFrame</c> is a no-op for
    ///     the pooled sink, then the buffer's persistence clock advances.</item>
    /// </list>
    /// </summary>
    /// <param name="world">The ECS world. Reserved for future view steps; the current three do not read it.</param>
    /// <param name="wallDt">Wall/render delta in seconds — advances the gizmo buffer's persistence clock.</param>
    /// <param name="emitHostGizmos">
    /// ⭐⭐ <b>Host-specific emission, invoked immediately before the render — and REQUIRED, not
    /// optional-with-a-default, precisely so that passing nothing is a visible decision.</b>
    ///
    /// <para>⛔ <b>Why this ordering is load-bearing and not a preference.</b> The editor's selection
    /// highlight and move marker used to be emitted AFTER the render, and they drew one tick late —
    /// a visible trail when dragging fast. That was fixed by moving the emission earlier
    /// ("BATCH-S2-AG"), and the fix is what this parameter's position encodes. A host that emits after
    /// <see cref="RunPostKernelStep"/> returns reintroduces the defect.</para>
    ///
    /// <para>⚠ It is a host callback rather than a bracket-owned step because the editor's emitters
    /// read the 2-D window's selection state; dragging that into a shared bracket would couple mode 2
    /// to the editor. Pass <see langword="null"/> for a host with nothing to emit.</para>
    /// </param>
    public void RunPostKernelStep(EntityRepository world, float wallDt, Action? emitHostGizmos)
    {
        // 1. Live animation glue reconcile (STR-P4, BATCH-16 Fix A).
        AnimationBinder?.Reconcile();

        // 2. Host emission INTO this frame's buffer — see the parameter docs for why it is here.
        emitHostGizmos?.Invoke();

        // 3. Render, then advance the buffer's persistence clock (STR-P5-T1 / STR-D16, BATCH-21).
        GizmoRenderer.Sink.BeginFrame();
        GizmoRenderer.Render(ProducerBuffer.GetFrame());
        GizmoRenderer.Sink.EndFrame();
        ProducerBuffer.EndFrame(wallDt);
    }
}
