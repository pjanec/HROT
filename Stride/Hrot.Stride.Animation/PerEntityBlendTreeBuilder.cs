using System;
using System.Collections.Generic;
using Stride.Animations;
using Stride.Engine;

namespace Hrot.Stride.Animation;

/// <summary>
/// Per-entity Stride blend-tree builder (DD-1 §15.1/§15.2). Implements Stride's
/// <see cref="IBlendTreeBuilder"/>; one instance is attached to each registered
/// entity's <see cref="AnimationComponent"/> via
/// <c>animationComponent.BlendTreeBuilder = builder</c>. Stride's animation
/// processor calls <see cref="BuildBlendTree"/> every frame; this builder pushes
/// the idle/walk/run locomotion blend (and, when a montage slot is active, the
/// montage clip on top) describing the frame's pose composition.
/// <para>
/// <b>This is the GPU-bound half of the animation backend.</b> It owns
/// <see cref="AnimationClipEvaluator"/> instances created from the entity's
/// <c>Blender</c> (a <c>GraphicsDevice</c>-backed resource), and is only
/// instantiated when a real <see cref="AnimationComponent"/> is available — i.e.
/// inside the running Stride app. The <b>decision of which clips to blend and at
/// what weight</b> is computed headlessly by <see cref="LocomotionBlend"/> and the
/// backend's montage slot state machine, then handed to this builder via
/// <see cref="SetLocomotion"/> / <see cref="SetMontage"/>, so the behavioral logic
/// is unit-tested without this class.
/// </para>
/// </summary>
/// <remarks>
/// Modeled on the template <c>HrotStrideApp.Player.AnimationController</c>
/// (DD-1 §15.3). The locomotion two-clip lerp uses
/// <c>AnimationOperation.NewBlend(CoreAnimationOperation.Blend, factor)</c>; the
/// montage overlay is pushed last so it wins on shared bones (Override compositing,
/// DD-4 §2). Aim/additive layering and per-bone masks are out of scope for the
/// idle/walk/run + jump-montage bring-up (STR-P4-T1) and are documented as
/// follow-on work for the locomotion bridge (BATCH-14) and aim layer.
/// </remarks>
public sealed class PerEntityBlendTreeBuilder : IBlendTreeBuilder
{
    private readonly AnimationComponent _animationComponent;

    // Locomotion clip evaluators (GPU-bound; created from the Blender).
    private readonly AnimationClip _idleClip;
    private readonly AnimationClip _walkClip;
    private readonly AnimationClip _runClip;
    private readonly AnimationClipEvaluator _idleEval;
    private readonly AnimationClipEvaluator _walkEval;
    private readonly AnimationClipEvaluator _runEval;

    // Optional full-body montage clip pool, keyed by montage asset id hash.
    private readonly Dictionary<int, AnimationClip> _montageClips = new();
    private readonly Dictionary<int, AnimationClipEvaluator> _montageEvals = new();

    // ── Frame state, fed by the headless backend (no Stride types) ──
    private LocomotionBlendWeights _loco = LocomotionBlend.FromSpeed(0f);
    private double _locoNormalizedTime; // 0..1 phase, advanced by the backend tick

    private int _activeMontageHash;
    private float _montageWeight;       // 0..1 overlay weight, computed by the slot state machine
    private double _montageNormalizedTime;
    private bool _montageActive;

    /// <summary>
    /// Create a builder bound to a Stride <see cref="AnimationComponent"/> and the
    /// three locomotion clips. Creates the clip evaluators from the component's
    /// <c>Blender</c> (GPU-bound) — call only when a real graphics device exists.
    /// </summary>
    public PerEntityBlendTreeBuilder(
        AnimationComponent animationComponent,
        AnimationClip idleClip,
        AnimationClip walkClip,
        AnimationClip runClip)
    {
        _animationComponent = animationComponent ?? throw new ArgumentNullException(nameof(animationComponent));
        _idleClip = idleClip ?? throw new ArgumentNullException(nameof(idleClip));
        _walkClip = walkClip ?? throw new ArgumentNullException(nameof(walkClip));
        _runClip = runClip ?? throw new ArgumentNullException(nameof(runClip));

        _idleEval = _animationComponent.Blender.CreateEvaluator(_idleClip);
        _walkEval = _animationComponent.Blender.CreateEvaluator(_walkClip);
        _runEval = _animationComponent.Blender.CreateEvaluator(_runClip);

        // Install ourselves as the custom blend tree builder (DD-1 §15.1).
        _animationComponent.BlendTreeBuilder = this;
    }

