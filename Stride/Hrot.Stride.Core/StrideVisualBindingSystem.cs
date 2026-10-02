using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

/// <summary>
/// Manages the Stride-side visual entity set for FDP entities.
///
/// <para>
/// Implements the two-pass differential sync pattern from
/// <c>SyncFdpToStrideScript</c> (design §7 "Pass A — reconcile Stride visual entity set"):
/// <list type="number">
///   <item>Pass 1 (destructions): iterate the visual dictionary; call
///     <see cref="IStrideVisualFactory.Destroy"/> and remove entries for dead entities.</item>
///   <item>Pass 2 (creations): query all entities with <see cref="SimTransform"/> +
///     <see cref="TkbIdentity"/>; upsert — create visuals for newcomers, update poses for
///     entities already in the set.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Descriptor resolution path:</b>
/// <c>entity → TkbIdentity.TkbType → TkbDatabase.TryGetByType(tkbType, out template) →
///   template.GetDescriptor&lt;StrideRenderModelDefDto&gt;()</c>.
/// If the template is not found, or the template has no <c>StrideRenderModelDefDto</c>,
/// the entity is silently skipped (no visual, no throw).
/// </para>
///
/// <para>
/// <b>Model vs procedural selection:</b>
/// <list type="bullet">
///   <item><c>ModelAssetRef</c> non-empty → <see cref="IStrideVisualFactory.CreateModelVisual"/>.</item>
///   <item><c>ModelAssetRef</c> empty → <see cref="IStrideVisualFactory.CreateProceduralVisual"/>
///     with <see cref="ShapeKind"/> and resolved <see cref="ShapeDims"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>"0 =&gt; default" shape-sizing rules</b> (applied before calling the factory):
/// <list type="bullet">
///   <item><c>ShapeRadius == 0</c> → use <c>PhysicsCollider.Radius</c> (from ECS entity).</item>
///   <item><c>BoxHalfX == 0</c> → use <c>VehicleParametersDto.Length / 2</c> (from TKB descriptor).</item>
///   <item><c>BoxHalfY == 0</c> → use <c>VehicleParametersDto.Width / 2</c> (from TKB descriptor).</item>
///   <item><c>BoxHalfZ == 0</c> → fall back to <c>ShapeHeight / 2</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scale/Offset:</b> <c>StrideRenderModelDefDto.Scale</c> and <c>Offset{X,Y,Z}</c>
/// are forwarded to the factory as-is (in FDP coordinates; the factory applies the
/// swizzle).
/// </para>
///
/// <para>
/// <b>Threading:</b> must always be called on the single host thread (design §8.3).
/// </para>
/// </summary>
public sealed class StrideVisualBindingSystem
{
    private readonly IStrideVisualFactory _factory;
    private readonly ITkbDatabase         _tkbDb;

    // Visual set — keyed by FDP entity.
    // Not in the ECS repo because Stride-layer objects can't live in blittable ECS slots.
    private readonly Dictionary<Entity, StrideVisualReference> _visuals = new();

    // Reusable stale-key list (mirrors SyncFdpToStrideScript pattern — never replaced).
    private readonly List<Entity> _staleEntities = new(64);

    /// <summary>Provides read-only access to the live visual set (for tests / diagnostics).</summary>
    public IReadOnlyDictionary<Entity, StrideVisualReference> Visuals => _visuals;

