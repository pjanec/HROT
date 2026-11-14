#nullable enable
namespace Fdp.Toolkit.Navigation;

/// <summary>
/// Seam for providing scene triangle geometry to <see cref="Fdp.Toolkit.Navigation.Recast.RecastNavmeshBaker"/>.
///
/// <para>
/// ⭐ <b>The triangles are in RECAST space (Y-up): X=East, Y=Up, Z=North</b> — the baker's input, <b>not</b> a navigation
/// API. The engine-wide navigation contract (<see cref="Fdp.Toolkit.Navigation.INavmeshProvider"/>) is Z-up
/// (DESIGN_Terrain_World W7 / R-182); the swizzle engine ⇄ Recast lives only INSIDE the Recast implementation and at the
/// Stride boundary. A Stride source gets Y-up for free (<c>FdpStrideTransform.ToStridePosition</c>); a source built from
/// engine (Z-up) geometry must swizzle <c>(x, y, z)</c> to <c>(x, z, y)</c> itself.
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
    /// RECAST space (X=East, Y=Up, Z=North).
    /// </param>
    /// <param name="indices">
    /// Flat array of triangle indices (3 indices per triangle): <c>[i0, i1, i2, …]</c>.
    /// </param>
    /// <returns><c>true</c> if geometry was successfully extracted; <c>false</c> otherwise.</returns>
    bool TryGetTriangles(out float[] verts, out int[] indices);
}
