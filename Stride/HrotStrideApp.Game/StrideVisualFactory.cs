#nullable enable
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;

namespace HrotStrideApp;

/// <summary>
/// Concrete GPU implementation of <see cref="IStrideVisualFactory"/>.
///
/// <para>
/// <b>Requires a live <c>GraphicsDevice</c></b> — this class uses <c>Content.Load</c> to
/// load Stride assets (Model, Skeleton) and attaches <see cref="ModelComponent"/> /
/// <see cref="AnimationComponent"/> to new Stride entities.  It cannot run headlessly.
/// </para>
///
/// <para>
/// <b>STR-D4 / real GPU bring-up attempt (BATCH-03):</b>
/// The concrete factory is wired into <see cref="EditorStrideSubsystem"/> and exercised
/// by the T8 real-GPU smoke.  See <c>BATCH-03-REPORT.md</c> for the outcome
/// (whether a <see cref="ModelComponent"/> + procedural capsule actually appeared, or what
/// blocked it).
/// </para>
///
/// <para>
/// <b>[VERIFY] Stride 4.2.1.2487 asset-load and instantiate API:</b>
/// <list type="bullet">
///   <item><c>ContentManager.Load&lt;Model&gt;(url)</c> is the public synchronous asset-load API,
///     exposed on <see cref="Game.Content"/> (inherited by <see cref="StrideHrotGame"/>).
///     The URL must match an asset registered in the project's asset-compilation pipeline
///     (i.e. defined in the <c>Assets/</c> folder and compiled by
///     <c>Stride.Core.Assets.CompilerApp</c>).</item>
///   <item><c>new Entity()</c> creates a detached Stride entity; it is added to the scene
///     via <c>Scene.Entities.Add(entity)</c>.</item>
///   <item><c>entity.Add(new ModelComponent { Model = model })</c> attaches the model
///     (equivalent to adding a component in Game Studio).</item>
///   <item>For skinned models, <c>entity.Add(new AnimationComponent())</c> enables animation
///     playback; the skeleton is embedded in the <see cref="Model"/> asset itself (the
///     <c>SkeletonAssetRef</c> is used only to reference the skeleton separately for
///     retargeting — when the mannequin model already embeds its own skeleton, this field
///     can be left empty or used as a hint).</item>
///   <item>Procedural primitives (capsule, box) are created via
///     <c>Stride.Rendering.ProceduralModels</c> helpers when available, or via a
///     <c>ModelComponent</c> with a programmatically-constructed mesh. For P0 (bring-up)
///     a simple colored material-less entity suffices — the visual proves spawn/destroy
///     reconciliation; fidelity is Phase 4+.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Threading:</b> must be called on the Stride game thread. All calls originate from
/// <see cref="StrideHostLoopDriver.AdvanceFrame"/> which runs on the host thread, satisfying
/// the single-thread invariant (design §8.3).
/// </para>
/// </summary>
public sealed class StrideVisualFactory : IStrideVisualFactory
{
    private readonly Game   _game;
    private readonly Scene  _scene;