    /// <summary>
    /// Constructs the binding system.
    /// </summary>
    /// <param name="factory">
    /// Visual factory (GPU or recording fake). The system owns no GPU resources itself.
    /// </param>
    /// <param name="tkbDb">TKB database used to resolve <see cref="StrideRenderModelDefDto"/>.</param>
    public StrideVisualBindingSystem(IStrideVisualFactory factory, ITkbDatabase tkbDb)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _tkbDb   = tkbDb   ?? throw new ArgumentNullException(nameof(tkbDb));
    }

    /// <summary>
    /// Synchronises the Stride visual set with the live FDP entity set.
    /// Call once per frame from the host-loop sync script (after the FDP kernel tick).
    ///
    /// <para>Pass 1 removes visuals for dead entities; Pass 2 upserts visuals for
    /// all live entities that have both <see cref="SimTransform"/> and
    /// <see cref="TkbIdentity"/>.</para>
    /// </summary>
    /// <param name="world">The shared ECS world (simulation layer).</param>
    public void Sync(EntityRepository world)
    {
        var view = (ISimulationView)world;

        // ── Pass 1: destructions ─────────────────────────────────────────────
        // Collect stale keys into a pre-allocated list; remove them after iteration.
        _staleEntities.Clear();
        foreach (var kvp in _visuals)
        {
            if (!world.IsAlive(kvp.Key))
                _staleEntities.Add(kvp.Key);
        }
        foreach (var stale in _staleEntities)
        {
            _factory.Destroy(_visuals[stale].VisualHandle);
            _visuals.Remove(stale);
        }

        // ── Pass 2: creations + pose updates ────────────────────────────────
        // Query all entities that have SimTransform and TkbIdentity.
        var query = view.Query()
            .With<SimTransform>()
            .With<TkbIdentity>()
            .Build();

        foreach (var entity in query)
        {
            ref readonly var xform    = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var identity = ref view.GetComponentRO<TkbIdentity>(entity);

            if (_visuals.TryGetValue(entity, out var existing))
            {
                // Already spawned — just update the pose.
                _factory.UpdatePose(existing.VisualHandle, in xform);
                continue;
            }

            // New entity — resolve the TKB descriptor.
            var visual = TryCreateVisual(entity, in xform, in identity, view);
            if (visual != null)
                _visuals[entity] = visual;
            // If no descriptor → silently skip (no throw, no visual).
        }
    }

    /// <summary>
    /// Synchronises the Stride visual <b>existence set</b> only — creates visuals for
    /// newly-appeared entities and tears down visuals for dead entities, but does NOT
    /// call <see cref="IStrideVisualFactory.UpdatePose"/>.
    ///
    /// <para>
    /// Used by <see cref="SplitAuthorityStrideSyncScript"/> (Pass A) so that the authority-
    /// forked sync can manage existence separately from the transform-direction decision
    /// (Pass B). The caller is responsible for calling <c>UpdatePose</c> on the appropriate
    /// entities afterwards (non-owned entities only in Pass B).
    /// </para>
    /// </summary>
    /// <param name="world">The shared ECS world (simulation layer).</param>
    public void SyncExistenceOnly(EntityRepository world)
    {
        var view = (ISimulationView)world;

        // ── Pass 1: destructions ─────────────────────────────────────────────
        _staleEntities.Clear();
        foreach (var kvp in _visuals)
        {
            if (!world.IsAlive(kvp.Key))
                _staleEntities.Add(kvp.Key);
        }
        foreach (var stale in _staleEntities)
        {
            _factory.Destroy(_visuals[stale].VisualHandle);
            _visuals.Remove(stale);
        }

        // ── Pass 2: creations only (no pose updates) ─────────────────────────
        var query = view.Query()
            .With<SimTransform>()
            .With<TkbIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (_visuals.ContainsKey(entity))
                continue; // already exists — no pose update here

            ref readonly var xform    = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var identity = ref view.GetComponentRO<TkbIdentity>(entity);

            var visual = TryCreateVisual(entity, in xform, in identity, view);
            if (visual != null)
                _visuals[entity] = visual;
        }
    }

    /// <summary>
    /// Tears down all live visuals and clears the visual set.
    /// Call on shutdown to avoid leaking Stride-side resources.
    /// </summary>
    public void DestroyAll()
    {
        foreach (var kvp in _visuals)
            _factory.Destroy(kvp.Value.VisualHandle);
        _visuals.Clear();
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private StrideVisualReference? TryCreateVisual(
        Entity entity,
        in SimTransform xform,
        in TkbIdentity identity,
        ISimulationView view)
    {
        // Resolve the template.
        if (!_tkbDb.TryGetByType(identity.TkbType, out var template))
            return null;   // Unknown TKB type — skip silently.

        // Resolve the render/model descriptor.
        var def = template.GetDescriptor<StrideRenderModelDefDto>();
        if (def == null)
            return null;   // No Stride visual definition for this class — skip silently.

        // Resolve VehicleParametersDto from the TKB template (for box-half defaults).
        // This is a template-level descriptor, not an ECS component.
        var vehicleDto = template.GetDescriptor<VehicleParametersDto>();

        var offsetFdp = new System.Numerics.Vector3(def.OffsetX, def.OffsetY, def.OffsetZ);

        if (!string.IsNullOrEmpty(def.ModelAssetRef))
        {
            // ── Model path ───────────────────────────────────────────────────
            var dims = ResolveShapeDims(def, vehicleDto, entity, view);

            var handle = _factory.CreateModelVisual(
                def.ModelAssetRef,
                def.SkeletonAssetRef,
                def.Scale,
                offsetFdp,
                in xform);

            return new StrideVisualReference(
                handle,
                def.ShapeKind,
                dims,
                isModelVisual: true);
        }
        else
        {
            // ── Procedural fallback ──────────────────────────────────────────
            var dims = ResolveShapeDims(def, vehicleDto, entity, view);

            var handle = _factory.CreateProceduralVisual(
                def.ShapeKind,
                dims,
                def.Scale,
                offsetFdp,
                in xform);

            return new StrideVisualReference(
                handle,
                def.ShapeKind,
                dims,
                isModelVisual: false);
        }
    }

    /// <summary>
    /// Applies the "0 =&gt; default" rules from <see cref="StrideRenderModelDefDto"/> doc
    /// to produce a concrete <see cref="ShapeDims"/>.
    ///
    /// <para>Shape-default sources (design §6.5):</para>
    /// <list type="bullet">
    ///   <item><c>ShapeRadius == 0</c> → <c>PhysicsCollider.Radius</c> (ECS component on entity).</item>
    ///   <item><c>BoxHalfX == 0</c> → <c>VehicleParametersDto.Length / 2</c> (TKB template descriptor).</item>
    ///   <item><c>BoxHalfY == 0</c> → <c>VehicleParametersDto.Width / 2</c> (TKB template descriptor).</item>
    ///   <item><c>BoxHalfZ == 0</c> → <c>ShapeHeight / 2</c>.</item>
    /// </list>
    /// </summary>
    private static ShapeDims ResolveShapeDims(
        StrideRenderModelDefDto def,
        VehicleParametersDto?   vehicleDto,
        Entity                  entity,
        ISimulationView         view)
    {
        switch (def.ShapeKind)
        {
            case CollisionShapeKind.Capsule:
            case CollisionShapeKind.Cylinder:
            case CollisionShapeKind.Sphere:
            {
                float radius = def.ShapeRadius;
                if (radius == 0f && view.HasComponent<PhysicsCollider>(entity))
                {
                    ref readonly var collider = ref view.GetComponentRO<PhysicsCollider>(entity);
                    radius = collider.Radius;
                }
                return ShapeDims.Capsule(radius, def.ShapeHeight);
            }

            case CollisionShapeKind.OrientedBox:
            {
                float halfX = def.BoxHalfX;
                if (halfX == 0f && vehicleDto != null)
                    halfX = vehicleDto.Length / 2f;

                float halfY = def.BoxHalfY;
                if (halfY == 0f && vehicleDto != null)
                    halfY = vehicleDto.Width / 2f;

                float halfZ = def.BoxHalfZ;
                if (halfZ == 0f)
                    halfZ = def.ShapeHeight / 2f;

                return ShapeDims.Box(halfX, halfY, halfZ);
            }

            default:
                // None / MeshFromModel — return a zero dims struct.
                return default;
        }
    }
}
