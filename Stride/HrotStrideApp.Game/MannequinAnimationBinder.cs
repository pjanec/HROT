#nullable enable
using System;
using System.Collections.Generic;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.Stride.Animation;
using Hrot.Stride.Core;
using Stride.Animations;
using Stride.Core.Serialization.Contents;
using Stride.Engine;
using Entity = Fdp.Core.Entity;
using StrideEntity = Stride.Engine.Entity;

namespace HrotStrideApp;

/// <summary>
/// <b>STR-P4 live animation glue (BATCH-16 Fix A).</b> This is the missing live-path
/// connection that makes D5–D7 mannequins actually <i>animate</i> on the GPU.
///
/// <para>
/// The headless half was already complete before this batch:
/// <list type="bullet">
///   <item><see cref="StrideAnimationBridge"/> registers each mannequin with the
///     <see cref="StrideAnimationBackend"/> and pumps <c>SimVelocity</c> →
///     idle/walk/run blend + jump-montage slot state every frame.</item>
///   <item><see cref="StrideAnimationBackend.Tick"/> already pushes that per-entity blend +
///     montage state into a <see cref="PerEntityBlendTreeBuilder"/> <i>iff one is attached</i>
///     (see <see cref="StrideAnimationBackend.AttachBlendTreeBuilder"/>).</item>
///   <item><see cref="PerEntityBlendTreeBuilder"/> is a complete Stride
///     <c>IBlendTreeBuilder</c> that composes the frame's pose.</item>
/// </list>
/// What was missing: <b>nothing created the builder, loaded the clips, or attached it to the
/// mannequin's <c>AnimationComponent</c></b>, so the backend's <c>Builder</c> stayed null and
/// the skeleton never moved. This class closes that gap.
/// </para>
///
/// <para>
/// Each frame (driven from <c>EditorStrideSubsystem.Tick</c> / the Game's host loop, after the
/// animation bridge has reconciled registration) it reconciles the set of <i>bound</i>
/// mannequins against the live visual set:
/// <list type="number">
///   <item><b>Bind</b> a mannequin when it (a) has a Stride visual with a valid
///     <c>AnimationComponent</c> and (b) is registered with the backend (the bridge gave it a
///     handle). Binding loads the six locomotion/jump clips, constructs a
///     <see cref="PerEntityBlendTreeBuilder"/> bound to that <c>AnimationComponent</c>,
///     registers the three jump montage clips on it, and calls
///     <see cref="StrideAnimationBackend.AttachBlendTreeBuilder"/> so the backend drives it.</item>
///   <item><b>Unbind</b> a mannequin when its visual disappears (death/teardown). The builder's
///     evaluators are released back to the <c>Blender</c> via
///     <see cref="PerEntityBlendTreeBuilder.ReleaseEvaluators"/>.
///     (Backend-side <see cref="StrideAnimationBackend.UnregisterEntity"/> also releases the
///     builder it holds; this binder additionally releases when only the <i>visual</i> goes
///     away while the entity lingers.)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Testable / GPU-bound split.</b> Constructing a <see cref="PerEntityBlendTreeBuilder"/>
/// requires a real <c>AnimationComponent.Blender</c> (a <c>GraphicsDevice</c> resource), and
/// <c>Content.Load&lt;AnimationClip&gt;</c> requires the compiled asset pipeline — neither runs
/// headlessly. So the GPU-bound work (load clips, create + attach the builder, release it) is
/// behind <see cref="IMannequinBlendTreeInstaller"/>. This class owns only the engine-agnostic
/// <i>decision logic</i> (which mannequins to bind/unbind), which is unit-tested with a fake
/// installer. The real installer is <see cref="StrideMannequinBlendTreeInstaller"/>.
/// </para>
/// </summary>
public sealed class MannequinAnimationBinder
{
    private readonly StrideAnimationBackend _backend;
    private readonly StrideAnimationBridge _bridge;
    private readonly StrideVisualBindingSystem _visualBinding;
    private readonly IMannequinBlendTreeInstaller _installer;

    // Mannequins we have already bound (FDP entity → its installed builder token).
    private readonly Dictionary<Entity, object> _bound = new();

    // Reusable stale list so reconciliation does not allocate per frame.
    private readonly List<Entity> _stale = new(16);

    /// <summary>Number of mannequins currently bound (builder attached). Test/diagnostic seam.</summary>
    public int BoundCount => _bound.Count;