    /// <summary>
    /// Constructs the factory bound to the running game and the target scene.
    /// </summary>
    /// <param name="game">The running <see cref="StrideHrotGame"/> (provides <c>Content</c>).</param>
    /// <param name="scene">The Stride <see cref="Scene"/> to which new entities are added.</param>
    public StrideVisualFactory(Game game, Scene scene)
    {
        _game  = game  ?? throw new ArgumentNullException(nameof(game));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <inheritdoc/>
    public object CreateModelVisual(
        string modelRef,
        string skeletonRef,
        float scale,
        System.Numerics.Vector3 offsetFdp,
        in SimTransform initialPose)
    {
        // [VERIFY] Content.Load<Model>(url) — synchronous load of a compiled Stride model asset.
        // The URL must match a compiled asset in the HrotStrideApp Assets/ folder.
        // If the asset is not found, Content.Load throws an exception.
        //
        // STR-D10 RESOLVED (BATCH-10): Asset-load failures are LOUD — we rethrow so the
        // developer sees the exception immediately rather than silently getting invisible
        // placeholder entities.  A missing/miscompiled asset is always a bug at this stage;
        // the prior "swallow + placeholder" behaviour hid real problems.
        //
        // What the human sees on asset failure:
        //   Stride window crashes/closes immediately with an unhandled exception; the
        //   exception message includes the failed asset URL (e.g. "Models/mannequinModel").
        //   Fix: ensure the Stride asset pipeline has compiled the model (check the
        //   HrotStrideApp.Game Assets/ folder and re-run the asset compiler).
        Model model;
        try
        {
            model = _game.Content.Load<Model>(modelRef);
        }
        catch (Exception ex)
        {
            // STR-D10: Fail loud — log the full asset URL + exception details, then rethrow.
            // Do NOT swallow or fall back to a silent placeholder; missing assets must be fixed.
            var message = $"[StrideVisualFactory] FATAL: Content.Load<Model> failed for asset '{modelRef}'. " +
                          $"Ensure the asset is compiled in the HrotStrideApp asset pipeline. " +
                          $"Inner exception: {ex.GetType().Name}: {ex.Message}";
            // Log to all standard channels before rethrowing.
            System.Diagnostics.Debug.WriteLine(message);
            Console.Error.WriteLine(message);
            throw new InvalidOperationException(message, ex);
        }

        var entity = new global::Stride.Engine.Entity($"Visual_{modelRef}");

        // Apply scale via the entity transform.
        entity.Transform.Scale = new Stride.Core.Mathematics.Vector3(scale, scale, scale);

        // Attach ModelComponent with the loaded model.
        // [VERIFY] ModelComponent constructor + property assignment, Stride 4.2.1.2487.
        entity.Add(new ModelComponent { Model = model });

        // For skinned models, attach AnimationComponent.
        // The AnimationComponent drives the blend tree; the skeleton is embedded in the Model asset.
        // [VERIFY] AnimationComponent usage — adding it is sufficient; blend-tree wiring is P4.
        if (!string.IsNullOrEmpty(skeletonRef))
        {
            entity.Add(new AnimationComponent());
        }

        // Place at the swizzled initial pose.
        ApplyPose(entity, scale, offsetFdp, in initialPose);

        // Register with the scene so it renders.
        _scene.Entities.Add(entity);

        return entity;
    }

    /// <inheritdoc/>
    public object CreateProceduralVisual(
        CollisionShapeKind kind,
        ShapeDims dims,
        float scale,
        System.Numerics.Vector3 offsetFdp,
        in SimTransform initialPose)
    {
        // P0 procedural primitive: create a named entity in the scene.
        // A full mesh primitive (capsule, box) requires ProceduralModels or custom mesh data.
        // For the P0 bring-up smoke, a bare entity with a debug label is sufficient to
        // prove spawn/destroy reconciliation; the ModelComponent will be wired in P1+.
        //
        // [VERIFY] Stride.Rendering.ProceduralModels (capsule/box) availability in 4.2.1.2487:
        // Stride.Physics provides CapsuleColliderShape for physics, but visual primitives
        // live in Stride.Rendering.ProceduralModels which may or may not be available.
        // For P0 we use a bare entity; this is documented in BATCH-03-REPORT.md.

        var label = kind switch
        {
            CollisionShapeKind.Capsule     => $"Capsule_r{dims.Radius:F2}_h{dims.Height:F2}",
            CollisionShapeKind.OrientedBox => $"Box_{dims.HalfX:F2}x{dims.HalfY:F2}x{dims.HalfZ:F2}",
            CollisionShapeKind.Sphere      => $"Sphere_r{dims.Radius:F2}",
            _                              => $"Primitive_{kind}",
        };

        var entity = new global::Stride.Engine.Entity(label);
        entity.Transform.Scale = new Stride.Core.Mathematics.Vector3(scale, scale, scale);

        // Place at the swizzled initial pose.
        ApplyPose(entity, scale, offsetFdp, in initialPose);

        _scene.Entities.Add(entity);

        return entity;
    }

    /// <inheritdoc/>
    public void UpdatePose(object visualHandle, in SimTransform pose)
    {
        if (visualHandle is not global::Stride.Engine.Entity entity)
            return;   // Guard: handle must be a Stride Entity.

        // Apply the swizzled transform.
        // Scale and offset are baked into the entity from creation; only position/rotation change.
        entity.Transform.Position = FdpStrideTransform.ToStridePosition(pose.Position);
        entity.Transform.Rotation = FdpStrideTransform.ToStrideRotation(pose.Rotation);
    }

    /// <inheritdoc/>
    public void Destroy(object visualHandle)
    {
        if (visualHandle is not global::Stride.Engine.Entity entity)
            return;

        // Remove from scene (prevents further rendering) then unload the entity.
        // [VERIFY] Scene.Entities.Remove — confirmed in Stride.Engine.Scene API.
        _scene.Entities.Remove(entity);

        // Dispose any unmanaged Stride resources held by the entity's components.
        entity.Dispose();
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Applies the FDP-to-Stride swizzled transform to the entity.
    /// <paramref name="offsetFdp"/> is an FDP-space local render offset from the body origin;
    /// it is added to the swizzled world position.
    /// </summary>
    private static void ApplyPose(
        global::Stride.Engine.Entity entity,
        float scale,
        System.Numerics.Vector3 offsetFdp,
        in SimTransform pose)
    {
        var stridePos  = FdpStrideTransform.ToStridePosition(pose.Position);
        var strideOff  = FdpStrideTransform.ToStridePosition(offsetFdp);
        var strideRot  = FdpStrideTransform.ToStrideRotation(pose.Rotation);

        entity.Transform.Position = stridePos + strideOff;
        entity.Transform.Rotation = strideRot;
    }
}
