#nullable enable
using System;
using System.Collections.Generic;
using Hrot.Stride.Core;
using NLog;
using Stride.Engine;
using Stride.Physics;
using SMath = Stride.Core.Mathematics;

namespace HrotStrideApp;

/// <summary>
/// Concrete <see cref="ISceneGeometrySource"/> that extracts triangle geometry from a
/// loaded Stride scene's <see cref="StaticColliderComponent"/>s (BATCH-18, STR-D19).
///
/// <para>
/// <b>Coordinate output.</b>
/// Vertices are output in navmesh-query space: X=East, Y=altitude(up), Z=North —
/// identical to Stride world space. No additional swizzle is required.
/// </para>
///
/// <para>
/// <b>Shape handling:</b>
/// <list type="bullet">
///   <item><b>BoxColliderShapeDesc</b> — exact: 8 world-space corners computed from
///     half-extents + shape LocalOffset/LocalRotation + entity WorldMatrix → 12 triangles
///     (6 faces × 2). Handles axis-aligned AND rotated boxes. Delegates to
///     <see cref="BoxGeometryHelper.ExtractBoxTriangles"/>.</item>
///   <item><b>All other shapes</b> — conservative AABB fallback: the world-space AABB
///     estimated from the entity's world-matrix column magnitudes is used as a box via
///     <see cref="BoxGeometryHelper.AabbToBox"/>. A Warn is logged per such shape.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Floor guard.</b>
/// After collecting all collider triangles, if no vertex has Y &lt; <c>FloorGuardThresholdY</c>
/// a synthetic large floor quad at Y=0 is injected so DotRecast has a walkable surface.
/// </para>
///
/// <para>
/// <b>Triangle winding.</b>
/// Up-facing (top) faces are wound CCW when viewed from above so their normal points +Y
/// (walkable for DotRecast). See <see cref="BoxGeometryHelper.ExtractBoxTriangles"/> for details.
/// </para>
///
/// <para>
/// <b>Why this class is not tested headlessly.</b>
/// This class walks a live Stride <see cref="Scene"/> requiring a running
/// <c>PhysicsProcessor</c>. The math helpers live in <see cref="BoxGeometryHelper"/>
/// (in <c>Hrot.Stride.Core</c>) and are fully tested headlessly from
/// <c>StrideSceneGeometryExtractorTests</c>.
/// </para>
/// </summary>
public sealed class StrideSceneGeometrySource : ISceneGeometrySource
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Vertices below this Y are considered "floor-level". If none exist we inject a floor.
    private const float FloorGuardThresholdY = 0.5f;

    // Half-extent of the synthetic floor quad (metres), centred at origin.
    private const float SyntheticFloorHalfExtent = 60f;

    private readonly Scene _scene;

    /// <param name="scene">The loaded Stride scene to extract geometry from.</param>
    public StrideSceneGeometrySource(Scene scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <inheritdoc/>
    public bool TryGetTriangles(out float[] verts, out int[] indices)
    {
        var vertList  = new List<float>();
        var indexList = new List<int>();

        int boxCount  = 0;
        int aabbCount = 0;

        // Walk all root entities recursively.
        foreach (var entity in _scene.Entities)
            CollectFromEntity(entity, vertList, indexList, ref boxCount, ref aabbCount);

        // Floor guard: check whether any vertex is near ground level (Y < threshold).
        bool hasFloor = false;
        for (int i = 1; i < vertList.Count; i += 3) // Y component = indices 1, 4, 7, …
        {
            if (vertList[i] < FloorGuardThresholdY)
            {
                hasFloor = true;
                break;
            }
        }

        string floorSource;
        if (!hasFloor)
        {
            float h = SyntheticFloorHalfExtent;
            AddGroundQuad(vertList, indexList, -h, h, -h, h, 0f);
            floorSource = $"SYNTHETIC floor injected (no collider vertex Y < {FloorGuardThresholdY:F2})";
        }
        else
        {
            floorSource = "floor from arena colliders";
        }

        int totalTris = indexList.Count / 3;
        Log.Info(
            "[StrideSceneGeometrySource] Extracted: {0} box-exact + {1} AABB-fallback colliders, " +
            "{2} triangles total. Floor: {3}.",
            boxCount, aabbCount, totalTris, floorSource);

        if (vertList.Count == 0 || indexList.Count == 0)
        {
            Log.Warn("[StrideSceneGeometrySource] No geometry extracted — navmesh bake will fail.");
            verts   = Array.Empty<float>();
            indices = Array.Empty<int>();
            return false;
        }

        verts   = vertList.ToArray();
        indices = indexList.ToArray();
        return true;
    }

    // ── Recursive entity walk ────────────────────────────────────────────────────

    private static void CollectFromEntity(
        Entity      entity,
        List<float> vertList,
        List<int>   indexList,
        ref int     boxCount,
        ref int     aabbCount)
    {
        var collider = entity.Get<StaticColliderComponent>();
        if (collider != null)
            CollectStaticCollider(entity, collider, vertList, indexList, ref boxCount, ref aabbCount);

        foreach (var childTransform in entity.Transform.Children)
            CollectFromEntity(childTransform.Entity, vertList, indexList, ref boxCount, ref aabbCount);
    }

    private static void CollectStaticCollider(
        Entity                  entity,
        StaticColliderComponent collider,
        List<float>             vertList,
        List<int>               indexList,
        ref int                 boxCount,
        ref int                 aabbCount)
    {
        // Ensure the world matrix is current for this entity.
        entity.Transform.UpdateWorldMatrix();
        var worldMatrix = entity.Transform.WorldMatrix;

        foreach (var shapeDesc in collider.ColliderShapes)
        {
            if (shapeDesc is BoxColliderShapeDesc boxDesc)
            {
                // Box-exact path.
                // VERIFIED (Stride.Physics 4.2.1.2487):
                //   BoxColliderShapeDesc.Size       : Vector3    — full extents (width, height, depth).
                //   IColliderShapeDesc.LocalOffset  : Vector3    — shape centre in entity-local space.
                //   IColliderShapeDesc.LocalRotation: Quaternion — shape rotation in entity-local space.
                var halfExtents = new SMath.Vector3(
                    boxDesc.Size.X * 0.5f,
                    boxDesc.Size.Y * 0.5f,
                    boxDesc.Size.Z * 0.5f);

                // Shape-local matrix: rotation first, then translation.
                SMath.Matrix shapeLocalMatrix =
                    SMath.Matrix.RotationQuaternion(boxDesc.LocalRotation) *
                    SMath.Matrix.Translation(boxDesc.LocalOffset);

                // Full shape-to-world matrix.
                SMath.Matrix shapeWorldMatrix = shapeLocalMatrix * worldMatrix;

                BoxGeometryHelper.ExtractBoxTriangles(shapeWorldMatrix, halfExtents, vertList, indexList);
                boxCount++;
            }
            else
            {
                // AABB fallback for all non-box shapes.
                string shapeName = shapeDesc?.GetType().Name ?? "unknown";
                Log.Warn(
                    "[StrideSceneGeometrySource] Non-box shape '{0}' on entity '{1}' — " +
                    "AABB fallback used (navmesh will be conservative).",
                    shapeName, entity.Name ?? "(unnamed)");

                BoxGeometryHelper.AabbToBox(worldMatrix, vertList, indexList);
                aabbCount++;
            }
        }
    }

    /// <summary>
    /// Appends a flat ground quad at altitude <paramref name="y"/> as two CCW triangles
    /// (wound from above → +Y normal = walkable for DotRecast).
    /// </summary>
    private static void AddGroundQuad(
        List<float> vertList, List<int> indexList,
        float minX, float maxX, float minZ, float maxZ, float y)
    {
        int b = vertList.Count / 3;

        vertList.AddRange(new float[] {
            minX, y, minZ,   // 0: SW
            maxX, y, minZ,   // 1: SE
            maxX, y, maxZ,   // 2: NE
            minX, y, maxZ,   // 3: NW
        });

        // CCW from above → normal +Y (walkable).
        indexList.AddRange(new int[] {
            b + 0, b + 2, b + 1,
            b + 0, b + 3, b + 2,
        });
    }
}