    /// <summary>
    /// Register a montage clip so it can be overlaid on the locomotion blend when
    /// its slot is active. Called by the asset bridge at registration time.
    /// </summary>
    public void RegisterMontageClip(int montageHash, AnimationClip clip)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        _montageClips[montageHash] = clip;
        _montageEvals[montageHash] = _animationComponent.Blender.CreateEvaluator(clip);
    }

    /// <summary>
    /// Push the latest headless-computed locomotion blend + phase into the builder.
    /// </summary>
    public void SetLocomotion(LocomotionBlendWeights weights, double normalizedTime)
    {
        _loco = weights;
        _locoNormalizedTime = normalizedTime;
    }

    /// <summary>
    /// Push the latest headless-computed montage overlay state into the builder.
    /// <paramref name="weight"/> 0 (or no registered clip) means no overlay this frame.
    /// </summary>
    public void SetMontage(int montageHash, float weight, double normalizedTime)
    {
        _activeMontageHash = montageHash;
        _montageWeight = weight;
        _montageNormalizedTime = normalizedTime;
        _montageActive = weight > 0f && _montageEvals.ContainsKey(montageHash);
    }

    /// <summary>
    /// Release every evaluator back to the <c>Blender</c>. Called when the entity
    /// is unregistered. GPU-bound cleanup.
    /// </summary>
    public void ReleaseEvaluators()
    {
        _animationComponent.Blender.ReleaseEvaluator(_idleEval);
        _animationComponent.Blender.ReleaseEvaluator(_walkEval);
        _animationComponent.Blender.ReleaseEvaluator(_runEval);
        foreach (var eval in _montageEvals.Values)
            _animationComponent.Blender.ReleaseEvaluator(eval);
        _montageEvals.Clear();
        _montageClips.Clear();
        if (ReferenceEquals(_animationComponent.BlendTreeBuilder, this))
            _animationComponent.BlendTreeBuilder = null;
    }

    private AnimationClipEvaluator EvalFor(LocomotionClip clip) => clip switch
    {
        LocomotionClip.Idle => _idleEval,
        LocomotionClip.Walk => _walkEval,
        LocomotionClip.Run => _runEval,
        _ => _idleEval,
    };

    private AnimationClip ClipFor(LocomotionClip clip) => clip switch
    {
        LocomotionClip.Idle => _idleClip,
        LocomotionClip.Walk => _walkClip,
        LocomotionClip.Run => _runClip,
        _ => _idleClip,
    };

    /// <summary>
    /// Stride animation-processor callback (DD-1 §15.1). Composes the frame's blend
    /// tree as a flattened operation stack: push the lower locomotion clip, push the
    /// upper, blend them by the locomotion factor; then if a montage slot is active,
    /// push the montage clip and blend it on top by the montage weight.
    /// </summary>
    public void BuildBlendTree(List<AnimationOperation> blendStack)
    {
        // Locomotion two-clip lerp (matches the template AnimationController).
        var lowerEval = EvalFor(_loco.LowerClip);
        var upperEval = EvalFor(_loco.UpperClip);
        var lowerClip = ClipFor(_loco.LowerClip);
        var upperClip = ClipFor(_loco.UpperClip);

        blendStack.Add(AnimationOperation.NewPush(
            lowerEval, TimeSpan.FromTicks((long)(_locoNormalizedTime * lowerClip.Duration.Ticks))));
        blendStack.Add(AnimationOperation.NewPush(
            upperEval, TimeSpan.FromTicks((long)(_locoNormalizedTime * upperClip.Duration.Ticks))));
        blendStack.Add(AnimationOperation.NewBlend(CoreAnimationOperation.Blend, _loco.Factor));

        // Montage overlay (Override compositing): blend the montage on top.
        if (_montageActive && _montageEvals.TryGetValue(_activeMontageHash, out var montageEval))
        {
            var montageClip = _montageClips[_activeMontageHash];
            blendStack.Add(AnimationOperation.NewPush(
                montageEval, TimeSpan.FromTicks((long)(_montageNormalizedTime * montageClip.Duration.Ticks))));
            blendStack.Add(AnimationOperation.NewBlend(CoreAnimationOperation.Blend, _montageWeight));
        }
    }
}
