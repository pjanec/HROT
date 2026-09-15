#nullable enable
namespace Hrot.Stride.Core;

/// <summary>
/// Seam for providing scene triangle geometry to <see cref="StrideNavmeshBaker"/>.
///
/// <para>
/// The geometry is in <b>navmesh-query space</b>, i.e. the same convention as
/// <see cref="Fdp.Toolkit.Navigation.INavmeshProvider"/>:
/// <c>System.Numerics.Vector3(x_east, altitude, z_north)</c> — identical to Stride world space
/// (X=East, Y=Up, Z=North), obtained by swizzling FDP positions through
/// <see cref="FdpStrideTransform.ToStridePosition"/>.
/// </para>
///
/// <para>
/// <b>Triangle winding.</b>
/// Triangles must be wound so that the surface normal points upward (+Y) for walkable
/// surfaces (DotRecast uses the right-hand rule; counter-clockwise winding from above
/// gives +Y normals).  Stride scene triangles extracted from <c>StaticColliderComponent</c>s
/// have outward-facing normals by convention; the extractor must verify or flip winding.
/// </para>
///
/// <para>
/// The concrete Stride implementation (<c>StrideSceneGeometrySource</c> in
/// <c>HrotStrideApp.Game</c>) walks the loaded MainScene's
/// <c>StaticColliderComponent</c>s and swizzles each vertex via
/// <c>FdpStrideTransform</c>.  That class requires a running Stride scene and is not
/// tested headlessly.  Everything else — baking, querying — is tested via synthetic soups
/// passed through this interface.
/// </para>
/// </summary>
public interface ISceneGeometrySource
{
    /// <summary>
    /// Tries to extract the triangle soup for the scene.
    /// </summary>
    /// <param name="verts">
    /// Flat array of vertex positions: <c>[x0, y0, z0, x1, y1, z1, …]</c> in
    /// navmesh-query space (X=East, Y=altitude, Z=North).
    /// </param>
    /// <param name="indices">
    /// Flat array of triangle indices (3 indices per triangle): <c>[i0, i1, i2, …]</c>.
    /// </param>
    /// <returns><c>true</c> if geometry was successfully extracted; <c>false</c> otherwise.</returns>
    bool TryGetTriangles(out float[] verts, out int[] indices);
}