    /// <summary>
    /// Construct the binder.
    /// </summary>
    /// <param name="backend">The animation backend that drives attached builders (non-null).</param>
    /// <param name="bridge">The locomotion/montage bridge that owns backend handles (non-null).</param>
    /// <param name="visualBinding">The visual-binding system exposing live visuals (non-null).</param>
    /// <param name="installer">
    /// The GPU-bound clip-loader + builder installer (non-null). In the live app this is
    /// <see cref="StrideMannequinBlendTreeInstaller"/>; tests pass a fake.
    /// </param>
    public MannequinAnimationBinder(
        StrideAnimationBackend backend,
        StrideAnimationBridge bridge,
        StrideVisualBindingSystem visualBinding,
        IMannequinBlendTreeInstaller installer)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _visualBinding = visualBinding ?? throw new ArgumentNullException(nameof(visualBinding));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    }

    /// <summary>
    /// Reconcile bound mannequins against the live visual set. Call once per frame after the
    /// animation bridge has run (so freshly-spawned mannequins already have a backend handle)
    /// and after the visual binding has created their <c>AnimationComponent</c>.
    /// </summary>
    public void Reconcile()
    {
        // ── Pass 1: unbind mannequins whose visual disappeared or which the bridge dropped. ──
        _stale.Clear();
        foreach (var kvp in _bound)
        {
            var entity = kvp.Key;
            bool stillBindable =
                _visualBinding.Visuals.ContainsKey(entity) &&
                _bridge.TryGetHandle(entity, out _);
            if (!stillBindable)
                _stale.Add(entity);
        }
        foreach (var entity in _stale)
        {
            _installer.Release(_bound[entity]);
            _bound.Remove(entity);
        }

        // ── Pass 2: bind newly-appeared mannequins that have both a visual + a backend handle. ──
        foreach (var kvp in _visualBinding.Visuals)
        {
            var entity = kvp.Key;
            if (_bound.ContainsKey(entity))
                continue;
            if (!_bridge.TryGetHandle(entity, out var handle))
                continue; // not an animated mannequin (or bridge has not registered it yet)

            // Install the GPU-bound builder for this entity's AnimationComponent. The installer
            // returns a token (the builder) we keep so we can release it on unbind. If the visual
            // has no AnimationComponent (e.g. a non-skinned model), the installer returns null and
            // we skip — there is nothing to drive.
            var token = _installer.Install(handle, kvp.Value.VisualHandle, _backend);
            if (token != null)
                _bound[entity] = token;
        }
    }

    /// <summary>Release every bound builder. Call on shutdown to avoid leaking GPU evaluators.</summary>
    public void ReleaseAll()
    {
        foreach (var kvp in _bound)
            _installer.Release(kvp.Value);
        _bound.Clear();
    }
}

/// <summary>
/// GPU-bound seam for <see cref="MannequinAnimationBinder"/>: loads the locomotion/jump
/// <c>AnimationClip</c>s, constructs the per-entity <see cref="PerEntityBlendTreeBuilder"/>,
/// attaches it to the backend, and releases it on teardown. Abstracted so the binder's
/// decision logic is unit-testable without a <c>GraphicsDevice</c> or the asset pipeline.
/// </summary>
public interface IMannequinBlendTreeInstaller
{
    /// <summary>
    /// Create + attach a blend-tree builder for the mannequin identified by
    /// <paramref name="handle"/> whose Stride visual is <paramref name="visualHandle"/>.
    /// Returns an opaque token (passed back to <see cref="Release"/>) on success, or
    /// <c>null</c> if the visual has no usable <c>AnimationComponent</c> (nothing to drive).
    /// A missing/un-compiled clip must throw (fail loud) — never silently no-op.
    /// </summary>
    object? Install(AnimationBackendHandle handle, object visualHandle, StrideAnimationBackend backend);

    /// <summary>Release the builder/evaluators created by a prior <see cref="Install"/>.</summary>
    void Release(object token);
}

/// <summary>
/// The live GPU implementation of <see cref="IMannequinBlendTreeInstaller"/>. Loads the six
/// mannequin clips through a <see cref="ContentManager"/> (<c>Content.Load&lt;AnimationClip&gt;</c>),
/// builds a <see cref="PerEntityBlendTreeBuilder"/> on the entity's <c>AnimationComponent</c>,
/// registers the three jump montage clips on it (keyed by the same
/// <see cref="StableIdHasher.ComputeMontageAssetId"/> hashes the bridge uses), and attaches it to
/// the backend. Requires a running Stride app (GPU + compiled assets); cannot run headlessly.
///
/// <para><b>Loud failure (STR-D10 parity):</b> the six clip URLs are
/// <c>Animations/Idle | Walk | Run | Jump_Start | Jump_Loop | Jump_End</c>. A missing or
/// un-compiled clip throws from <c>Content.Load</c>; we wrap it in an
/// <see cref="InvalidOperationException"/> naming the failed URL and rethrow so the window
/// crashes loudly rather than spawning a silently-static mannequin.</para>
/// </summary>
public sealed class StrideMannequinBlendTreeInstaller : IMannequinBlendTreeInstaller
{
    // Locomotion clip URLs (DD-1 §12; carried on the mannequin's CharacterAnimationDefDto).
    private const string IdleClipUrl = "Animations/Idle";
    private const string WalkClipUrl = "Animations/Walk";
    private const string RunClipUrl = "Animations/Run";

