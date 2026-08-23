#nullable enable
using System.Collections.Generic;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// Pure math helpers for extracting box geometry into a navmesh triangle soup (BATCH-18).
///
/// <para>
/// These helpers are in <c>Hrot.Stride.Core</c> (rather than <c>HrotStrideApp.Game</c>)
/// so they can be unit-tested headlessly from <c>Hrot.Stride.Core.Tests</c>.
/// <c>StrideSceneGeometrySource</c> in <c>HrotStrideApp.Game</c> delegates to these.
/// </para>
///
/// <para>
/// <b>Coordinate convention.</b>
/// Inputs and outputs use navmesh-query space: X=East, Y=altitude(up), Z=North
/// (same as Stride world space).
/// </para>
/// </summary>
public static class BoxGeometryHelper
{
    /// <summary>
    /// Appends 12 triangles (6 faces × 2) for a box defined by its shape-to-world matrix
    /// and half-extents in shape-local space.
    ///
    /// <para>
    /// Corner layout (local space, indices 0–7):
    /// <code>
    ///   0: (-hx,-hy,-hz)   1: (+hx,-hy,-hz)
    ///   2: (+hx,-hy,+hz)   3: (-hx,-hy,+hz)
    ///   4: (-hx,+hy,-hz)   5: (+hx,+hy,-hz)
    ///   6: (+hx,+hy,+hz)   7: (-hx,+hy,+hz)
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Faces are wound CCW when viewed from the outside (outward normal via right-hand rule).
    /// The top face (Y=+hy) is wound CCW from above → outward normal +Y = walkable (DotRecast).
    /// </para>
    /// </summary>
    /// <param name="shapeWorldMatrix">Transform from shape-local space to world space.</param>
    /// <param name="halfExtents">Half-extents of the box in shape-local space.</param>
    /// <param name="vertList">Flat vertex buffer (X,Y,Z triplets) to append to.</param>
    /// <param name="indexList">Index buffer to append to.</param>
    public static void ExtractBoxTriangles(
        SMath.Matrix  shapeWorldMatrix,
        SMath.Vector3 halfExtents,
        List<float>   vertList,
        List<int>     indexList)
    {
        float hx = halfExtents.X;
        float hy = halfExtents.Y;
        float hz = halfExtents.Z;

        // 8 corners in shape-local space.
        var local = new SMath.Vector3[]
        {
            new(-hx, -hy, -hz),  // 0: left-bottom-back
            new( hx, -hy, -hz),  // 1: right-bottom-back
            new( hx, -hy,  hz),  // 2: right-bottom-front
            new(-hx, -hy,  hz),  // 3: left-bottom-front
            new(-hx,  hy, -hz),  // 4: left-top-back
            new( hx,  hy, -hz),  // 5: right-top-back
            new( hx,  hy,  hz),  // 6: right-top-front
            new(-hx,  hy,  hz),  // 7: left-top-front
        };

        // Transform to world space and append to vertex list.
        int baseIdx = vertList.Count / 3;
        for (int i = 0; i < 8; i++)
        {
            SMath.Vector3 w;
            SMath.Vector3.TransformCoordinate(ref local[i], ref shapeWorldMatrix, out w);
            vertList.Add(w.X);
            vertList.Add(w.Y);
            vertList.Add(w.Z);
        }

        // Append a quad face as two CCW triangles: (a,b,c) and (a,c,d).
        void Face(int a, int b, int c, int d)
        {
            indexList.Add(baseIdx + a); indexList.Add(baseIdx + b); indexList.Add(baseIdx + c);
            indexList.Add(baseIdx + a); indexList.Add(baseIdx + c); indexList.Add(baseIdx + d);
        }

        // Top    (+Y): CCW from above → normal +Y (walkable for DotRecast).
        Face(4, 7, 6, 5);
        // Bottom (-Y): CCW from below → normal -Y.
        Face(0, 1, 2, 3);
        // Front  (+Z): CCW from +Z.
        Face(3, 2, 6, 7);
        // Back   (-Z): CCW from -Z.
        Face(1, 0, 4, 5);
        // Right  (+X): CCW from +X.
        Face(1, 5, 6, 2);
        // Left   (-X): CCW from -X.
        Face(0, 3, 7, 4);
    }

    /// <summary>
    /// Fallback: appends a conservative AABB box estimated from the entity's world-matrix
    /// column magnitudes (scale). The box is axis-aligned in world space.
    ///
    /// <para>
    /// The world-matrix columns represent the X, Y, Z basis vectors at their scaled lengths.
    /// Their magnitudes are the world-space scale of each axis. Half-extents = magnitude / 2.
    /// </para>
    /// </summary>
    /// <param name="worldMatrix">Entity world matrix (may include rotation, scale, translation).</param>
    /// <param name="vertList">Flat vertex buffer to append to.</param>
    /// <param name="indexList">Index buffer to append to.</param>
    public static void AabbToBox(
        SMath.Matrix worldMatrix,
        List<float>  vertList,
        List<int>    indexList)
    {
        // Translation column = world-space centre.
        var center = new SMath.Vector3(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);

        // Column magnitudes = world-space scale of each axis.
        float sx = new SMath.Vector3(worldMatrix.M11, worldMatrix.M12, worldMatrix.M13).Length();
        float sy = new SMath.Vector3(worldMatrix.M21, worldMatrix.M22, worldMatrix.M23).Length();
        float sz = new SMath.Vector3(worldMatrix.M31, worldMatrix.M32, worldMatrix.M33).Length();

        var halfExtents = new SMath.Vector3(sx * 0.5f, sy * 0.5f, sz * 0.5f);

        // Identity-rotation matrix centred at 'center'.
        SMath.Matrix aabbMatrix = SMath.Matrix.Translation(center);

        ExtractBoxTriangles(aabbMatrix, halfExtents, vertList, indexList);
    }
}