    // Jump-montage clip URLs (off-mesh-link traversal montages).
    private const string JumpStartClipUrl = "Animations/Jump_Start";
    private const string JumpLoopClipUrl = "Animations/Jump_Loop";
    private const string JumpEndClipUrl = "Animations/Jump_End";

    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    private readonly ContentManager _content;

    // Clips are process-wide immutable assets — load once and share across all mannequins.
    // The per-entity AnimationClipEvaluators (Blender-bound, GPU) are created per builder.
    private AnimationClip? _idle, _walk, _run, _jumpStart, _jumpLoop, _jumpEnd;
    private bool _clipsLoaded;

    /// <param name="content">The running game's <c>Content</c> manager (non-null).</param>
    public StrideMannequinBlendTreeInstaller(ContentManager content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <inheritdoc/>
    public object? Install(AnimationBackendHandle handle, object visualHandle, StrideAnimationBackend backend)
    {
        if (backend == null) throw new ArgumentNullException(nameof(backend));

        // The visual handle is the Stride Entity created by StrideVisualFactory. Skinned
        // mannequins carry an AnimationComponent (added when SkeletonAssetRef is non-empty).
        if (visualHandle is not StrideEntity entity)
            return null;

        var animationComponent = entity.Get<AnimationComponent>();
        if (animationComponent == null)
            return null; // non-skinned visual — nothing to animate.

        // ── ISSUE-2 FIX: Blender is initialised by Stride's AnimationProcessor, which runs
        // once per Stride rendering frame — typically the frame AFTER the AnimationComponent
        // is first added to the scene. If Install() is called in the same frame as the visual
        // was created (our normal path), the Blender may still be null. Returning null here
        // causes MannequinAnimationBinder.Reconcile() to skip this entity and retry next frame
        // (the entity stays out of _bound so Reconcile() will try again). This ensures the
        // builder is attached on the very first frame the Blender is ready — usually 1-2 frames
        // after spawn — rather than many seconds later.
        if (animationComponent.Blender == null)
        {
            Log.Debug(
                "[StrideMannequinBlendTreeInstaller] Blender not yet initialised for '{0}' — " +
                "will retry next frame (AnimationProcessor initialises Blender on first render).",
                entity.Name);
            return null;
        }

        EnsureClipsLoaded();

        // Build the per-entity blend tree (creates GPU evaluators from the Blender and installs
        // itself as the AnimationComponent's BlendTreeBuilder).
        var builder = new PerEntityBlendTreeBuilder(animationComponent, _idle!, _walk!, _run!);

        // Register the three jump montage clips, keyed by the SAME montage-asset-id hashes the
        // bridge/backend use (StableIdHasher), so an active slot-0 montage resolves to the right
        // clip when the backend pushes SetMontage.
        builder.RegisterMontageClip(StableIdHasher.ComputeMontageAssetId("Jump_Start"), _jumpStart!);
        builder.RegisterMontageClip(StableIdHasher.ComputeMontageAssetId("Jump_Loop"), _jumpLoop!);
        builder.RegisterMontageClip(StableIdHasher.ComputeMontageAssetId("Jump_End"), _jumpEnd!);

        // Hand the builder to the backend; from now on Tick() pumps the per-frame locomotion
        // blend + montage overlay into it (TryGetLocomotionBlend / TryGetMontageOverlay state).
        backend.AttachBlendTreeBuilder(handle, builder);

        Log.Info("[anim] Bound PerEntityBlendTreeBuilder to mannequin visual '{0}' (backend idx={1}).",
            entity.Name, handle.Index);

        return builder;
    }

    /// <inheritdoc/>
    public void Release(object token)
    {
        if (token is PerEntityBlendTreeBuilder builder)
            builder.ReleaseEvaluators();
    }

    /// <summary>
    /// Loads the six shared clips on first use. A missing/un-compiled clip fails loud: the
    /// <c>Content.Load</c> exception is wrapped with the failing URL and rethrown.
    /// </summary>
    private void EnsureClipsLoaded()
    {
        if (_clipsLoaded)
            return;

        _idle = Load(IdleClipUrl);
        _walk = Load(WalkClipUrl);
        _run = Load(RunClipUrl);
        _jumpStart = Load(JumpStartClipUrl);
        _jumpLoop = Load(JumpLoopClipUrl);
        _jumpEnd = Load(JumpEndClipUrl);

        _clipsLoaded = true;
    }

    private AnimationClip Load(string url)
    {
        try
        {
            return _content.Load<AnimationClip>(url);
        }
        catch (Exception ex)
        {
            // STR-D10 parity: fail loud — a missing clip is always a pipeline bug at this stage.
            var message =
                $"[StrideMannequinBlendTreeInstaller] FATAL: Content.Load<AnimationClip> failed for '{url}'. " +
                $"Ensure the clip is compiled in the HrotStrideApp asset pipeline (Assets/Animations). " +
                $"Inner exception: {ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(message);
            Console.Error.WriteLine(message);
            Log.Error(message);
            throw new InvalidOperationException(message, ex);
        }
    }
}
